using Argent.Contracts.Authorization;
using Argent.Contracts.DataSources;
using Microsoft.AspNetCore.DataProtection;
using Argent.Contracts.Workflows;
using Argent.Contracts.Workflows.Execution;
using Argent.Engine.Services;
using Argent.Infrastructure.Data;
using Argent.Runtime.Authorization;
using Argent.Runtime.DataSources;
using Argent.Runtime.Workflows;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ArgentDB")
    ?? throw new InvalidOperationException("Connection string 'ArgentDB' is required.");

// ----- DbContext -----
builder.Services.AddDbContextFactory<ArgentDbContext>(options =>
    options.UseSqlServer(connectionString, x => x.MigrationsAssembly("Argent.Infrastructure")));

builder.Services.AddScoped(p =>
    p.GetRequiredService<IDbContextFactory<ArgentDbContext>>().CreateDbContext());

// ----- Data Protection (shared key ring with Argent.Web via SetApplicationName) -----
// In production configure a shared key store (e.g. PersistKeysToDbContext) so both
// services can encrypt/decrypt secrets written by the other.
builder.Services.AddDataProtection()
    .SetApplicationName("Argent");

// ----- HTTP client (REST / SOAP activity handlers) -----
builder.Services.AddHttpClient();

// ----- Workflow Engine core -----
builder.Services.AddSingleton<IWorkClaimer>(sp =>
{
    var factory = sp.GetRequiredService<IDbContextFactory<ArgentDbContext>>();
    using var ctx = factory.CreateDbContext();
    return new WorkClaimer(ctx.Database.GetConnectionString()!);
});
builder.Services.AddSingleton<ITokenRunner, TokenRunner>();
builder.Services.AddSingleton<WorkItemSignal>();
builder.Services.AddScoped<ITokenMovement, TokenMovement>();
builder.Services.AddSingleton<TimerManager>();
builder.Services.AddTransient<RecoveryPass>();
builder.Services.AddSingleton<IAuditService, AuditService>();
builder.Services.AddSingleton<IUserTaskManager, UserTaskManager>();
builder.Services.AddTransient<IWorkflowAudienceResolver, WorkflowAudienceResolver>();
builder.Services.AddSingleton<IWorkflowNodeRegistry, ArgentWorkflowNodeRegistry>();
builder.Services.AddSingleton<IPolicyDecisionService, PolicyDecisionService>();

// ----- Node handlers -----
builder.Services.AddTransient<INodeHandler, StartEventHandler>();
builder.Services.AddTransient<INodeHandler, EndEventHandler>();
builder.Services.AddTransient<INodeHandler, ExclusiveGatewayEvaluator>();
builder.Services.AddTransient<INodeHandler, InclusiveGatewayEvaluator>();
builder.Services.AddTransient<INodeHandler, ParallelGatewayEvaluator>();
builder.Services.AddTransient<INodeHandler, SQLActivityHandler>();
builder.Services.AddTransient<INodeHandler, RestActivityHandler>();
builder.Services.AddTransient<INodeHandler, JintActivityHandler>();
builder.Services.AddTransient<INodeHandler, UserActivityHandler>();
builder.Services.AddTransient<INodeHandler, ScriptActivityHandler>();
builder.Services.AddTransient<INodeHandler, CatchingTimerHandler>();
builder.Services.AddTransient<INodeHandler, TimerBoundaryEventHandler>();

// ----- Data sources (needed by SQL/REST activity handlers) -----
builder.Services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddScoped<IDataSourceProvider, SqlDataSourceProvider>();
builder.Services.AddScoped<IDataSourceProvider, RestDataSourceProvider>();
builder.Services.AddScoped<IDataSourceProvider, SoapDataSourceProvider>();
builder.Services.AddScoped<IDataSourceCatalog, DataSourceCatalog>();
builder.Services.AddScoped<IDataSourceRunner, DataSourceRunner>();

// ----- OpenTelemetry -----
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(WorkflowMeter.Engine.Name));

// ----- Hosted service -----
builder.Services.AddHostedService<WorkflowEngine>();

builder.Build().Run();
