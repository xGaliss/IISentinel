namespace IISEntinel.Agent;

public class AgentCommandDto
{
    public Guid Id { get; set; }
    public string ActionType { get; set; } = "";
    public string TargetName { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}