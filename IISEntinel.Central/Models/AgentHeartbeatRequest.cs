namespace IISEntinel.Central.Models;

public class AgentHeartbeatRequest
{
    public string AgentIdentifier { get; set; } = "";
    public string AgentVersion { get; set; } = "";
}