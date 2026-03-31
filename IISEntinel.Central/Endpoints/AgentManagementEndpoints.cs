using IISEntinel.Central.Data;
using IISEntinel.Central.Models;
using Microsoft.EntityFrameworkCore;

namespace IISEntinel.Central.Endpoints;

public static class AgentManagementEndpoints
{
    public static void MapAgentManagementEndpoints(this WebApplication app)
    {
        app.MapGet("/api/agents", async (CentralDbContext db) =>
        {
            var now = DateTime.UtcNow;

            var agents = await db.Agents
                .OrderByDescending(a => a.CreatedUtc)
                .Select(a => new
                {
                    a.Id,
                    a.AgentIdentifier,
                    a.DisplayName,
                    a.Hostname,
                    a.Fqdn,
                    a.Status,
                    a.AgentVersion,
                    a.CreatedUtc,
                    a.ApprovedUtc,
                    a.LastSeenUtc,
                    ConnectionStatus = a.LastSeenUtc != null &&
                                       a.LastSeenUtc >= now.AddSeconds(-90)
                        ? "Online"
                        : "Offline"
                })
                .ToListAsync();

            return Results.Ok(agents);
        });

        app.MapPost("/api/agents/{id:guid}/approve", async (Guid id, CentralDbContext db) =>
        {
            var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == id);

            if (agent is null)
            {
                return Results.NotFound(new { message = "Agent not found" });
            }

            if (string.Equals(agent.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Ok(new
                {
                    message = "Agent already approved",
                    agent.Id,
                    agent.Status
                });
            }

            agent.Status = "Approved";
            agent.ApprovedUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                message = "Agent approved",
                agent.Id,
                agent.Status,
                agent.ApprovedUtc
            });
        });

        app.MapPost("/api/agents/heartbeat", async (
            AgentHeartbeatRequest request,
            CentralDbContext db) =>
        {
            var agent = await db.Agents
                .FirstOrDefaultAsync(a => a.AgentIdentifier == request.AgentIdentifier);

            if (agent is null)
            {
                return Results.NotFound(new { message = "Agent not found" });
            }

            if (!string.Equals(agent.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            agent.LastSeenUtc = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(request.AgentVersion))
            {
                agent.AgentVersion = request.AgentVersion;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                message = "Heartbeat accepted",
                agent.Id,
                agent.LastSeenUtc
            });
        });
    }
}