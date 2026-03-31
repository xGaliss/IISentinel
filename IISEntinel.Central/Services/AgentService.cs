using IISEntinel.Central.Data;
using IISEntinel.Central.Models;
using Microsoft.EntityFrameworkCore;

namespace IISEntinel.Central.Services;

public class AgentService : IAgentService
{
    private readonly CentralDbContext _db;

    public AgentService(CentralDbContext db)
    {
        _db = db;
    }

    public Task<List<Agent>> GetAgentsAsync()
    {
        return _db.Agents
            .OrderByDescending(x => x.LastSeenUtc ?? x.CreatedUtc)
            .ThenBy(x => x.DisplayName)
            .ToListAsync();
    }

    public async Task<bool> ApproveAgentAsync(Guid id)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(x => x.Id == id);
        if (agent is null)
            return false;

        if (string.Equals(agent.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            return true;

        agent.Status = "Approved";
        agent.ApprovedUtc = DateTime.UtcNow;
        agent.RevokedUtc = null;

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> QueueActionAsync(Guid agentId, string actionType, string targetName)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(x => x.Id == agentId);
        if (agent is null)
            return false;

        if (!string.Equals(agent.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            return false;

        _db.AgentActions.Add(new AgentAction
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            ActionType = actionType,
            TargetName = targetName,
            Status = "Pending",
            RequestedBy = "CentralUI",
            CreatedUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return true;
    }
}
