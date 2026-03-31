namespace IISEntinel.Central.Contracts;

public class AgentActionDto
{
    public Guid Id { get; set; }
    public string ActionType { get; set; } = "";
    public string TargetName { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}