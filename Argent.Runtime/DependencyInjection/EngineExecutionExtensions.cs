using Argent.Core.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Runtime.Workflows;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Argent.Runtime.DependencyInjection;

public static class EngineExecutionExtensions
{
    public static IServiceCollection AddArgentEngineExecution(this IServiceCollection services)
    {
        services.AddSingleton<IWorkClaimer>(sp =>
        {
            var factory = sp.GetRequiredService<IDbContextFactory<ArgentDbContext>>();
            using var ctx = factory.CreateDbContext();
            return new WorkClaimer(ctx.Database.GetConnectionString()!);
        });
        services.AddSingleton<ITokenRunner, TokenRunner>();
        services.AddSingleton<WorkItemSignal>();
        services.AddScoped<ITokenMovement, TokenMovement>();
        services.AddSingleton<TimerManager>();
        services.AddTransient<RecoveryPass>();

        services.AddTransient<INodeHandler, StartEventHandler>();
        services.AddTransient<INodeHandler, EndEventHandler>();
        services.AddTransient<INodeHandler, ExclusiveGatewayEvaluator>();
        services.AddTransient<INodeHandler, InclusiveGatewayEvaluator>();
        services.AddTransient<INodeHandler, ParallelGatewayEvaluator>();
        services.AddTransient<INodeHandler, SQLActivityHandler>();
        services.AddTransient<INodeHandler, RestActivityHandler>();
        services.AddTransient<INodeHandler, JintActivityHandler>();
        services.AddTransient<INodeHandler, UserActivityHandler>();
        services.AddTransient<INodeHandler, ScriptActivityHandler>();
        services.AddTransient<INodeHandler, CatchingTimerHandler>();
        services.AddTransient<INodeHandler, TimerBoundaryEventHandler>();

        services.AddHostedService<WorkflowEngine>();

        return services;
    }
}
