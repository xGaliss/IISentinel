namespace IISEntinel.Central.Models;

public class Agent
{
    public Guid Id { get; set; }

    public string AgentIdentifier { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Fqdn { get; set; } = "";

    public string Status { get; set; } = "Pending";
    public string AgentVersion { get; set; } = "";

    public string SecretHash { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public DateTime? LastSeenUtc { get; set; }
}