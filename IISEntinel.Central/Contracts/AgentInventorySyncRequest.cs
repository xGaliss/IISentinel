namespace IISEntinel.Central.Contracts;

public class AgentInventorySyncRequest
{
    public List<AppPoolSnapshotDto> AppPools { get; set; } = new();
    public List<SiteSnapshotDto> Sites { get; set; } = new();
}

public class AppPoolSnapshotDto
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "Unknown";
}

public class SiteSnapshotDto
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "Unknown";
    public List<string> Bindings { get; set; } = new();
}
