using Microsoft.Extensions.Options;
using Microsoft.Web.Administration;
using Serilog;
using System.Collections.Concurrent;
using IISEntinel.Agent;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

var apiKey = builder.Configuration["ApiKey"]
             ?? throw new InvalidOperationException("ApiKey not configured.");

var autoHealOptions = app.Services.GetRequiredService<IOptions<AutoHealOptions>>().Value;

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
app.MapGet("/logs/recent", () =>
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

        var latestFile = files.First();

        List<string> lines;
        using (var stream = new FileStream(latestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            var content = reader.ReadToEnd();
            lines = content
                .Split(Environment.NewLine, StringSplitOptions.None)
                .TakeLast(100)
                .ToList();
        }

        return Results.Ok(new
        {
            LogsDir = logsDir,
            File = latestFile,
            Files = files.Select(Path.GetFileName).ToList(),
            Lines = lines
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

sealed class HealState
{
    public int Attempts { get; set; } = 0;
    public DateTime WindowStartUtc { get; set; } = DateTime.UtcNow;
}