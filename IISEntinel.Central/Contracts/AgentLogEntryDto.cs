namespace IISEntinel.Central.Contracts;

public sealed class AgentLogEntryDto
{
    public DateTime TimestampUtc { get; set; }
    public string Level { get; set; } = "";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string? EventType { get; set; }
    public string? CorrelationId { get; set; }
}