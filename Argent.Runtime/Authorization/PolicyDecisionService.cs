using System.Collections.Concurrent;
using System.Text.Json;
using Argent.Contracts.Authorization;
using Argent.Contracts.Forms;
using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Argent.Runtime.Authorization;

public class PolicyDecisionService : IPolicyDecisionService
{
    private readonly IDbContextFactory<ArgentDbContext> _contextFactory;
    private readonly IConditionEvaluator _conditionEvaluator;
    private readonly IAuditService _auditService;
    private readonly ILogger<PolicyDecisionService> _logger;
    private readonly ConcurrentDictionary<string, List<PolicyDocument>> _cache = new();

    public PolicyDecisionService(
        IDbContextFactory<ArgentDbContext> contextFactory,
        IConditionEvaluator conditionEvaluator,
        IAuditService auditService,
        ILogger<PolicyDecisionService> logger)
    {
        _contextFactory = contextFactory;
        _conditionEvaluator = conditionEvaluator;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<PolicyDecision> EvaluateAsync(
        string userId,
        List<string> roles,
        ResourceType resourceType,
        Dictionary<string, object?> resourceAttributes,
        string action,
        Dictionary<string, object?>? environment = null,
        CancellationToken ct = default)
    {
        var policies = await GetApplicablePoliciesAsync(resourceType, action, ct);

        var subjectAttrs = new Dictionary<string, object?>
        {
            ["userId"] = userId,
            ["roles"] = roles
        };

        var ctx = new AuthorizationContext(subjectAttrs, resourceAttributes, environment);

        PolicyDecision? decision = null;

        foreach (var policy in policies.OrderByDescending(p => p.Priority))
        {
            if (!MatchesSubject(policy, userId, roles))
                continue;

            if (policy.Condition != null)
            {
                try
                {
                    if (!_conditionEvaluator.Evaluate(policy.Condition, ctx))
                        continue;
                }
                catch
                {
                    continue;
                }
            }

            if (policy.Effect == PolicyEffect.Deny)
            {
                await _auditService.RecordAsync(
                    "Authorization", "Deny",
                    actor: userId,
                    details: new { policyId = policy.Id, policyName = policy.Name, resourceType = resourceType.ToString(), action },
                    ct: ct);

                return PolicyDecision.Deny;
            }

            decision = PolicyDecision.Allow;
        }

        if (decision == PolicyDecision.Allow)
        {
            return PolicyDecision.Allow;
        }

        if (decision == null)
        {
            await _auditService.RecordAsync(
                "Authorization", "Deny (default)",
                actor: userId,
                details: new { resourceType = resourceType.ToString(), action, reason = "No matching policy" },
                ct: ct);
        }

        return PolicyDecision.Deny;
    }

    public async Task<List<PolicyDocument>> GetApplicablePoliciesAsync(
        ResourceType resourceType,
        string action,
        CancellationToken ct = default)
    {
        var cacheKey = $"{resourceType}:{action}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var policies = await context.PolicyDocuments
            .Where(p => p.ResourceType == resourceType && p.IsEnabled)
            .OrderByDescending(p => p.Priority)
            .ToListAsync(ct);

        var applicable = policies
            .Where(p => ActionsMatch(p.ActionsJson, action))
            .ToList();

        _cache[cacheKey] = applicable;
        return applicable;
    }

    public Task InvalidateCacheAsync(CancellationToken ct = default)
    {
        _cache.Clear();
        return Task.CompletedTask;
    }

    private static bool MatchesSubject(PolicyDocument policy, string userId, List<string> roles)
    {
        if (string.IsNullOrWhiteSpace(policy.SubjectJson) || policy.SubjectJson == "{}")
            return true;

        try
        {
            var subject = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(policy.SubjectJson);
            if (subject == null) return true;

            if (subject.TryGetValue("users", out var usersEl) && usersEl.ValueKind == JsonValueKind.Array)
            {
                var users = JsonSerializer.Deserialize<List<string>>(usersEl.GetRawText()) ?? [];
                if (users.Count > 0 && !users.Contains(userId))
                    return false;
            }

            if (subject.TryGetValue("roles", out var rolesEl) && rolesEl.ValueKind == JsonValueKind.Array)
            {
                var policyRoles = JsonSerializer.Deserialize<List<string>>(rolesEl.GetRawText()) ?? [];
                if (policyRoles.Count > 0 && !policyRoles.Any(r => roles.Contains(r)))
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ActionsMatch(string actionsJson, string action)
    {
        if (string.IsNullOrWhiteSpace(actionsJson) || actionsJson == "[]" || actionsJson == "{}")
            return true;

        try
        {
            var actions = JsonSerializer.Deserialize<List<string>>(actionsJson);
            return actions == null || actions.Count == 0 || actions.Contains("*") || actions.Contains(action);
        }
        catch
        {
            return false;
        }
    }
}
