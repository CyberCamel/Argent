using System.Text.Json;
using Argent.Contracts.Authorization;
using Argent.Infrastructure.Data;
using Argent.Models.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Authorization;

public class ResourceOwnershipService(
    IDbContextFactory<ArgentDbContext> _contextFactory,
    IPolicyDecisionService _policyDecisionService) : IResourceOwnershipService
{
    public async Task GrantOwnershipAsync(string resourceType, Guid resourceId, string userId, CancellationToken ct = default)
    {
        var actions = resourceType switch
        {
            "Workflow" => ResourceActions.Workflow.Owner,
            "Form" => ResourceActions.Form.Owner,
            _ => (string[])[]
        };

        if (actions.Length == 0)
            return;

        await using var ctx = await _contextFactory.CreateDbContextAsync(ct);

        var policy = new PolicyDocument
        {
            Name = $"{resourceType} owner: {userId[..Math.Min(8, userId.Length)]}",
            ResourceType = resourceType,
            ResourceSelectorJson = JsonSerializer.Serialize(new { id = resourceId }),
            ActionsJson = JsonSerializer.Serialize(actions),
            SubjectJson = JsonSerializer.Serialize(new { users = new[] { userId } }),
            Effect = PolicyEffect.Allow,
            IsEnabled = true,
            CreatedBy = userId
        };

        ctx.PolicyDocuments.Add(policy);
        await ctx.SaveChangesAsync(ct);
        await _policyDecisionService.InvalidateCacheAsync(ct);
    }

    public async Task ShareAccessAsync(string resourceType, Guid resourceId, string subjectJson, List<string> actions, CancellationToken ct = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(ct);

        var policy = new PolicyDocument
        {
            Name = $"{resourceType} shared access",
            ResourceType = resourceType,
            ResourceSelectorJson = JsonSerializer.Serialize(new { id = resourceId }),
            ActionsJson = JsonSerializer.Serialize(actions),
            SubjectJson = subjectJson,
            Effect = PolicyEffect.Allow,
            IsEnabled = true
        };

        ctx.PolicyDocuments.Add(policy);
        await ctx.SaveChangesAsync(ct);
        await _policyDecisionService.InvalidateCacheAsync(ct);
    }

    public async Task UpdateActionsAsync(Guid policyId, List<string> newActions, CancellationToken ct = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(ct);
        var policy = await ctx.PolicyDocuments.FindAsync([policyId], ct);
        if (policy == null) return;

        if (newActions.Count == 0)
            ctx.PolicyDocuments.Remove(policy);
        else
        {
            policy.ActionsJson = JsonSerializer.Serialize(newActions);
            policy.UpdatedAt = DateTime.UtcNow;
        }

        await ctx.SaveChangesAsync(ct);
        await _policyDecisionService.InvalidateCacheAsync(ct);
    }

    public async Task RevokeAccessAsync(Guid policyId, CancellationToken ct = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(ct);

        var policy = await ctx.PolicyDocuments.FindAsync([policyId], ct);
        if (policy != null)
        {
            ctx.PolicyDocuments.Remove(policy);
            await ctx.SaveChangesAsync(ct);
            await _policyDecisionService.InvalidateCacheAsync(ct);
        }
    }

    public async Task<List<PolicyDocument>> GetResourcePoliciesAsync(string resourceType, Guid resourceId, CancellationToken ct = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(ct);

        var idStr = resourceId.ToString();
        var allPolicies = await ctx.PolicyDocuments
            .Where(p => p.ResourceType == resourceType && p.IsEnabled)
            .ToListAsync(ct);

        return allPolicies
            .Where(p => IsInstanceScoped(p.ResourceSelectorJson, idStr))
            .ToList();
    }

    private static bool IsInstanceScoped(string selectorJson, string resourceId)
    {
        if (string.IsNullOrWhiteSpace(selectorJson) || selectorJson == "{}")
            return false;

        try
        {
            var selector = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(selectorJson);
            if (selector == null) return false;
            return selector.TryGetValue("id", out var idEl) &&
                   string.Equals(idEl.GetString(), resourceId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
