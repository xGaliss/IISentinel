namespace IISEntinel.Central.Models;

public class AgentSite
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    public string Name { get; set; } = "";
    public string State { get; set; } = "Unknown";
    public string BindingsJson { get; set; } = "[]";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
