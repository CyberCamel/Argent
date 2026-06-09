using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Argent.Models.Identity;
using Argent.Models.Workflows.Execution;
using Argent.Models.Forms.Components;
using Argent.Models.Workflows;

namespace Argent.Infrastructure.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<InternalUser, IdentityRole<Guid>, Guid>(options)
{

    public DbSet<Position> Positions { get; set; }

    public DbSet<FormDocument> FormDocuments { get; set; }

    public DbSet<WorkItem> WorkItems { get; set; }

    public DbSet<WorkflowInstance> WorkflowInstances { get; set; }

    public DbSet<Workflow> WorkflowDefinitions { get; set; }

    public DbSet<WorkflowVersion> WorkflowVersions { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // 1. Setup Identity tables (MUST BE FIRST)
        base.OnModelCreating(builder);

        // 2. Configure InternalUser & the Dictionary-to-JSON conversion
        builder.Entity<InternalUser>(entity =>
        {
            entity.Property(e => e.ExtraAttributes)
                .HasConversion(
                    v => JsonSerializer.Serialize(v),
                    v => JsonSerializer.Deserialize<Dictionary<string, string>>(v)
                         ?? new Dictionary<string, string>())
                .HasColumnType("nvarchar(max)");
        });

        // 3. Configure any other relationships (like Positions)
        builder.Entity<Position>()
            .HasOne(p => p.Person)
            .WithMany(u => u.Positions)
            .HasForeignKey(p => p.PersonId);

        builder.Entity<Workflow>(entity =>
        {
            entity.Property(e => e.Definition)
                .HasConversion(
                    v => JsonSerializer.Serialize(v),
                    v => JsonSerializer.Deserialize<WorkflowDefinition>(v) ?? new WorkflowDefinition())
                .HasColumnType("nvarchar(max)");
        });

        builder.Entity<WorkflowVersion>(entity =>
        {
            entity.Property(e => e.Definition)
                .HasConversion(
                    v => JsonSerializer.Serialize(v),
                    v => JsonSerializer.Deserialize<WorkflowDefinition>(v) ?? new WorkflowDefinition())
                .HasColumnType("nvarchar(max)");

            entity.HasOne(e => e.Workflow)
                .WithMany()
                .HasForeignKey(e => e.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.WorkflowId, e.VersionNumber }).IsUnique();
        });

        builder.Entity<FormDocument>(entity =>
        {
            entity.Property(e => e.Definition)
                .HasConversion(
                    v => JsonSerializer.Serialize(v),
                    v => JsonSerializer.Deserialize<FormDefinition>(v) ?? new FormDefinition())
                .HasColumnType("nvarchar(max)");
        });
    }
}