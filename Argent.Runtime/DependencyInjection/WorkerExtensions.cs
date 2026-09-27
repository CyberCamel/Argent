using Argent.Core.Workers;
using Argent.Infrastructure.Data;
using Argent.Runtime.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Argent.Runtime.DependencyInjection;

public static class WorkerExtensions
{
    /// <summary>
    /// Registers the worker registry and request queue. Call in any host that enqueues work or
    /// serves the worker API. The queue reads a raw connection string for its claim, so it is
    /// built the same way as the work item claimer rather than from the context factory.
    /// </summary>
    public static IServiceCollection AddArgentWorkers(this IServiceCollection services)
    {
        services.AddSingleton<IWorkerRequestQueue>(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<ArgentDbContext>>();
            using var ctx = factory.CreateDbContext();
            return new WorkerRequestQueue(
                factory,
                ctx.Database.GetConnectionString()!,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WorkerRequestQueue>>());
        });

        services.AddSingleton<IWorkerTransport>(sp => (WorkerRequestQueue)sp.GetRequiredService<IWorkerRequestQueue>());
        services.AddSingleton<IWorkerRegistry, WorkerRegistry>();

        return services;
    }

    /// <summary>
    /// Adds the background sweep that keeps the worker subsystem honest. Engine host only: the web
    /// host has no need for it, and running it in both would just duplicate the same atomic updates.
    /// </summary>
    public static IServiceCollection AddArgentWorkerMaintenance(this IServiceCollection services)
    {
        services.AddHostedService<WorkerMaintenanceService>();
        return services;
    }
}
