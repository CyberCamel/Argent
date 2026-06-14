using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Argent.Models.Identity;
using Argent.Models.Workflows.Execution;
using Argent.Models.Forms.Components;
using Argent.Models.Workflows;
using Argent.Models.Workflows.Auditing;
using Argent.Models.DomainObjects;
using Argent.Models.Authorization;
using Argent.Models.DataSources;
using Argent.Models.Forms.Components.Configuration;
using Argent.Models.Forms;
using Argent.Infrastructure.Serialization;

namespace Argent.Infrastructure.Data;

public class ArgentDbContext(DbContextOptions<ArgentDbContext> options) : IdentityDbContext<InternalUser, IdentityRole<Guid>, Guid>(options)
{

    public DbSet<Position> Positions { get; set; }

    public DbSet<FormDesign> FormDesigns { get; set; }

    public DbSet<FormCustomData> FormCustomData { get; set; }

    public DbSet<WorkItem> WorkItems { get; set; }

    public DbSet<WorkflowToken> WorkflowTokens { get; set; }

    public DbSet<WorkflowInstance> WorkflowInstances { get; set; }

    public DbSet<WorkflowJournalEntry> WorkflowJournalEntries { get; set; }

    public DbSet<UserTask> UserTasks { get; set; }

    public DbSet<Workflow> WorkflowDefinitions { get; set; }

    public DbSet<WorkflowVersion> WorkflowVersions { get; set; }

    public DbSet<WorkflowDraft> WorkflowDrafts { get; set; }

    public DbSet<DomainObject> DomainObjects { get; set; }

    public DbSet<DomainObjectVersion> DomainObjectVersions { get; set; }

    public DbSet<DomainObjectDraft> DomainObjectDrafts { get; set; }

    public DbSet<DomainObjectRecord> DomainObjectRecords { get; set; }

    public DbSet<DataSourceDocument> DataSources { get; set; }

    public DbSet<PolicyDocument> PolicyDocuments { get; set; }

    public DbSet<Group> Groups { get; set; }

    public DbSet<GroupMembership> GroupMemberships { get; set; }

    public DbSet<GroupGroupMembership> GroupGroupMemberships { get; set; }

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
            entity.HasOne(w => w.CreatedBy).WithMany().HasForeignKey(w => w.CreatedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(w => w.UpdatedBy).WithMany().HasForeignKey(w => w.UpdatedById).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WorkflowVersion>(entity =>
        {
            entity.Property(e => e.Definition)
                .HasConversion(
                    v => JsonSerializer.Serialize(v),
                    v => JsonSerializer.Deserialize<WorkflowDefinition>(v) ?? new WorkflowDefinition())
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.Version)
                .HasConversion(
                    v => v.ToString(),
                    v => Version.Parse(v));

            entity.HasOne(e => e.Workflow)
                .WithMany()
                .HasForeignKey(e => e.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.WorkflowId, e.Version }).IsUnique();

        });

        builder.Entity<WorkflowDraft>(entity =>
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

            entity.HasIndex(e => e.WorkflowId).IsUnique();
        });

        builder.Entity<FormDesign>(entity =>
        {
            entity.ToTable("FormDesigns");
            entity.Property(e => e.Definition)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, FormSerializer.Options),
                    v => JsonSerializer.Deserialize<FormDefinition>(v) ?? new FormDefinition())
                .HasColumnType("nvarchar(max)");
            entity.Property(e => e.ObjectKey).HasMaxLength(128);
            entity.HasIndex(e => e.ObjectKey);
        });

        builder.Entity<FormCustomData>(entity =>
        {
            entity.ToTable("FormCustomData");
            entity.Property(e => e.Values)
                .HasConversion(
                    v => JsonSerializer.Serialize(v),
                    v => JsonSerializer.Deserialize<Dictionary<string, object?>>(v) ?? new())
                .HasColumnType("nvarchar(max)");
            entity.HasIndex(e => e.RecordId);
        });

        // ----- Domain Objects (mirrors the workflow draft/version pattern above) -----

        builder.Entity<DomainObject>(entity =>
        {
            entity.HasOne(d => d.CreatedBy).WithMany().HasForeignKey(d => d.CreatedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(d => d.UpdatedBy).WithMany().HasForeignKey(d => d.UpdatedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(d => d.Key).IsUnique();
        });

        builder.Entity<DomainObjectVersion>(entity =>
        {
            entity.Property(e => e.Definition)
                .HasConversion(
                    v => JsonSerializer.Serialize(v),
                    v => JsonSerializer.Deserialize<DomainObjectDefinition>(v) ?? new DomainObjectDefinition())
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.Version)
                .HasConversion(
                    v => v.ToString(),
                    v => Version.Parse(v));

            entity.HasOne(e => e.DomainObject)
                .WithMany()
                .HasForeignKey(e => e.DomainObjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.DomainObjectId, e.Version }).IsUnique();
        });

        builder.Entity<DomainObjectDraft>(entity =>
        {
            entity.Property(e => e.Definition)
                .HasConversion(
                    v => JsonSerializer.Serialize(v),
                    v => JsonSerializer.Deserialize<DomainObjectDefinition>(v) ?? new DomainObjectDefinition())
                .HasColumnType("nvarchar(max)");

            entity.HasOne(e => e.DomainObject)
                .WithMany()
                .HasForeignKey(e => e.DomainObjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.DomainObjectId).IsUnique();
        });

        builder.Entity<DomainObjectRecord>(entity =>
        {
            entity.Property(e => e.Values)
                .HasConversion(
                    v => JsonSerializer.Serialize(v),
                    v => JsonSerializer.Deserialize<Dictionary<string, object?>>(v) ?? new Dictionary<string, object?>())
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.DefinitionVersion)
                .HasConversion(
                    v => v!.ToString(),
                    v => Version.Parse(v));

            entity.HasOne(e => e.DomainObject)
                .WithMany()
                .HasForeignKey(e => e.DomainObjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.DomainObjectId);
        });

        builder.Entity<DataSourceDocument>(entity =>
        {
            entity.Property(e => e.Config).HasColumnType("nvarchar(max)");
            entity.HasIndex(e => e.Key).IsUnique();
        });

        // ----- Workflow Execution Engine -----

        builder.Entity<WorkflowToken>(entity =>
        {
            entity.ToTable("WorkflowTokens");

            entity.Property(e => e.Payload)
                .HasColumnType("nvarchar(max)");

            entity.HasIndex(e => new { e.InstanceId, e.State })
                .HasDatabaseName("IX_WorkflowTokens_InstanceId_State");

            entity.HasIndex(e => e.State)
                .HasDatabaseName("IX_WorkflowTokens_State");
        });

        builder.Entity<WorkflowJournalEntry>(entity =>
        {
            entity.ToTable("WorkflowJournalEntries");

            entity.Property(e => e.Category)
                .HasMaxLength(64);

            entity.Property(e => e.EventType)
                .HasMaxLength(64);

            entity.Property(e => e.Actor)
                .HasMaxLength(256);

            entity.Property(e => e.Details)
                .HasColumnType("nvarchar(max)");

            entity.HasIndex(e => new { e.InstanceId, e.TimeStamp })
                .HasDatabaseName("IX_WorkflowJournalEntries_InstanceId_Timestamp");

            entity.HasIndex(e => new { e.Category, e.TimeStamp })
                .HasDatabaseName("IX_WorkflowJournalEntries_Category_Timestamp");
        });

        builder.Entity<UserTask>(entity =>
        {
            entity.ToTable("UserTasks");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ResultData)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.FormData)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.CandidateUsers)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.CandidateRoles)
                .HasColumnType("nvarchar(max)");

            entity.HasIndex(e => e.TokenId)
                .HasDatabaseName("IX_UserTasks_TokenId");

            entity.HasIndex(e => new { e.State, e.DueDate })
                .HasDatabaseName("IX_UserTasks_State_DueDate");

            entity.HasIndex(e => new { e.AssignedTo, e.State })
                .HasDatabaseName("IX_UserTasks_AssignedTo_State");

            entity.Property(e => e.RowVersion)
                .IsConcurrencyToken();
        });

        // Additional indexes for WorkItem (base table config is inferred by conventions)
        builder.Entity<WorkItem>(entity =>
        {
            entity.HasIndex(e => e.TokenId)
                .HasDatabaseName("IX_WorkItems_TokenId");

            // Index 1: Immediately processable (No schedule)
            entity.HasIndex(e => new { e.State, e.Priority, e.CreatedAt })
                .HasDatabaseName("IX_WorkItems_Claim_Immediate")
                .HasFilter("[State] = 0 AND [ScheduledAt] IS NULL");

            // Index 2: Highly targeted index on just the scheduled field to handle future tasks
            entity.HasIndex(e => new { e.ScheduledAt, e.State })
                .HasDatabaseName("IX_WorkItems_Scheduled")
                .HasFilter("[State] = 0 AND [ScheduledAt] IS NOT NULL");
        });

        // Recovery query support on WorkflowInstance
        builder.Entity<WorkflowInstance>(entity =>
        {
            entity.HasIndex(e => e.State)
                .HasDatabaseName("IX_WorkflowInstances_State");
        });

        builder.Entity<PolicyDocument>(entity =>
        {
            entity.ToTable("PolicyDocuments");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .HasMaxLength(256);

            entity.Property(e => e.Effect)
                .HasConversion<string>()
                .HasMaxLength(16);

            entity.Property(e => e.ResourceType)
                .HasMaxLength(64);

            entity.Property(e => e.ResourceSelectorJson)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.ActionsJson)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.SubjectJson)
                .HasColumnType("nvarchar(max)");

            entity.Property(e => e.Condition)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<Condition>(v, (JsonSerializerOptions?)null))
                .HasColumnType("nvarchar(max)");

            entity.HasIndex(e => new { e.ResourceType, e.IsEnabled })
                .HasDatabaseName("IX_PolicyDocuments_ResourceType_IsEnabled");
        });

        builder.Entity<Group>(entity =>
        {
            entity.ToTable("Groups");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .HasMaxLength(256);

            entity.HasIndex(e => e.Name)
                .IsUnique()
                .HasDatabaseName("IX_Groups_Name");
        });

        builder.Entity<GroupMembership>(entity =>
        {
            entity.ToTable("GroupMemberships");
            entity.HasKey(e => new { e.GroupId, e.UserId });

            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("IX_GroupMemberships_UserId");
        });

        builder.Entity<GroupGroupMembership>(entity =>
        {
            entity.ToTable("GroupGroupMemberships");
            entity.HasKey(e => new { e.GroupId, e.MemberGroupId });

            // Index for the child→parent walk used to resolve transitive membership.
            entity.HasIndex(e => e.MemberGroupId)
                .HasDatabaseName("IX_GroupGroupMemberships_MemberGroupId");
        });
    }
}