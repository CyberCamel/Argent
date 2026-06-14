using Argent.Contracts.Authorization;
using Argent.Models.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Argent.Web.Authorization;

public class PbacAuthorizationHandler(IPolicyDecisionService _policyService)
    : AuthorizationHandler<PbacRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PbacRequirement requirement)
    {
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var roles = context.User.Claims
            .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        var resourceAttrs = new Dictionary<string, object?>
        {
            ["type"] = requirement.ResourceType,
            ["action"] = requirement.Action
        };

        var result = await _policyService.EvaluateAsync(userId, roles, requirement.ResourceType, resourceAttrs, requirement.Action);

        if (result == PolicyDecision.Allow)
            context.Succeed(requirement);
    }
}
