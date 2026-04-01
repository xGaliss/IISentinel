using IISEntinel.Central.Contracts;
using IISEntinel.Central.Data;
using IISEntinel.Central.Models;
using Microsoft.EntityFrameworkCore;

namespace IISEntinel.Central.Endpoints;

public static class AgentLogEndpoints
{
    public static IEndpointRouteBuilder MapAgentLogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/agents");

        group.MapPost("/{agentId:guid}/logs/batch", async (
            Guid agentId,
            AgentLogBatchRequest request,
            CentralDbContext db) =>
        {
            var agentExists = await db.Agents.AnyAsync(x => x.Id == agentId);
            if (!agentExists)
                return Results.NotFound();

            var now = DateTime.UtcNow;

            var items = request.Entries.Select(x => new AgentLogEntry
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                TimestampUtc = x.TimestampUtc,
                Level = x.Level,
                Category = x.Category,
                Message = x.Message,
                EventType = x.EventType,
                CorrelationId = x.CorrelationId,
                ReceivedUtc = now
            });

            db.AgentLogEntries.AddRange(items);
            await db.SaveChangesAsync();

            return Results.Ok();
        });

        group.MapGet("/{agentId:guid}/logs", async (
            Guid agentId,
            int? take,
            string? level,
            string? search,
            CentralDbContext db) =>
        {
            var query = db.AgentLogEntries
                .AsNoTracking()
                .Where(x => x.AgentId == agentId);

            if (!string.IsNullOrWhiteSpace(level))
                query = query.Where(x => x.Level == level);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(x => x.Message.Contains(search) || x.Category.Contains(search));

            var top = Math.Clamp(take ?? 200, 1, 1000);

            var lines = await query
                .OrderByDescending(x => x.TimestampUtc)
                .Take(top)
                .Select(x => new AgentLogLineDto
                {
                    TimestampUtc = x.TimestampUtc,
                    Level = x.Level,
                    Category = x.Category,
                    Message = x.Message
                })
                .ToListAsync();

            var agent = await db.Agents
                .AsNoTracking()
                .Where(x => x.Id == agentId)
                .Select(x => new { x.Id, x.DisplayName })
                .FirstOrDefaultAsync();

            if (agent is null)
                return Results.NotFound();

            lines.Reverse();

            return Results.Ok(new AgentLogsResponseDto
            {
                AgentId = agent.Id,
                AgentName = agent.DisplayName,
                Total = lines.Count,
                Lines = lines
            });
        });

        return app;
    }
}