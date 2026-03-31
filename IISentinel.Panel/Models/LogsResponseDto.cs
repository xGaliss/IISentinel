namespace IISentinel.Panel.Models;

public sealed class LogsResponseDto
{
    public string? LogsDir { get; set; }
    public string? File { get; set; }
    public List<string> Files { get; set; } = new();
    public List<string> Lines { get; set; } = new();
    public string? Error { get; set; }
}