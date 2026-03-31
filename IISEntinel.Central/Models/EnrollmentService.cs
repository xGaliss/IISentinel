namespace IISEntinel.Central.Models;

public class EnrollmentToken
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresUtc { get; set; }

    public int MaxUses { get; set; } = 1;
    public int UsedCount { get; set; } = 0;

    public bool IsDisabled { get; set; } = false;
}