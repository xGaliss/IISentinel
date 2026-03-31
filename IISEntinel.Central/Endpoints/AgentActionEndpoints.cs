using IISEntinel.Central.Contracts;
using IISEntinel.Central.Data;
using IISEntinel.Central.Models;
using Microsoft.EntityFrameworkCore;

namespace IISEntinel.Central.Endpoints;

public static class AgentActionEndpoints
{
    public static void MapAgentActionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agents/{id:guid}/actions", async (
            Guid id,
            CreateAgentActionRequest request,
            CentralDbContext db) =>
        {
            var agent = await db.Agents.FirstOrDefaultAsync(x => x.Id == id);
            if (agent is null)
                return Results.NotFound(new { message = "Agent not found" });

            if (!string.Equals(agent.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "Agent is not approved" });

            if (string.IsNullOrWhiteSpace(request.ActionType))
                return Results.BadRequest(new { message = "ActionType is required" });

            if (string.IsNullOrWhiteSpace(request.TargetName))
                return Results.BadRequest(new { message = "TargetName is required" });

            var action = new AgentAction
            {
                Id = Guid.NewGuid(),
                AgentId = agent.Id,
                ActionType = request.ActionType.Trim(),
                TargetName = request.TargetName.Trim(),
                Status = "Pending",
                RequestedBy = "CentralUI",
                CreatedUtc = DateTime.UtcNow
            };

            db.AgentActions.Add(action);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                action.Id,
                action.ActionType,
                action.TargetName,
                action.Status,
                action.CreatedUtc
            });
        });

        app.MapGet("/api/agents/{agentIdentifier}/actions/next", async (
            string agentIdentifier,
            CentralDbContext db) =>
        {
            var agent = await db.Agents.FirstOrDefaultAsync(x => x.AgentIdentifier == agentIdentifier);
            if (agent is null)
                return Results.NotFound(new { message = "Agent not found" });

            if (!string.Equals(agent.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var action = await db.AgentActions
                .Where(x => x.AgentId == agent.Id && x.Status == "Pending")
                .OrderBy(x => x.CreatedUtc)
                .FirstOrDefaultAsync();

            if (action is null)
                return Results.NoContent();

            action.Status = "InProgress";
            action.PickedUpUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new AgentActionDto
            {
                Id = action.Id,
                ActionType = action.ActionType,
                TargetName = action.TargetName,
                CreatedUtc = action.CreatedUtc
            });
        });

        app.MapPost("/api/agents/{agentIdentifier}/actions/{actionId:guid}/complete", async (
            string agentIdentifier,
            Guid actionId,
            AgentActionResultRequest request,
            CentralDbContext db) =>
        {
            var agent = await db.Agents.FirstOrDefaultAsync(x => x.AgentIdentifier == agentIdentifier);
            if (agent is null)
                return Results.NotFound(new { message = "Agent not found" });

            var action = await db.AgentActions.FirstOrDefaultAsync(x => x.Id == actionId && x.AgentId == agent.Id);
            if (action is null)
                return Results.NotFound(new { message = "Action not found" });

            action.Status = "Succeeded";
            action.CompletedUtc = DateTime.UtcNow;
            action.ResultMessage = request.ResultMessage;
            action.Error = null;
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        app.MapPost("/api/agents/{agentIdentifier}/actions/{actionId:guid}/fail", async (
            string agentIdentifier,
            Guid actionId,
            AgentActionResultRequest request,
            CentralDbContext db) =>
        {
            var agent = await db.Agents.FirstOrDefaultAsync(x => x.AgentIdentifier == agentIdentifier);
            if (agent is null)
                return Results.NotFound(new { message = "Agent not found" });

            var action = await db.AgentActions.FirstOrDefaultAsync(x => x.Id == actionId && x.AgentId == agent.Id);
            if (action is null)
                return Results.NotFound(new { message = "Action not found" });

            action.Status = "Failed";
            action.CompletedUtc = DateTime.UtcNow;
            action.ResultMessage = request.ResultMessage;
            action.Error = request.Error;
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        app.MapGet("/api/agents/{id:guid}/actions", async (Guid id, CentralDbContext db) =>
        {
            var exists = await db.Agents.AnyAsync(x => x.Id == id);
            if (!exists)
                return Results.NotFound(new { message = "Agent not found" });

            var actions = await db.AgentActions
                .Where(x => x.AgentId == id)
                .OrderByDescending(x => x.CreatedUtc)
                .Take(100)
                .Select(x => new
                {
                    x.Id,
                    x.ActionType,
                    x.TargetName,
                    x.Status,
                    x.RequestedBy,
                    x.ResultMessage,
                    x.Error,
                    x.CreatedUtc,
                    x.PickedUpUtc,
                    x.CompletedUtc
                })
                .ToListAsync();

            return Results.Ok(actions);
        });
    }
}
