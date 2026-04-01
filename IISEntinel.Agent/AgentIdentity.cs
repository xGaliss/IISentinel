namespace IISEntinel.Agent;

public class AgentIdentity
{
    public string AgentIdentifier { get; set; } = "";
    public Guid? AgentId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}