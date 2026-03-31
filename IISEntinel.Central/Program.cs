using IISEntinel.Central.Data;
using IISEntinel.Central.Endpoints;
using IISEntinel.Central.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("https://0.0.0.0:7016", "http://0.0.0.0:5008");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddServerSideBlazor()
    .AddCircuitOptions(options =>
    {
        options.DetailedErrors = true;
    });

builder.Services.AddDbContext<CentralDbContext>(options =>
    options.UseSqlite("Data Source=IISEntinelCentral.db"));

var app = builder.Build();

app.UseDeveloperExceptionPage();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<IISEntinel.Central.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapAgentEndpoints();
app.MapAgentManagementEndpoints();
app.MapAgentActionEndpoints();

app.MapPost("/api/agents/{id:guid}/approve", async (Guid id, CentralDbContext db) =>
{
    var agent = await db.Agents.FirstOrDefaultAsync(x => x.Id == id);
    if (agent is null)
        return Results.NotFound();

    if (agent.Status != "Approved")
    {
        agent.Status = "Approved";
        agent.ApprovedUtc = DateTime.UtcNow;
        agent.RevokedUtc = null;

        await db.SaveChangesAsync();
    }

    return Results.Ok(new
    {
        agent.Id,
        agent.AgentIdentifier,
        agent.Status,
        agent.ApprovedUtc
    });
});


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CentralDbContext>();
    db.Database.EnsureCreated();

    if (!db.EnrollmentTokens.Any(t => t.TokenHash == "test"))
    {
        db.EnrollmentTokens.Add(new IISEntinel.Central.Models.EnrollmentToken
        {
            Id = Guid.NewGuid(),
            Name = "Token inicial",
            TokenHash = "test",
            CreatedUtc = DateTime.UtcNow,
            MaxUses = 10,
            UsedCount = 0,
            IsDisabled = false
        });

        db.SaveChanges();
    }
}

app.Run();