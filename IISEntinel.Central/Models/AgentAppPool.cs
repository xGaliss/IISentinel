namespace IISEntinel.Central.Models;

public class AgentAppPool
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    public string Name { get; set; } = "";
    public string State { get; set; } = "Unknown";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
