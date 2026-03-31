namespace IISEntinel.Central.Contracts;

public class CreateAgentActionRequest
{
    public string ActionType { get; set; } = "";
    public string TargetName { get; set; } = "";
}
