using Microsoft.Extensions.Options;
using Microsoft.Web.Administration;
using Serilog;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using IISEntinel.Agent;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5261);
});

const string AgentVersion = "0.1.0";

// =========================
// Serilog
// =========================
var logsRelativePath = builder.Configuration["Serilog:LogsPath"] ?? "Logs/iissentinel-.log";
var logsPath = Path.Combine(builder.Environment.ContentRootPath, logsRelativePath);
var logsDir = Path.GetDirectoryName(logsPath) ?? Path.Combine(builder.Environment.ContentRootPath, "Logs");

Directory.CreateDirectory(logsDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.File(
        path: logsPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// =========================
// Config
// =========================
builder.Services.Configure<AutoHealOptions>(
    builder.Configuration.GetSection("AutoHeal"));

builder.Services.Configure<CentralOptions>(
    builder.Configuration.GetSection("Central"));

var app = builder.Build();

var apiKey = builder.Configuration["ApiKey"]
             ?? throw new InvalidOperationException("ApiKey not configured.");

var autoHealOptions = app.Services.GetRequiredService<IOptions<AutoHealOptions>>().Value;
var centralOptions = app.Services.GetRequiredService<IOptions<CentralOptions>>().Value;

// Historial simple en memoria para cooldown por pool
var healTracker = new ConcurrentDictionary<string, HealState>();

// =========================
// Middleware de seguridad + auditoría básica
// =========================
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var method = context.Request.Method;
    var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    if (!context.Request.Headers.TryGetValue("x-api-key", out var key))
    {
        Log.Warning("Auth failed: missing API key. Method={Method} Path={Path} RemoteIp={RemoteIp}",
            method, path, remoteIp);

        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("API Key missing");
        return;
    }

    if (key != apiKey)
    {
        Log.Warning("Auth failed: invalid API key. Method={Method} Path={Path} RemoteIp={RemoteIp}",
            method, path, remoteIp);

        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("Invalid API Key");
        return;
    }

    Log.Information("API request authorized. Method={Method} Path={Path} RemoteIp={RemoteIp}",
        method, path, remoteIp);

    await next();
});

// =========================
// Logs
// =========================
app.MapGet("/logs/recent", (int? lines, string? file) =>
{
    try
    {
        if (!Directory.Exists(logsDir))
        {
            return Results.Ok(new
            {
                LogsDir = logsDir,
                Exists = false,
                Files = Array.Empty<string>()
            });
        }

        var files = Directory.GetFiles(logsDir, "iissentinel-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        if (files.Count == 0)
        {
            return Results.Ok(new
            {
                LogsDir = logsDir,
                Exists = true,
                Files = Array.Empty<string>()
            });
        }

        var selectedLines = lines.GetValueOrDefault(100);
        if (selectedLines <= 0)
            selectedLines = 100;

        string latestFile;

        if (!string.IsNullOrWhiteSpace(file))
        {
            latestFile = files.FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f), file, StringComparison.OrdinalIgnoreCase))
                ?? files.First();
        }
        else
        {
            latestFile = files.First();
        }

        List<string> logLines;
        using (var stream = new FileStream(latestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            var content = reader.ReadToEnd();
            logLines = content
                .Split(Environment.NewLine, StringSplitOptions.None)
                .TakeLast(selectedLines)
                .ToList();
        }

        return Results.Ok(new
        {
            LogsDir = logsDir,
            File = Path.GetFileName(latestFile),
            Files = files.Select(Path.GetFileName).ToList(),
            Lines = logLines
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new
        {
            Error = ex.ToString(),
            LogsDir = logsDir
        });
    }
});

app.MapGet("/logs/download", (string? file) =>
{
    try
    {
        if (!Directory.Exists(logsDir))
            return Results.NotFound("Logs folder not found");

        var files = Directory.GetFiles(logsDir, "iissentinel-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        if (files.Count == 0)
            return Results.NotFound("No log files");

        var selectedFile = !string.IsNullOrWhiteSpace(file)
            ? files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), file, StringComparison.OrdinalIgnoreCase))
            : files.First();

        if (selectedFile == null)
            return Results.NotFound("Requested log file not found");

        return Results.File(
            selectedFile,
            "text/plain",
            Path.GetFileName(selectedFile));
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error downloading log file");
        return Results.Problem(ex.ToString());
    }
});

// =========================
// Health
// =========================
app.MapGet("/", () =>
{
    Log.Information("Health endpoint called.");
    return "IISEntinel Agent running";
});

// =========================
// App Pools
// =========================
app.MapGet("/apppools", () =>
{
    try
    {
        using var serverManager = new ServerManager();

        var pools = serverManager.ApplicationPools
            .Select(p => new
            {
                Name = p.Name,
                State = p.State.ToString()
            })
            .ToList();

        Log.Information("Listed app pools. Count={Count}", pools.Count);
        return Results.Ok(pools);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error listing app pools");
        return Results.Problem(ex.ToString());
    }
});

app.MapPost("/apppools/{name}/recycle", (string name) =>
{
    try
    {
        using var serverManager = new ServerManager();

        var pool = serverManager.ApplicationPools[name];
        if (pool == null)
        {
            Log.Warning("Recycle requested for missing app pool {PoolName}", name);
            return Results.NotFound(new { message = "App pool not found" });
        }

        pool.Recycle();

        Log.Information("App pool recycled manually. Pool={PoolName}", name);
        return Results.Ok(new { message = $"Recycled {name}" });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error recycling app pool {PoolName}", name);
        return Results.Problem(ex.ToString());
    }
});

app.MapPost("/apppools/{name}/start", (string name) =>
{
    try
    {
        using var serverManager = new ServerManager();

        var pool = serverManager.ApplicationPools[name];
        if (pool == null)
        {
            Log.Warning("Start requested for missing app pool {PoolName}", name);
            return Results.NotFound(new { message = "App pool not found" });
        }

        var result = pool.Start();

        Log.Information("App pool start requested manually. Pool={PoolName} Result={Result}", name, result);
        return Results.Ok(new
        {
            message = $"Start attempted for {name}",
            result = result.ToString()
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error starting app pool {PoolName}", name);
        return Results.Problem(ex.ToString());
    }
});

app.MapPost("/apppools/{name}/stop", (string name) =>
{
    try
    {
        using var serverManager = new ServerManager();

        var pool = serverManager.ApplicationPools[name];
        if (pool == null)
        {
            Log.Warning("Stop requested for missing app pool {PoolName}", name);
            return Results.NotFound(new { message = "App pool not found" });
        }

        var result = pool.Stop();

        Log.Information("App pool stop requested manually. Pool={PoolName} Result={Result}", name, result);
        return Results.Ok(new
        {
            message = $"Stop attempted for {name}",
            result = result.ToString()
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error stopping app pool {PoolName}", name);
        return Results.Problem(ex.ToString());
    }
});

// =========================
// Sites
// =========================
app.MapGet("/sites", () =>
{
    try
    {
        using var serverManager = new ServerManager();

        var sites = serverManager.Sites
            .Select(s => new
            {
                Name = s.Name,
                State = s.State.ToString(),
                Bindings = s.Bindings.Select(b => b.BindingInformation).ToList()
            })
            .ToList();

        Log.Information("Listed sites. Count={Count}", sites.Count);
        return Results.Ok(sites);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error listing sites");
        return Results.Problem(ex.ToString());
    }
});

app.MapPost("/sites/{name}/start", (string name) =>
{
    try
    {
        using var serverManager = new ServerManager();

        var site = serverManager.Sites[name];
        if (site == null)
        {
            Log.Warning("Start requested for missing site {SiteName}", name);
            return Results.NotFound(new { message = "Site not found" });
        }

        var result = site.Start();

        Log.Information("Site start requested manually. Site={SiteName} Result={Result}", name, result);
        return Results.Ok(new
        {
            message = $"Start attempted for {name}",
            result = result.ToString()
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error starting site {SiteName}", name);
        return Results.Problem(ex.ToString());
    }
});

app.MapPost("/sites/{name}/stop", (string name) =>
{
    try
    {
        using var serverManager = new ServerManager();

        var site = serverManager.Sites[name];
        if (site == null)
        {
            Log.Warning("Stop requested for missing site {SiteName}", name);
            return Results.NotFound(new { message = "Site not found" });
        }

        var result = site.Stop();

        Log.Information("Site stop requested manually. Site={SiteName} Result={Result}", name, result);
        return Results.Ok(new
        {
            message = $"Stop attempted for {name}",
            result = result.ToString()
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error stopping site {SiteName}", name);
        return Results.Problem(ex.ToString());
    }
});

// =========================
// Auto-heal
// =========================
if (autoHealOptions.Enabled)
{
    Log.Information("Auto-heal enabled. IntervalSeconds={Interval} MaxAttempts={MaxAttempts} CooldownMinutes={CooldownMinutes}",
        autoHealOptions.CheckIntervalSeconds,
        autoHealOptions.MaxAttempts,
        autoHealOptions.CooldownMinutes);

    _ = Task.Run(async () =>
    {
        while (true)
        {
            try
            {
                using var serverManager = new ServerManager();

                foreach (var pool in serverManager.ApplicationPools)
                {
                    if (autoHealOptions.Pools.Count > 0 &&
                        !autoHealOptions.Pools.Contains(pool.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Log.Information("Pool check. Pool={PoolName} State={State}", pool.Name, pool.State);

                    if (pool.State != ObjectState.Stopped)
                        continue;

                    var state = healTracker.GetOrAdd(pool.Name, _ => new HealState());

                    var now = DateTime.UtcNow;
                    var cooldownWindow = TimeSpan.FromMinutes(autoHealOptions.CooldownMinutes);

                    if (now - state.WindowStartUtc > cooldownWindow)
                    {
                        state.WindowStartUtc = now;
                        state.Attempts = 0;
                        Log.Information("Auto-heal window reset. Pool={PoolName}", pool.Name);
                    }

                    if (state.Attempts >= autoHealOptions.MaxAttempts)
                    {
                        Log.Warning(
                            "Auto-heal blocked by cooldown. Pool={PoolName} Attempts={Attempts} WindowStartUtc={WindowStartUtc}",
                            pool.Name, state.Attempts, state.WindowStartUtc);

                        continue;
                    }

                    Log.Warning("Auto-heal attempting start. Pool={PoolName} Attempt={Attempt}",
                        pool.Name, state.Attempts + 1);

                    var result = pool.Start();
                    state.Attempts++;

                    Log.Information(
                        "Auto-heal start attempted. Pool={PoolName} Result={Result} AttemptsInWindow={Attempts} CurrentState={State}",
                        pool.Name, result, state.Attempts, pool.State);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled error in auto-heal loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(autoHealOptions.CheckIntervalSeconds));
        }
    });
}
else
{
    Log.Information("Auto-heal disabled.");
}

try
{
    await TryEnrollWithCentralAsync(centralOptions);
}
catch (Exception ex)
{
    Log.Warning(ex, "Central enrollment attempt failed during startup.");
}

await StartHeartbeatLoopAsync(centralOptions);
await StartCommandPollingLoopAsync(centralOptions);

try
{
    Log.Information("Starting IISEntinel Agent");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "IISEntinel Agent terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static async Task TryEnrollWithCentralAsync(CentralOptions centralOptions)
{
    if (string.IsNullOrWhiteSpace(centralOptions.BaseUrl) ||
        string.IsNullOrWhiteSpace(centralOptions.EnrollmentToken))
    {
        Log.Warning("Central enrollment skipped: Central:BaseUrl or Central:EnrollmentToken is missing.");
        return;
    }

    var identity = await LoadOrCreateIdentityAsync();

    var hostname = Environment.MachineName;
    var fqdn = GetFqdn();

    var request = new AgentEnrollRequest
    {
        EnrollmentToken = centralOptions.EnrollmentToken,
        AgentIdentifier = identity.AgentIdentifier,
        Hostname = hostname,
        Fqdn = fqdn,
        AgentVersion = AgentVersion
    };

    using var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };

    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri(centralOptions.BaseUrl),
        Timeout = TimeSpan.FromSeconds(15)
    };

    Log.Information("Attempting enrollment with Central. BaseUrl={BaseUrl} AgentIdentifier={AgentIdentifier}",
        centralOptions.BaseUrl, identity.AgentIdentifier);

    try
    {
        var response = await client.PostAsJsonAsync("/api/agents/enroll", request);

        var responseText = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("Central enrollment failed. StatusCode={StatusCode} Response={Response}",
                (int)response.StatusCode, responseText);
            return;
        }

        var enrollResponse = JsonSerializer.Deserialize<AgentEnrollResponse>(responseText);

        if (enrollResponse == null)
        {
            Log.Warning("Central enrollment returned empty or invalid JSON. Response={Response}", responseText);
            return;
        }

        Log.Information("Central enrollment successful. AgentId={AgentId} Status={Status}",
            enrollResponse.AgentId, enrollResponse.Status);
    }
    catch (TaskCanceledException ex)
    {
        Log.Warning(ex, "Central enrollment timed out. BaseUrl={BaseUrl}", centralOptions.BaseUrl);
    }
    catch (HttpRequestException ex)
    {
        Log.Warning(ex, "Central enrollment HTTP error. BaseUrl={BaseUrl}", centralOptions.BaseUrl);
    }
}

static Task StartHeartbeatLoopAsync(CentralOptions centralOptions)
{
    _ = Task.Run(async () =>
    {
        while (true)
        {
            try
            {
                await SendHeartbeatAsync(centralOptions);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Heartbeat loop iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30));
        }
    });

    return Task.CompletedTask;
}

static async Task SendHeartbeatAsync(CentralOptions centralOptions)
{
    if (string.IsNullOrWhiteSpace(centralOptions.BaseUrl))
    {
        Log.Warning("Heartbeat skipped: Central:BaseUrl is missing.");
        return;
    }

    var identity = await LoadOrCreateIdentityAsync();

    using var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };

    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri(centralOptions.BaseUrl),
        Timeout = TimeSpan.FromSeconds(15)
    };

    var request = new AgentHeartbeatRequest
    {
        AgentIdentifier = identity.AgentIdentifier,
        AgentVersion = AgentVersion
    };

    var response = await client.PostAsJsonAsync("/api/agents/heartbeat", request);
    var responseText = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        Log.Warning("Heartbeat rejected. StatusCode={StatusCode} Response={Response}",
            (int)response.StatusCode, responseText);
        return;
    }

    Log.Information("Heartbeat sent successfully. AgentIdentifier={AgentIdentifier}",
        identity.AgentIdentifier);
}

static async Task<AgentIdentity> LoadOrCreateIdentityAsync()
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
            var json = await File.ReadAllTextAsync(identityPath);
            var existing = JsonSerializer.Deserialize<AgentIdentity>(json);

            if (existing != null && !string.IsNullOrWhiteSpace(existing.AgentIdentifier))
            {
                return existing;
            }
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

    var newJson = JsonSerializer.Serialize(identity, new JsonSerializerOptions
    {
        WriteIndented = true
    });

    await File.WriteAllTextAsync(identityPath, newJson);

    Log.Information("Created new agent identity. AgentIdentifier={AgentIdentifier} Path={Path}",
        identity.AgentIdentifier, identityPath);

    return identity;
}

static string GetFqdn()
{
    try
    {
        return System.Net.Dns.GetHostEntry(Environment.MachineName).HostName;
    }
    catch
    {
        return Environment.MachineName;
    }
}

static Task StartCommandPollingLoopAsync(CentralOptions centralOptions)
{
    _ = Task.Run(async () =>
    {
        while (true)
        {
            try
            {
                await PollAndExecuteNextCommandAsync(centralOptions);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Command polling loop iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
        }
    });

    return Task.CompletedTask;
}

static async Task PollAndExecuteNextCommandAsync(CentralOptions centralOptions)
{
    if (string.IsNullOrWhiteSpace(centralOptions.BaseUrl))
    {
        Log.Warning("Command polling skipped: Central:BaseUrl is missing.");
        return;
    }

    var identity = await LoadOrCreateIdentityAsync();

    using var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    };

    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri(centralOptions.BaseUrl),
        Timeout = TimeSpan.FromSeconds(20)
    };

    var response = await client.GetAsync($"/api/agents/{identity.AgentIdentifier}/actions/next");

    if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        return;

    var responseText = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        Log.Warning("Command polling rejected. StatusCode={StatusCode} Response={Response}",
            (int)response.StatusCode, responseText);
        return;
    }

    var command = JsonSerializer.Deserialize<AgentCommandDto>(responseText,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (command is null)
    {
        Log.Warning("Command polling returned invalid JSON. Response={Response}", responseText);
        return;
    }

    Log.Information("Received command from Central. ActionId={ActionId} ActionType={ActionType} Target={Target}",
        command.Id, command.ActionType, command.TargetName);

    try
    {
        var resultMessage = ExecuteLocalCommand(command);

        var completeResponse = await client.PostAsJsonAsync(
            $"/api/agents/{identity.AgentIdentifier}/actions/{command.Id}/complete",
            new AgentActionResultRequest
            {
                ResultMessage = resultMessage
            });

        if (!completeResponse.IsSuccessStatusCode)
        {
            var completeText = await completeResponse.Content.ReadAsStringAsync();
            Log.Warning("Complete callback failed. StatusCode={StatusCode} Response={Response}",
                (int)completeResponse.StatusCode, completeText);
            return;
        }

        Log.Information("Command completed successfully. ActionId={ActionId}", command.Id);
    }
    catch (Exception ex)
    {
        var failResponse = await client.PostAsJsonAsync(
            $"/api/agents/{identity.AgentIdentifier}/actions/{command.Id}/fail",
            new AgentActionResultRequest
            {
                Error = ex.ToString(),
                ResultMessage = ex.Message
            });

        if (!failResponse.IsSuccessStatusCode)
        {
            var failText = await failResponse.Content.ReadAsStringAsync();
            Log.Warning("Fail callback failed. StatusCode={StatusCode} Response={Response}",
                (int)failResponse.StatusCode, failText);
        }

        Log.Warning(ex, "Command failed. ActionId={ActionId}", command.Id);
    }
}

static string ExecuteLocalCommand(AgentCommandDto command)
{
    using var serverManager = new ServerManager();

    switch (command.ActionType)
    {
        case "AppPoolRecycle":
            {
                var pool = serverManager.ApplicationPools[command.TargetName]
                    ?? throw new InvalidOperationException($"App pool '{command.TargetName}' not found.");

                pool.Recycle();
                return $"Recycled app pool '{command.TargetName}'.";
            }

        case "AppPoolStart":
            {
                var pool = serverManager.ApplicationPools[command.TargetName]
                    ?? throw new InvalidOperationException($"App pool '{command.TargetName}' not found.");

                var result = pool.Start();
                return $"Start attempted for app pool '{command.TargetName}'. Result={result}";
            }

        case "AppPoolStop":
            {
                var pool = serverManager.ApplicationPools[command.TargetName]
                    ?? throw new InvalidOperationException($"App pool '{command.TargetName}' not found.");

                var result = pool.Stop();
                return $"Stop attempted for app pool '{command.TargetName}'. Result={result}";
            }

        case "SiteStart":
            {
                var site = serverManager.Sites[command.TargetName]
                    ?? throw new InvalidOperationException($"Site '{command.TargetName}' not found.");

                var result = site.Start();
                return $"Start attempted for site '{command.TargetName}'. Result={result}";
            }

        case "SiteStop":
            {
                var site = serverManager.Sites[command.TargetName]
                    ?? throw new InvalidOperationException($"Site '{command.TargetName}' not found.");

                var result = site.Stop();
                return $"Stop attempted for site '{command.TargetName}'. Result={result}";
            }

        default:
            throw new InvalidOperationException($"Unsupported action type '{command.ActionType}'.");
    }
}

sealed class HealState
{
    public int Attempts { get; set; } = 0;
    public DateTime WindowStartUtc { get; set; } = DateTime.UtcNow;
}