namespace IISentinel.Panel.Models;

public sealed class SiteDto
{
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public List<string> Bindings { get; set; } = new();
}