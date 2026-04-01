namespace IISEntinel.Central.Contracts;

public sealed class AgentLogBatchRequest
{
    public List<AgentLogEntryDto> Entries { get; set; } = new();
}