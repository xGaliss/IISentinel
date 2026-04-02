public sealed class AgentLogShippingOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 100;
    public string StateFilePath { get; set; } = "Logs/logshipping-state.json";
}