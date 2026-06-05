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


        });
        return services;
    }
}
