using IISEntinel.Central.Models;

namespace IISEntinel.Central.Services;

public interface IAgentService
{
    Task<List<Agent>> GetAgentsAsync();
    Task<bool> ApproveAgentAsync(Guid id);
}