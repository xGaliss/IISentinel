namespace IISEntinel.Agent;
using System.Text.Json.Serialization;

public class AgentEnrollResponse
{
    [JsonPropertyName("agentId")]
    public Guid AgentId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}