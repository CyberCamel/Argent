using System.Text.Json;
using Argent.Contracts.Authorization;
using Argent.Infrastructure.Data;
using Argent.Models.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.Authorization;

// Manages global (non-resource-scoped) PolicyDocuments — the "base capabilities" layer:
// e.g. "FlowDesigners can run all workflows" with no ResourceSelectorJson.
public class CapabilityService(
    IDbContextFactory<ArgentDbContext> _contextFactory,
    IPolicyDecisionService _policyService) : ICapabilityService
{
    public async Task<List<PolicyDocument>> GetCapabilitiesAsync(string resourceType, CancellationToken ct = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(ct);

        var all = await ctx.PolicyDocuments
            .Where(p => p.ResourceType == resourceType && p.IsEnabled)
            .ToListAsync(ct);

        return all.Where(IsGlobal).ToList();
    }

    public async Task GrantAsync(string resourceType, string subjectJson, string action, CancellationToken ct = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(ct);

        // Reuse an existing global policy for this exact subject if one exists.
        var all = await ctx.PolicyDocuments
            .Where(p => p.ResourceType == resourceType && p.IsEnabled)
            .ToListAsync(ct);

        var existing = all
            .Where(IsGlobal)
            .FirstOrDefault(p => p.SubjectJson == subjectJson);

        if (existing != null)
        {
            var actions = ParseList(existing.ActionsJson);
            if (!actions.Contains(action, StringComparer.OrdinalIgnoreCase))
            {
                actions.Add(action);
                existing.ActionsJson = JsonSerializer.Serialize(actions);
                existing.UpdatedAt = DateTime.UtcNow;
            }
        }
        else
        {
            ctx.PolicyDocuments.Add(new PolicyDocument
            {
                ResourceType = resourceType,
                ResourceSelectorJson = "{}",
                ActionsJson = JsonSerializer.Serialize(new[] { action }),
                SubjectJson = subjectJson,
                Effect = PolicyEffect.Allow,
                IsEnabled = true
            });
        }

        await ctx.SaveChangesAsync(ct);
        await _policyService.InvalidateCacheAsync(ct);
    }

    public async Task RevokeActionAsync(Guid policyId, string action, CancellationToken ct = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(ct);
        var policy = await ctx.PolicyDocuments.FindAsync([policyId], ct);
        if (policy == null) return;

        var actions = ParseList(policy.ActionsJson)
            .Where(a => !a.Equals(action, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (actions.Count == 0)
            ctx.PolicyDocuments.Remove(policy);
        else
        {
            policy.ActionsJson = JsonSerializer.Serialize(actions);
            policy.UpdatedAt = DateTime.UtcNow;
        }

        await ctx.SaveChangesAsync(ct);
        await _policyService.InvalidateCacheAsync(ct);
    }

    public Task InvalidateCacheAsync(CancellationToken ct = default) =>
        _policyService.InvalidateCacheAsync(ct);

    private static bool IsGlobal(PolicyDocument p) =>
        string.IsNullOrWhiteSpace(p.ResourceSelectorJson) || p.ResourceSelectorJson == "{}";

    private static List<string> ParseList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }
}
