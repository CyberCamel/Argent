using Argent.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Argent.Runtime.DependencyInjection;

public static class PersistenceExtensions
{
    public static IServiceCollection AddArgentPersistence(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<ArgentDbContext>(options =>
            options.UseSqlServer(connectionString, x => x.MigrationsAssembly("Argent.Infrastructure")));

        services.AddScoped(p =>
            p.GetRequiredService<IDbContextFactory<ArgentDbContext>>().CreateDbContext());

        services.AddDataProtection()
            .SetApplicationName("Argent")
            .PersistKeysToDbContext<ArgentDbContext>();

        services.AddHttpClient();

        return services;
    }
}
