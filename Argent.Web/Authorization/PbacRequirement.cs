using Microsoft.AspNetCore.Authorization;

namespace Argent.Web.Authorization;

public class PbacRequirement : IAuthorizationRequirement
{
    public string ResourceType { get; }
    public string Action { get; }

    public PbacRequirement(string resourceType, string action)
    {
        ResourceType = resourceType;
        Action = action;
    }
}
