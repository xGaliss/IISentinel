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

    public async Task<List<Agent>> GetAgentsAsync()
    {
        return await _db.Agents
            .OrderByDescending(x => x.LastSeenUtc ?? x.CreatedUtc)
            .ThenBy(x => x.DisplayName)
            .ToListAsync();
    }

    public async Task<bool> ApproveAgentAsync(Guid id)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(x => x.Id == id);
        if (agent is null)
            return false;

        if (agent.Status == "Approved")
            return true;

        agent.Status = "Approved";
        agent.ApprovedUtc = DateTime.UtcNow;
        agent.RevokedUtc = null;

        await _db.SaveChangesAsync();
        return true;
    }
}