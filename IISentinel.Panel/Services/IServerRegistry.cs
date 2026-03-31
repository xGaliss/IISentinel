using IISentinel.Panel.Configuration;

namespace IISentinel.Panel.Services;

public interface IServerRegistry
{
    IReadOnlyList<ManagedServer> GetAll();
    ManagedServer? GetById(string id);
    ManagedServer GetDefault();
}