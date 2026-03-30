using Microsoft.Web.Administration;
using Microsoft.Extensions.Options;
using IISEntinel.Agent;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AutoHealOptions>(
    builder.Configuration.GetSection("AutoHeal"));

var app = builder.Build();

var apiKey = builder.Configuration["ApiKey"] ?? throw new InvalidOperationException("ApiKey not configured.");
var autoHealOptions = app.Services.GetRequiredService<IOptions<AutoHealOptions>>().Value;

// Historial simple en memoria para cooldown por pool
var healTracker = new ConcurrentDictionary<string, HealState>();

app.Use(async (context, next) =>
{
    if (!context.Request.Headers.TryGetValue("x-api-key", out var key))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("API Key missing");
        return;
    }

    if (key != apiKey)
    {
        context.Response.StatusCode = 403;
        await context.Response.WriteAsync("Invalid API Key");
        return;
    }

    await next();
});

app.MapGet("/", () => "IISEntinel Agent running");

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

        return Results.Ok(pools);
    }
    catch (Exception ex)
    {
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
            return Results.NotFound(new { message = "App pool not found" });

        pool.Recycle();

        return Results.Ok(new { message = $"Recycled {name}" });
    }
    catch (Exception ex)
    {
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
            return Results.NotFound(new { message = "App pool not found" });

        var result = pool.Start();

        return Results.Ok(new
        {
            message = $"Start attempted for {name}",
            result = result.ToString()
        });
    }
    catch (Exception ex)
    {
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
            return Results.NotFound(new { message = "App pool not found" });

        var result = pool.Stop();

        return Results.Ok(new
        {
            message = $"Stop attempted for {name}",
            result = result.ToString()
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString());
    }
});

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

        return Results.Ok(sites);
    }
    catch (Exception ex)
    {
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
            return Results.NotFound(new { message = "Site not found" });

        var result = site.Start();

        return Results.Ok(new
        {
            message = $"Start attempted for {name}",
            result = result.ToString()
        });
    }
    catch (Exception ex)
    {
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
            return Results.NotFound(new { message = "Site not found" });

        var result = site.Stop();

        return Results.Ok(new
        {
            message = $"Stop attempted for {name}",
            result = result.ToString()
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString());
    }
});

// Auto-heal
if (autoHealOptions.Enabled)
{
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

                    Console.WriteLine($"[CHECK] Pool {pool.Name} - State: {pool.State}");

                    if (pool.State != ObjectState.Stopped)
                        continue;

                    var state = healTracker.GetOrAdd(pool.Name, _ => new HealState());

                    var now = DateTime.UtcNow;
                    var cooldownWindow = TimeSpan.FromMinutes(autoHealOptions.CooldownMinutes);

                    if (now - state.WindowStartUtc > cooldownWindow)
                    {
                        state.WindowStartUtc = now;
                        state.Attempts = 0;
                    }

                    if (state.Attempts >= autoHealOptions.MaxAttempts)
                    {
                        Console.WriteLine($"[AUTOHEAL] Max attempts reached for {pool.Name}. Cooldown active until {state.WindowStartUtc.Add(cooldownWindow):u}");
                        continue;
                    }

                    Console.WriteLine($"[AUTOHEAL] Attempting to start pool {pool.Name}");

                    var result = pool.Start();
                    state.Attempts++;

                    Console.WriteLine($"[AUTOHEAL] Start() result for {pool.Name}: {result}");
                    Console.WriteLine($"[AUTOHEAL] Attempt #{state.Attempts} in current window for {pool.Name}");

                    // Releer estado en la siguiente vuelta es suficiente, pero dejamos traza
                    Console.WriteLine($"[AUTOHEAL] Current state after start attempt for {pool.Name}: {pool.State}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex}");
            }

            await Task.Delay(TimeSpan.FromSeconds(autoHealOptions.CheckIntervalSeconds));
        }
    });
}

app.Run();

sealed class HealState
{
    public int Attempts { get; set; } = 0;
    public DateTime WindowStartUtc { get; set; } = DateTime.UtcNow;
}