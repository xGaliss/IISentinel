using IISEntinel.Central.Models;
using Microsoft.EntityFrameworkCore;

namespace IISEntinel.Central.Data;

public class CentralDbContext : DbContext
{
    public CentralDbContext(DbContextOptions<CentralDbContext> options)
        : base(options)
    {
    }

    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();
    public DbSet<AgentAction> AgentActions => Set<AgentAction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Agent>()
            .HasIndex(x => x.AgentIdentifier)
            .IsUnique();

        modelBuilder.Entity<EnrollmentToken>()
            .HasIndex(x => x.TokenHash)
            .IsUnique();

        modelBuilder.Entity<AgentAction>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ActionType).HasMaxLength(100);
            entity.Property(x => x.TargetName).HasMaxLength(200);
            entity.Property(x => x.Status).HasMaxLength(50);
            entity.Property(x => x.RequestedBy).HasMaxLength(100);

            entity.HasOne(x => x.Agent)
                .WithMany()
                .HasForeignKey(x => x.AgentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.AgentId, x.Status, x.CreatedUtc });
        });
    }
}