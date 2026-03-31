using IISEntinel.Central.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace IISEntinel.Central.Data;

public class CentralDbContext : DbContext
{
    public CentralDbContext(DbContextOptions<CentralDbContext> options)
        : base(options)
    {
    }

    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<EnrollmentToken> EnrollmentTokens => Set<EnrollmentToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Agent>()
            .HasIndex(x => x.AgentIdentifier)
            .IsUnique();

        modelBuilder.Entity<EnrollmentToken>()
            .HasIndex(x => x.TokenHash)
            .IsUnique();
    }
}