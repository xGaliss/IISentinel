namespace IISEntinel.Central.Models;

public sealed class AgentLogEntry
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }

    public DateTime TimestampUtc { get; set; }
    public string Level { get; set; } = "";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";

    public string? EventType { get; set; }
    public string? Source { get; set; }
    public string? CorrelationId { get; set; }

    public DateTime ReceivedUtc { get; set; }

    public Agent? Agent { get; set; }
}