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

            options.AddPolicy("PbacAdmin", policy =>
            {
                policy.Requirements.Add(new PbacRequirement("AdminPage", "View"));
            });
            options.AddPolicy("PbacTaskView", policy =>
            {
                policy.Requirements.Add(new PbacRequirement("Task", "View"));
            });
            options.AddPolicy("PbacTaskComplete", policy =>
            {
                policy.Requirements.Add(new PbacRequirement("Task", "Complete"));
            });
        });

        services.AddScoped<IAuthorizationHandler, PbacAuthorizationHandler>();

        return services;
    }
}
