using System.Text.Json;

namespace IISentinel.Panel.Services;

public interface IAgentApiClient
{
    Task<string> GetAppPoolsRawAsync(string serverId);
    Task<string> GetSitesRawAsync(string serverId);
    Task<string> GetLogsRawAsync(string serverId);
}