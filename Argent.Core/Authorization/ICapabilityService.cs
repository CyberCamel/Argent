using Argent.Core.Authorization;

namespace Argent.Core.Authorization;

public interface ICapabilityService
{
    Task<List<PolicyDocument>> GetCapabilitiesAsync(string resourceType, CancellationToken ct = default);

    Task GrantAsync(string resourceType, string subjectJson, string action, CancellationToken ct = default);

    Task RevokeActionAsync(Guid policyId, string action, CancellationToken ct = default);

    Task InvalidateCacheAsync(CancellationToken ct = default);
}
