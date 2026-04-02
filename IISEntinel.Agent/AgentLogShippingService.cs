using Microsoft.Extensions.Options;
using Serilog;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IISEntinel.Agent;

public sealed class AgentLogShippingService : IAgentLogShippingService
{
    private static readonly Regex LogLineRegex = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\s+\[(?<lvl>[A-Z]{3})\]\s+(?<msg>.*)$",
        RegexOptions.Compiled);

    private readonly CentralOptions _centralOptions;
    private readonly AgentLogShippingOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly string _logsDir;

    public AgentLogShippingService(
        IOptions<CentralOptions> centralOptions,
        IOptions<AgentLogShippingOptions> options,
        IWebHostEnvironment environment)
    {
        _centralOptions = centralOptions.Value;
        _options = options.Value;
        _environment = environment;

        var logsRelativePath = "Logs/iissentinel-.log";
        var logsPath = Path.Combine(_environment.ContentRootPath, logsRelativePath);
        _logsDir = Path.GetDirectoryName(logsPath) ?? Path.Combine(_environment.ContentRootPath, "Logs");
    }

    public async Task PushRecentLogsAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(_centralOptions.BaseUrl))
            return;

        if (!Directory.Exists(_logsDir))
            return;

        var identity = await LoadOrCreateIdentityAsync(cancellationToken);
        if (identity.AgentId is null || identity.AgentId == Guid.Empty)
        {
            Log.Warning("Log shipping skipped: AgentId is missing in local identity.");
            return;
        }

        var latestFile = Directory.GetFiles(_logsDir, "iissentinel-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(latestFile) || !File.Exists(latestFile))
            return;

        var statePath = Path.IsPathRooted(_options.StateFilePath)
            ? _options.StateFilePath
            : Path.Combine(_environment.ContentRootPath, _options.StateFilePath);

        var state = await LoadLogShippingStateAsync(statePath, latestFile, cancellationToken);

        var fileInfo = new FileInfo(latestFile);
        if (!string.Equals(state.FilePath, latestFile, StringComparison.OrdinalIgnoreCase) ||
            state.LastPosition > fileInfo.Length)
        {
            state = new AgentLogShippingState
            {
                FilePath = latestFile,
                LastPosition = 0,
                UpdatedUtc = DateTime.UtcNow
            };
        }

        var entries = new List<AgentLogEntryDto>();
        long newPosition;

        using (var stream = new FileStream(latestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            stream.Seek(state.LastPosition, SeekOrigin.Begin);

            while (!reader.EndOfStream && entries.Count < _options.BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parsed = ParseLogLine(line);
                if (parsed is not null)
                    entries.Add(parsed);
            }

            newPosition = stream.Position;
        }

        if (entries.Count == 0)
            return;

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(_centralOptions.BaseUrl),
            Timeout = TimeSpan.FromSeconds(20)
        };

        var response = await client.PostAsJsonAsync(
            $"/api/agents/{identity.AgentId.Value}/logs/batch",
            new AgentLogBatchRequest
            {
                Entries = entries
            },
            cancellationToken);

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Log shipping rejected. StatusCode={StatusCode} Response={Response}",
                (int)response.StatusCode, responseText);
            return;
        }

        state.FilePath = latestFile;
        state.LastPosition = newPosition;
        state.UpdatedUtc = DateTime.UtcNow;

        await SaveLogShippingStateAsync(statePath, state, cancellationToken);

        Log.Information("Log shipping sent successfully. AgentId={AgentId} Entries={Count}",
            identity.AgentId.Value, entries.Count);
    }

    private static AgentLogEntryDto? ParseLogLine(string line)
    {
        var match = LogLineRegex.Match(line);
        if (!match.Success)
            return null;

        if (!DateTime.TryParse(match.Groups["ts"].Value, out var timestamp))
            return null;

        var message = match.Groups["msg"].Value;

        return new AgentLogEntryDto
        {
            TimestampUtc = DateTime.SpecifyKind(timestamp, DateTimeKind.Local).ToUniversalTime(),
            Level = match.Groups["lvl"].Value,
            Category = InferLogCategory(message),
            Message = message,
            EventType = InferLogEventType(message),
            CorrelationId = null
        };
    }

    private static string InferLogCategory(string message)
    {
        if (message.Contains("Heartbeat", StringComparison.OrdinalIgnoreCase))
            return "Heartbeat";

        if (message.Contains("Inventory", StringComparison.OrdinalIgnoreCase))
            return "Inventory";

        if (message.Contains("Command", StringComparison.OrdinalIgnoreCase))
            return "Command";

        if (message.Contains("Auto-heal", StringComparison.OrdinalIgnoreCase))
            return "AutoHeal";

        if (message.Contains("enrollment", StringComparison.OrdinalIgnoreCase))
            return "Enrollment";

        return "Agent";
    }

    private static string? InferLogEventType(string message)
    {
        if (message.Contains("sent successfully", StringComparison.OrdinalIgnoreCase))
            return "Success";

        if (message.Contains("completed successfully", StringComparison.OrdinalIgnoreCase))
            return "Success";

        if (message.Contains("failed", StringComparison.OrdinalIgnoreCase))
            return "Failure";

        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return "Timeout";

        return null;
    }

    private async Task<AgentIdentity> LoadOrCreateIdentityAsync(CancellationToken cancellationToken)
    {
        var identityDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "IISEntinel");

        var identityPath = Path.Combine(identityDir, "agent.identity.json");

        Directory.CreateDirectory(identityDir);

        if (File.Exists(identityPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(identityPath, cancellationToken);
                var existing = JsonSerializer.Deserialize<AgentIdentity>(json);

                if (existing != null && !string.IsNullOrWhiteSpace(existing.AgentIdentifier))
                    return existing;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read existing agent identity. A new one will be created.");
            }
        }

        var identity = new AgentIdentity
        {
            AgentIdentifier = Guid.NewGuid().ToString("D"),
            CreatedUtc = DateTime.UtcNow
        };

        await SaveIdentityAsync(identity, cancellationToken);

        Log.Information("Created new agent identity. AgentIdentifier={AgentIdentifier}",
            identity.AgentIdentifier);

        return identity;
    }

    private static async Task SaveIdentityAsync(AgentIdentity identity, CancellationToken cancellationToken)
    {
        var identityDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "IISEntinel");

        var identityPath = Path.Combine(identityDir, "agent.identity.json");

        Directory.CreateDirectory(identityDir);

        var json = JsonSerializer.Serialize(identity, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(identityPath, json, cancellationToken);
    }

    private static async Task<AgentLogShippingState> LoadLogShippingStateAsync(
        string statePath,
        string currentFile,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(statePath))
            {
                return new AgentLogShippingState
                {
                    FilePath = currentFile,
                    LastPosition = 0,
                    UpdatedUtc = DateTime.UtcNow
                };
            }

            var json = await File.ReadAllTextAsync(statePath, cancellationToken);
            var state = JsonSerializer.Deserialize<AgentLogShippingState>(json);

            return state ?? new AgentLogShippingState
            {
                FilePath = currentFile,
                LastPosition = 0,
                UpdatedUtc = DateTime.UtcNow
            };
        }
        catch
        {
            return new AgentLogShippingState
            {
                FilePath = currentFile,
                LastPosition = 0,
                UpdatedUtc = DateTime.UtcNow
            };
        }
    }

    private static async Task SaveLogShippingStateAsync(
        string statePath,
        AgentLogShippingState state,
        CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(statePath, json, cancellationToken);
    }
}