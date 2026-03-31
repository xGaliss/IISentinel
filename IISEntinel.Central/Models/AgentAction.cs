namespace IISEntinel.Central.Models;

public class AgentAction
{
    public Guid Id { get; set; }

    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    public string ActionType { get; set; } = "";   // AppPoolRecycle, AppPoolStart, SiteStop...
    public string TargetName { get; set; } = "";   // DefaultAppPool, MiSite...
    public string Status { get; set; } = "Pending"; // Pending, InProgress, Succeeded, Failed

    public string RequestedBy { get; set; } = "CentralUI";
    public string? ResultMessage { get; set; }
    public string? Error { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PickedUpUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}