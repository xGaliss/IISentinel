public sealed class AgentLogShippingState
{
    public string FilePath { get; set; } = "";
    public long LastPosition { get; set; }
    public DateTime UpdatedUtc { get; set; }
}