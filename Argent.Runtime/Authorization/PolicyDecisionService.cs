using System.Collections.Concurrent;
using System.Text.Json;
using Argent.Contracts.Authorization;
using Argent.Contracts.Forms;
using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Authorization;
using Argent.Models.Identity;
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
    private readonly ConcurrentDictionary<string, List<string>> _groupCache = new();

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
        string resourceType,
        Dictionary<string, object?> resourceAttributes,
        string action,
        Dictionary<string, object?>? environment = null,
        CancellationToken ct = default)
    {
        var policies = await GetApplicablePoliciesAsync(resourceType, action, ct);
        var scopedPolicies = FilterByResourceSelector(policies, resourceAttributes);

        var groups = await GetUserGroupsAsync(userId, ct);

        var subjectAttrs = new Dictionary<string, object?>
        {
            ["userId"] = userId,
            ["roles"] = roles,
            ["groups"] = groups
        };

        var ctx = new AuthorizationContext(subjectAttrs, resourceAttributes, environment);

        PolicyDecision? decision = null;

        foreach (var policy in scopedPolicies.OrderByDescending(p => p.Priority))
        {
            if (!MatchesSubject(policy, userId, roles, groups))
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
                    details: new { policyId = policy.Id, policyName = policy.Name, resourceType = resourceType, action },
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
                details: new { resourceType = resourceType, action, reason = "No matching policy" },
                ct: ct);
        }

        return PolicyDecision.Deny;
    }

    public async Task<List<PolicyDocument>> GetApplicablePoliciesAsync(
        string resourceType,
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
        _groupCache.Clear();
        return Task.CompletedTask;
    }

    private static bool MatchesSubject(PolicyDocument policy, string userId, List<string> roles, List<string> groups)
    {
        if (string.IsNullOrWhiteSpace(policy.SubjectJson) || policy.SubjectJson == "{}")
            return true;

        try
        {
            var subject = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(policy.SubjectJson);
            if (subject == null) return true;

            // ANY/OR semantics: a subject with no constraints applies to everyone; otherwise the
            // user must satisfy at least one specified dimension (users OR roles OR groups).
            var anyConstraint = false;
            var anyMatch = false;

            if (subject.TryGetValue("users", out var usersEl) && usersEl.ValueKind == JsonValueKind.Array)
            {
                var users = JsonSerializer.Deserialize<List<string>>(usersEl.GetRawText()) ?? [];
                if (users.Count > 0)
                {
                    anyConstraint = true;
                    if (users.Contains(userId, StringComparer.OrdinalIgnoreCase)) anyMatch = true;
                }
            }

            if (subject.TryGetValue("roles", out var rolesEl) && rolesEl.ValueKind == JsonValueKind.Array)
            {
                var policyRoles = JsonSerializer.Deserialize<List<string>>(rolesEl.GetRawText()) ?? [];
                if (policyRoles.Count > 0)
                {
                    anyConstraint = true;
                    if (policyRoles.Any(roles.Contains)) anyMatch = true;
                }
            }

            if (subject.TryGetValue("groups", out var groupsEl) && groupsEl.ValueKind == JsonValueKind.Array)
            {
                var policyGroups = JsonSerializer.Deserialize<List<string>>(groupsEl.GetRawText()) ?? [];
                if (policyGroups.Count > 0)
                {
                    anyConstraint = true;
                    if (policyGroups.Any(groups.Contains)) anyMatch = true;
                }
            }

            return !anyConstraint || anyMatch;
        }
        catch
        {
            return false;
        }
    }

    // Resolves a user's group ids, cached process-wide (the service is a singleton). Invalidated
    // alongside the policy cache when groups/memberships or policies change.
    private async Task<List<string>> GetUserGroupsAsync(string userId, CancellationToken ct)
    {
        if (_groupCache.TryGetValue(userId, out var cached))
            return cached;

        if (!Guid.TryParse(userId, out var uid))
            return [];

        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        // Groups the user is a direct member of.
        var direct = await context.GroupMemberships
            .Where(m => m.UserId == uid)
            .Select(m => m.GroupId)
            .ToListAsync(ct);

        // Nested-group edges (child → its parent containers). The graph is small, so load it
        // once and walk in memory.
        var edges = await context.GroupGroupMemberships
            .Select(e => new { e.GroupId, e.MemberGroupId })
            .ToListAsync(ct);
        var parentsByChild = edges
            .GroupBy(e => e.MemberGroupId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.GroupId).ToList());

        // Transitive closure of "is a member of", cycle-safe via the visited set.
        var effective = new HashSet<Guid>();
        var queue = new Queue<Guid>(direct);
        while (queue.Count > 0)
        {
            var g = queue.Dequeue();
            if (!effective.Add(g))
                continue;
            if (parentsByChild.TryGetValue(g, out var parents))
                foreach (var p in parents)
                    queue.Enqueue(p);
        }

        // All Users is a virtual group — every authenticated user is a member.
        effective.Add(SystemGroups.AllUsersId);

        var groups = effective.Select(g => g.ToString()).ToList();
        _groupCache[userId] = groups;
        return groups;
    }

    private static List<PolicyDocument> FilterByResourceSelector(
        List<PolicyDocument> policies,
        Dictionary<string, object?> resourceAttributes)
    {
        return policies.Where(p => MatchesResourceSelector(p.ResourceSelectorJson, resourceAttributes)).ToList();
    }

    private static bool MatchesResourceSelector(string? selectorJson, Dictionary<string, object?> resourceAttributes)
    {
        if (string.IsNullOrWhiteSpace(selectorJson) || selectorJson == "{}")
            return true;

        try
        {
            var selector = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(selectorJson);
            if (selector == null || selector.Count == 0)
                return true;

            if (selector.TryGetValue("id", out var idEl))
            {
                var selectorId = idEl.GetString();
                if (selectorId == null)
                    return true;

                if (!resourceAttributes.TryGetValue("id", out var resourceId))
                    return false;

                return string.Equals(resourceId?.ToString(), selectorId, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }
        catch
        {
            return true;
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
