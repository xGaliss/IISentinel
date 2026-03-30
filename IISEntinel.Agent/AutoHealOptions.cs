namespace IISEntinel.Agent;

public sealed class AutoHealOptions
{
    public bool Enabled { get; set; }
    public int CheckIntervalSeconds { get; set; } = 10;
    public List<string> Pools { get; set; } = new();
    public int MaxAttempts { get; set; } = 3;
    public int CooldownMinutes { get; set; } = 10;
}