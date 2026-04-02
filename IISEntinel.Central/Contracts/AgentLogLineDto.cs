namespace IISEntinel.Central.Contracts;

public sealed class AgentLogLineDto
{
    public DateTime TimestampUtc { get; set; }
    public string Level { get; set; } = "";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
}