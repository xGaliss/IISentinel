using IISEntinel.Central.Models;
using IISEntinel.Central.Data;
using Microsoft.EntityFrameworkCore;

namespace IISEntinel.Central.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agents/enroll", async (
            AgentEnrollRequest request,
            CentralDbContext db) =>
        {
            // validar token
            var tokenHash = request.EnrollmentToken; // luego lo haseamos

            var token = await db.EnrollmentTokens
                .FirstOrDefaultAsync(t =>
                    t.TokenHash == tokenHash &&
                    !t.IsDisabled &&
                    (t.ExpiresUtc == null || t.ExpiresUtc > DateTime.UtcNow));

            if (token == null)
            {
                return Results.BadRequest("Invalid enrollment token");
            }

            if (token.MaxUses > 0 && token.UsedCount >= token.MaxUses)
            {
                return Results.BadRequest("Token max uses reached");
            }

            // comprobar si ya existe
            var existing = await db.Agents
                .FirstOrDefaultAsync(a => a.AgentIdentifier == request.AgentIdentifier);

            if (existing != null)
            {
                return Results.Ok(new
                {
                    agentId = existing.Id,
                    status = existing.Status
                });
            }

            // crear agente
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                AgentIdentifier = request.AgentIdentifier,
                DisplayName = request.Hostname,
                Hostname = request.Hostname,
                Fqdn = request.Fqdn,
                Status = "Pending",
                AgentVersion = request.AgentVersion,
                CreatedUtc = DateTime.UtcNow
            };

            db.Agents.Add(agent);

            token.UsedCount++;

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                agentId = agent.Id,
                status = agent.Status
            });
        });
    }
}