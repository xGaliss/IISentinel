using Microsoft.Web.Administration;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

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
                Bindings = s.Bindings.Select(b => b.BindingInformation)
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
    using var serverManager = new ServerManager();
    var site = serverManager.Sites[name];

    if (site == null) return Results.NotFound();

    site.Start();
    return Results.Ok();
});

app.MapPost("/sites/{name}/stop", (string name) =>
{
    using var serverManager = new ServerManager();
    var site = serverManager.Sites[name];

    if (site == null) return Results.NotFound();

    site.Stop();
    return Results.Ok();
});

app.Run();