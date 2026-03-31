using IISentinel.Panel.Configuration;
using Microsoft.Extensions.Options;

namespace IISentinel.Panel.Services;

public class ServerRegistry : IServerRegistry
{
    private readonly ManagedServersOptions _options;

    public ServerRegistry(IOptions<ManagedServersOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<ManagedServer> GetAll()
    {
        return _options.Servers;
    }

    public ManagedServer? GetById(string id)
    {
        return _options.Servers.FirstOrDefault(s =>
            s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public ManagedServer GetDefault()
    {
        return _options.Servers.First();
    }
}