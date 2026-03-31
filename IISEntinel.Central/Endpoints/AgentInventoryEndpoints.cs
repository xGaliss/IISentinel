using System.Text.Json;
using IISEntinel.Central.Contracts;
using IISEntinel.Central.Data;
using IISEntinel.Central.Models;
using Microsoft.EntityFrameworkCore;

namespace IISEntinel.Central.Endpoints;

public static class AgentInventoryEndpoints
{
    public static void MapAgentInventoryEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agents/{agentIdentifier}/inventory", async (
            string agentIdentifier,
            AgentInventorySyncRequest request,
            CentralDbContext db) =>
        {
            var agent = await db.Agents.FirstOrDefaultAsync(x => x.AgentIdentifier == agentIdentifier);
            if (agent is null)
                return Results.NotFound(new { message = "Agent not found" });

            if (!string.Equals(agent.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var now = DateTime.UtcNow;

            var existingPools = await db.AgentAppPools.Where(x => x.AgentId == agent.Id).ToListAsync();
            var incomingPoolNames = request.AppPools.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var pool in existingPools.Where(x => !incomingPoolNames.Contains(x.Name)).ToList())
            {
                db.AgentAppPools.Remove(pool);
            }

            foreach (var dto in request.AppPools)
            {
                var existing = existingPools.FirstOrDefault(x => string.Equals(x.Name, dto.Name, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    db.AgentAppPools.Add(new AgentAppPool
                    {
                        Id = Guid.NewGuid(),
                        AgentId = agent.Id,
                        Name = dto.Name,
                        State = dto.State,
                        UpdatedUtc = now
                    });
                }
                else
                {
                    existing.State = dto.State;
                    existing.UpdatedUtc = now;
                }
            }

            var existingSites = await db.AgentSites.Where(x => x.AgentId == agent.Id).ToListAsync();
            var incomingSiteNames = request.Sites.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var site in existingSites.Where(x => !incomingSiteNames.Contains(x.Name)).ToList())
            {
                db.AgentSites.Remove(site);
            }

            foreach (var dto in request.Sites)
            {
                var existing = existingSites.FirstOrDefault(x => string.Equals(x.Name, dto.Name, StringComparison.OrdinalIgnoreCase));
                var bindingsJson = JsonSerializer.Serialize(dto.Bindings);

                if (existing is null)
                {
                    db.AgentSites.Add(new AgentSite
                    {
                        Id = Guid.NewGuid(),
                        AgentId = agent.Id,
                        Name = dto.Name,
                        State = dto.State,
                        BindingsJson = bindingsJson,
                        UpdatedUtc = now
                    });
                }
                else
                {
                    existing.State = dto.State;
                    existing.BindingsJson = bindingsJson;
                    existing.UpdatedUtc = now;
                }
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { message = "Inventory synced" });
        });

        app.MapGet("/api/agents/{id:guid}/inventory", async (Guid id, CentralDbContext db) =>
        {
            var agent = await db.Agents.FirstOrDefaultAsync(x => x.Id == id);
            if (agent is null)
                return Results.NotFound(new { message = "Agent not found" });

            var pools = await db.AgentAppPools
                .Where(x => x.AgentId == id)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Name, x.State, x.UpdatedUtc })
                .ToListAsync();

            var sites = await db.AgentSites
                .Where(x => x.AgentId == id)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Name, x.State, x.BindingsJson, x.UpdatedUtc })
                .ToListAsync();

            return Results.Ok(new
            {
                Agent = new
                {
                    agent.Id,
                    agent.DisplayName,
                    agent.Hostname,
                    agent.Fqdn,
                    agent.AgentIdentifier,
                    agent.Status,
                    agent.LastSeenUtc,
                    agent.AgentVersion
                },
                AppPools = pools,
                Sites = sites
            });
        });
    }
}
