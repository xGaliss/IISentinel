using IISEntinel.Central.Data;
using IISEntinel.Central.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("https://0.0.0.0:7016", "http://0.0.0.0:5008");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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