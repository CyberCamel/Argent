using Argent.Models.Authorization;

namespace Argent.Contracts.Authorization;

public interface IPolicyDecisionService
{
    Task<PolicyDecision> EvaluateAsync(
        string userId,
        List<string> roles,
        string resourceType,
        Dictionary<string, object?> resourceAttributes,
        string action,
        Dictionary<string, object?>? environment = null,
        CancellationToken ct = default);

    Task<List<PolicyDocument>> GetApplicablePoliciesAsync(
        string resourceType,
        string action,
        CancellationToken ct = default);

    Task InvalidateCacheAsync(CancellationToken ct = default);
}
