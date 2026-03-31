using System.Net.Http.Headers;
using IISentinel.Panel.Configuration;

namespace IISentinel.Panel.Services;

public class AgentApiClient : IAgentApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServerRegistry _serverRegistry;

    public AgentApiClient(IHttpClientFactory httpClientFactory, IServerRegistry serverRegistry)
    {
        _httpClientFactory = httpClientFactory;
        _serverRegistry = serverRegistry;
    }

    public Task<string> GetAppPoolsRawAsync(string serverId)
        => GetAsync(serverId, "/apppools");

    public Task<string> GetSitesRawAsync(string serverId)
        => GetAsync(serverId, "/sites");

    public Task<string> GetLogsRawAsync(string serverId)
        => GetAsync(serverId, "/logs");

    private async Task<string> GetAsync(string serverId, string path)
    {
        var server = _serverRegistry.GetById(serverId)
                     ?? throw new InvalidOperationException($"Servidor no encontrado: {serverId}");

        var client = _httpClientFactory.CreateClient("Agent");
        client.BaseAddress = new Uri(server.BaseUrl);
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("x-api-key", server.ApiKey);

        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }
}