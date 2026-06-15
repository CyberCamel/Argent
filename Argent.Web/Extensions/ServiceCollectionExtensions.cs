using Argent.Contracts.Authorization;
using Argent.Runtime.Authorization;
using Argent.Web.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Argent.Web.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArgentSecurity(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy("UserAdminOnly", policy =>
            {
                policy.RequireRole("UserAdmin","SuperAdmin");
            });
            options.AddPolicy("FlowAdminOnly", policy =>
            {
                policy.RequireRole("FlowAdmin", "SuperAdmin");
            });
            options.AddPolicy("FormAdminOnly", policy =>
            {
                policy.RequireRole("FormAdmin", "SuperAdmin");
            });
            options.AddPolicy("SuperAdminOnly", policy =>
            {
                policy.RequireRole("SuperAdmin");
            });
            options.AddPolicy("PbacWorkflowRun", policy =>
            {
                policy.Requirements.Add(new PbacRequirement("Workflow", "run"));
            });
            options.AddPolicy("PbacWorkflowModel", policy =>
            {
                policy.Requirements.Add(new PbacRequirement("Workflow", "model"));
            });
            options.AddPolicy("PbacFormDesign", policy =>
            {
                policy.Requirements.Add(new PbacRequirement("Form", "design"));
            });
        });

        services.AddScoped<IAuthorizationHandler, PbacAuthorizationHandler>();
        services.AddSingleton<IPbacResourceRegistry, PbacResourceRegistry>();
        services.AddScoped<IResourceOwnershipService, ResourceOwnershipService>();
        services.AddScoped<ICapabilityService, CapabilityService>();

        return services;
    }
}
