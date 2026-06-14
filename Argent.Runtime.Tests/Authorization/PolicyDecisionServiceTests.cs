using Argent.Contracts.Authorization;
using Argent.Contracts.Forms;
using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Authorization;
using Argent.Models.Forms.Components.Configuration;
using Argent.Runtime.Authorization;
using Argent.Runtime.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.Authorization;

public class PolicyDecisionServiceTests
{
    private static PolicyDecisionService CreateService(Action<ArgentDbContext>? seed = null)
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var ctx = new ArgentDbContext(options);
        ctx.Database.EnsureCreated();
        seed?.Invoke(ctx);

        var factory = new Mock<IDbContextFactory<ArgentDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ArgentDbContext(options));

        return new PolicyDecisionService(
            factory.Object,
            new ConditionEvaluator(),
            new Mock<IAuditService>().Object,
            NullLogger<PolicyDecisionService>.Instance);
    }

    [Fact]
    public async Task No_policies_returns_Deny()
    {
        var svc = CreateService();
        var result = await svc.EvaluateAsync("user1", [], ResourceType.DomainRecord, [], "read");
        Assert.Equal(PolicyDecision.Deny, result);
    }

    [Fact]
    public async Task Allow_policy_matches_action_and_subject()
    {
        var svc = CreateService(ctx =>
        {
            ctx.PolicyDocuments.Add(new PolicyDocument
            {
                Name = "Allow read",
                Effect = PolicyEffect.Allow,
                ResourceType = ResourceType.DomainRecord,
                ActionsJson = """["read"]""",
                SubjectJson = """{"users":["user1"]}""",
                IsEnabled = true
            });
            ctx.SaveChanges();
        });

        var result = await svc.EvaluateAsync("user1", [], ResourceType.DomainRecord, [], "read");
        Assert.Equal(PolicyDecision.Allow, result);
    }

    [Fact]
    public async Task Deny_overrides_Allow_regardless_of_priority()
    {
        var svc = CreateService(ctx =>
        {
            ctx.PolicyDocuments.Add(new PolicyDocument
            {
                Name = "Allow read",
                Effect = PolicyEffect.Allow,
                ResourceType = ResourceType.DomainRecord,
                ActionsJson = """["read"]""",
                SubjectJson = """{"users":["user1"]}""",
                Priority = 100,
                IsEnabled = true
            });
            ctx.PolicyDocuments.Add(new PolicyDocument
            {
                Name = "Deny read",
                Effect = PolicyEffect.Deny,
                ResourceType = ResourceType.DomainRecord,
                ActionsJson = """["read"]""",
                SubjectJson = """{"users":["user1"]}""",
                Priority = 0,
                IsEnabled = true
            });
            ctx.SaveChanges();
        });

        var result = await svc.EvaluateAsync("user1", [], ResourceType.DomainRecord, [], "read");
        Assert.Equal(PolicyDecision.Deny, result);
    }

    [Fact]
    public async Task Priority_ties_break_within_Allow_only()
    {
        var svc = CreateService(ctx =>
        {
            ctx.PolicyDocuments.Add(new PolicyDocument
            {
                Name = "Allow high",
                Effect = PolicyEffect.Allow,
                ResourceType = ResourceType.DomainRecord,
                ActionsJson = """["read"]""",
                SubjectJson = """{"users":["user1"]}""",
                Priority = 100,
                IsEnabled = true
            });
            ctx.PolicyDocuments.Add(new PolicyDocument
            {
                Name = "Allow low",
                Effect = PolicyEffect.Allow,
                ResourceType = ResourceType.DomainRecord,
                ActionsJson = """["read"]""",
                SubjectJson = """{"users":["user1"]}""",
                Priority = 10,
                IsEnabled = true
            });
            ctx.SaveChanges();
        });

        var result = await svc.EvaluateAsync("user1", [], ResourceType.DomainRecord, [], "read");
        Assert.Equal(PolicyDecision.Allow, result);
    }

    [Fact]
    public async Task Role_based_policy_matches()
    {
        var svc = CreateService(ctx =>
        {
            ctx.PolicyDocuments.Add(new PolicyDocument
            {
                Name = "Admin access",
                Effect = PolicyEffect.Allow,
                ResourceType = ResourceType.AdminArea,
                ActionsJson = """["*"]""",
                SubjectJson = """{"roles":["SuperAdmin"]}""",
                IsEnabled = true
            });
            ctx.SaveChanges();
        });

        var result = await svc.EvaluateAsync("user1", ["SuperAdmin"], ResourceType.AdminArea, [], "delete");
        Assert.Equal(PolicyDecision.Allow, result);
    }

    [Fact]
    public async Task Role_mismatch_returns_Deny()
    {
        var svc = CreateService(ctx =>
        {
            ctx.PolicyDocuments.Add(new PolicyDocument
            {
                Name = "Admin access",
                Effect = PolicyEffect.Allow,
                ResourceType = ResourceType.AdminArea,
                ActionsJson = """["*"]""",
                SubjectJson = """{"roles":["SuperAdmin"]}""",
                IsEnabled = true
            });
            ctx.SaveChanges();
        });

        var result = await svc.EvaluateAsync("user1", ["User"], ResourceType.AdminArea, [], "delete");
        Assert.Equal(PolicyDecision.Deny, result);
    }

    [Fact]
    public async Task Condition_evaluated_using_attribute_bag()
    {
        var svc = CreateService(ctx =>
        {
            ctx.PolicyDocuments.Add(new PolicyDocument
            {
                Name = "Self-service edit",
                Effect = PolicyEffect.Allow,
                ResourceType = ResourceType.DomainRecord,
                ActionsJson = """["edit"]""",
                SubjectJson = "{}",
                Condition = new CompareCondition
                {
                    Field = "resource.ownerId",
                    Operator = "==",
                    ValueField = "subject.userId"
                },
                IsEnabled = true
            });
            ctx.SaveChanges();
        });

        var result = await svc.EvaluateAsync(
            "user1", [], ResourceType.DomainRecord,
            new Dictionary<string, object?> { ["ownerId"] = "user1" },
            "edit");

        Assert.Equal(PolicyDecision.Allow, result);
    }

    [Fact]
    public async Task Condition_false_returns_Deny()
    {
        var svc = CreateService(ctx =>
        {
            ctx.PolicyDocuments.Add(new PolicyDocument
            {
                Name = "Self-service edit",
                Effect = PolicyEffect.Allow,
                ResourceType = ResourceType.DomainRecord,
                ActionsJson = """["edit"]""",
                SubjectJson = "{}",
                Condition = new CompareCondition
                {
                    Field = "resource.ownerId",
                    Operator = "==",
                    ValueField = "subject.userId"
                },
                IsEnabled = true
            });
            ctx.SaveChanges();
        });

        var result = await svc.EvaluateAsync(
            "user2", [], ResourceType.DomainRecord,
            new Dictionary<string, object?> { ["ownerId"] = "user1" },
            "edit");

        Assert.Equal(PolicyDecision.Deny, result);
    }

    [Fact]
    public async Task Disabled_policy_is_skipped()
    {
        var svc = CreateService(ctx =>
        {
            ctx.PolicyDocuments.Add(new PolicyDocument
            {
                Name = "Allow read",
                Effect = PolicyEffect.Allow,
                ResourceType = ResourceType.DomainRecord,
                ActionsJson = """["read"]""",
                SubjectJson = """{"users":["user1"]}""",
                IsEnabled = false
            });
            ctx.SaveChanges();
        });

        var result = await svc.EvaluateAsync("user1", [], ResourceType.DomainRecord, [], "read");
        Assert.Equal(PolicyDecision.Deny, result);
    }
}
