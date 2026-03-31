namespace IISEntinel.Agent;

public class AgentIdentity
{
    public string AgentIdentifier { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}