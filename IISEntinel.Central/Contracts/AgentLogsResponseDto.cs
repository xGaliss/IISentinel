namespace IISEntinel.Central.Contracts;

public sealed class AgentLogsResponseDto
{
    public Guid AgentId { get; set; }
    public string AgentName { get; set; } = "";
    public int Total { get; set; }
    public List<AgentLogLineDto> Lines { get; set; } = new();
}