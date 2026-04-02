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
    public DbSet<AgentAppPool> AgentAppPools => Set<AgentAppPool>();
    public DbSet<AgentSite> AgentSites => Set<AgentSite>();

    public DbSet<AgentLogEntry> AgentLogEntries => Set<AgentLogEntry>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Agent>(entity =>
        {
            entity.HasIndex(x => x.AgentIdentifier).IsUnique();
        });

        modelBuilder.Entity<EnrollmentToken>(entity =>
        {
            entity.HasIndex(x => x.TokenHash).IsUnique();
        });

        modelBuilder.Entity<AgentAction>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ActionType).HasMaxLength(100);
            entity.Property(x => x.TargetName).HasMaxLength(200);
            entity.Property(x => x.Status).HasMaxLength(50);
            entity.Property(x => x.RequestedBy).HasMaxLength(100);
            entity.HasIndex(x => new { x.AgentId, x.Status, x.CreatedUtc });
            entity.HasOne(x => x.Agent)
                .WithMany()
                .HasForeignKey(x => x.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentAppPool>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.State).HasMaxLength(50);
            entity.HasIndex(x => new { x.AgentId, x.Name }).IsUnique();
            entity.HasOne(x => x.Agent)
                .WithMany()
                .HasForeignKey(x => x.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentSite>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.State).HasMaxLength(50);
            entity.HasIndex(x => new { x.AgentId, x.Name }).IsUnique();
            entity.HasOne(x => x.Agent)
                .WithMany()
                .HasForeignKey(x => x.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentLogEntry>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Level).HasMaxLength(32);
            entity.Property(x => x.Category).HasMaxLength(128);
            entity.Property(x => x.EventType).HasMaxLength(64);
            entity.Property(x => x.CorrelationId).HasMaxLength(128);

            entity.HasIndex(x => new { x.AgentId, x.TimestampUtc });
            entity.HasIndex(x => new { x.AgentId, x.Level });
        });

    }
}
