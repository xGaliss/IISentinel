using IISentinel.Panel.Components;
using IISentinel.Panel.Configuration;
using IISentinel.Panel.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ManagedServersOptions>(
    builder.Configuration.GetSection("ManagedServers"));

builder.Services.AddSingleton<IServerRegistry, ServerRegistry>();

builder.Services.AddScoped<IAgentApiClient, AgentApiClient>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient("Agent")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        };
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();