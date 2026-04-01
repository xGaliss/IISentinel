using Microsoft.Extensions.Options;
using Serilog;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IISEntinel.Agent;

public sealed class AgentLogShippingService : IAgentLogShippingService
{
    private static readonly Regex LogRegex = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\s+\[(?<lvl>[A-Z]{3})\]\s+(?<msg>.*)$",
        RegexOptions.Compiled);

    private readonly CentralOptions _centralOptions;
    private readonly AgentLogShippingOptions _options;
    private readonly string _contentRootPath;

    public AgentLogShippingService(
        IOptions<CentralOptions> centralOptions,
        IOptions<AgentLogShippingOptions> options,
        IWebHostEnvironment env)
    {
        _centralOptions = centralOptions.Value;
        _options = options.Value;
        _contentRootPath = env.ContentRootPath;
    }

    public async Task ShipOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(_centralOptions.BaseUrl))
            return;

        var identity = await Program_LoadOrCreateIdentityAsync();
        var latestFile = GetLatestLogFile();

        if (latestFile is null || !File.Exists(latestFile))
            return;

        var statePath = GetAbsoluteStatePath();
        var state = await LoadStateAsync(statePath, latestFile, cancellationToken);

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

        List<AgentLogEntryDto> entries = new();
        long newPosition;

        using (var stream = new FileStream(latestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            stream.Seek(state.LastPosition, SeekOrigin.Begin);

            while (!reader.EndOfStream && entries.Count < _options.BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parsed = ParseLine(line);
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
            $"/api/agents/{identity.AgentId}/logs/batch",
            new AgentLogBatchRequest { Entries = entries },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            Log.Warning("Log shipping failed. StatusCode={StatusCode} Response={Response}",
                (int)response.StatusCode, text);
            return;
        }

        state.FilePath = latestFile;
        state.LastPosition = newPosition;
        state.UpdatedUtc = DateTime.UtcNow;

        await SaveStateAsync(statePath, state, cancellationToken);

        Log.Information("Log shipping succeeded. AgentId={AgentId} SentEntries={Count} File={File}",
            identity.AgentId, entries.Count, Path.GetFileName(latestFile));
    }

    private string? GetLatestLogFile()
    {
        var logsRelativePath = "Logs";
        var logsDir = Path.Combine(_contentRootPath, logsRelativePath);

        if (!Directory.Exists(logsDir))
            return null;

        return Directory.GetFiles(logsDir, "iissentinel-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private string GetAbsoluteStatePath()
    {
        return Path.IsPathRooted(_options.StateFilePath)
            ? _options.StateFilePath
            : Path.Combine(_contentRootPath, _options.StateFilePath);
    }

    private static AgentLogEntryDto? ParseLine(string line)
    {
        var match = LogRegex.Match(line);
        if (!match.Success)
            return null;

        if (!DateTime.TryParse(match.Groups["ts"].Value, out var ts))
            return null;

        var level = match.Groups["lvl"].Value;
        var message = match.Groups["msg"].Value;

        return new AgentLogEntryDto
        {
            TimestampUtc = DateTime.SpecifyKind(ts, DateTimeKind.Local).ToUniversalTime(),
            Level = level,
            Category = InferCategory(message),
            Message = message,
            EventType = InferEventType(message),
            CorrelationId = null
        };
    }

    private static string InferCategory(string message)
    {
        if (message.Contains("Heartbeat", StringComparison.OrdinalIgnoreCase)) return "Heartbeat";
        if (message.Contains("Inventory", StringComparison.OrdinalIgnoreCase)) return "Inventory";
        if (message.Contains("Command", StringComparison.OrdinalIgnoreCase)) return "Command";
        if (message.Contains("Auto-heal", StringComparison.OrdinalIgnoreCase)) return "AutoHeal";
        if (message.Contains("enrollment", StringComparison.OrdinalIgnoreCase)) return "Enrollment";
        return "Agent";
    }

    private static string? InferEventType(string message)
    {
        if (message.Contains("sent successfully", StringComparison.OrdinalIgnoreCase)) return "Success";
        if (message.Contains("failed", StringComparison.OrdinalIgnoreCase)) return "Failure";
        if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase)) return "Timeout";
        return null;
    }

    private static async Task<AgentLogShippingState> LoadStateAsync(
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

    private static async Task SaveStateAsync(
        string statePath,
        AgentLogShippingState state,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(statePath, json, cancellationToken);
    }

    // puente simple para reutilizar tu método actual si sigue en Program.cs
    private static Task<AgentIdentity> Program_LoadOrCreateIdentityAsync()
        => LoadOrCreateIdentityBridge();

    private static Func<Task<AgentIdentity>> LoadOrCreateIdentityBridge = default!;

    public static void ConfigureIdentityLoader(Func<Task<AgentIdentity>> loader)
        => LoadOrCreateIdentityBridge = loader;
}