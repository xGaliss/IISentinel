namespace IISEntinel.Agent;

public class AgentEnrollRequest
{
    public string EnrollmentToken { get; set; } = "";
    public string AgentIdentifier { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Fqdn { get; set; } = "";
    public string AgentVersion { get; set; } = "";
}