using Argent.Models.Authorization;

namespace Argent.Contracts.Authorization;

public interface IResourceOwnershipService
{
    Task GrantOwnershipAsync(string resourceType, Guid resourceId, string userId, CancellationToken ct = default);

    Task ShareAccessAsync(string resourceType, Guid resourceId, string subjectJson, List<string> actions, CancellationToken ct = default);

    Task RevokeAccessAsync(Guid policyId, CancellationToken ct = default);

    Task UpdateActionsAsync(Guid policyId, List<string> newActions, CancellationToken ct = default);

    Task<List<PolicyDocument>> GetResourcePoliciesAsync(string resourceType, Guid resourceId, CancellationToken ct = default);
}
