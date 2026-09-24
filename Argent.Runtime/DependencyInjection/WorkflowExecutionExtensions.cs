using Argent.Core.Authorization;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Execution;
using Argent.Runtime.Authorization;
using Argent.Runtime.Workflows;
using Argent.Runtime.Workflows.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Argent.Runtime.DependencyInjection;

public static class WorkflowExecutionExtensions
{
    public static IServiceCollection AddArgentWorkflowExecution(this IServiceCollection services)
    {
        services.AddSingleton<ITaskInboxService, TaskInboxService>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddTransient<IWorkflowAudienceResolver, WorkflowAudienceResolver>();
        services.AddSingleton<IPolicyDecisionService, PolicyDecisionService>();
        services.AddSingleton<IWorkflowNodeRegistry, ArgentWorkflowNodeRegistry>();

        return services;
    }
}
