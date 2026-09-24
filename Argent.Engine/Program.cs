using Argent.Core.Authorization;
using Argent.Runtime.Authorization;
using Argent.Runtime.DependencyInjection;
using Argent.Runtime.Workflows.Execution;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ArgentDB")
    ?? throw new InvalidOperationException("Connection string 'ArgentDB' is required.");

builder.Services.AddArgentPersistence(connectionString);
// Runtime services shared with the web host use the accessor for an optional current-user
// fallback. Background workflow execution has no request context, so HttpContext remains null.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IConditionEvaluator, ConditionEvaluator>();
builder.Services.AddArgentWorkflowExecution();
builder.Services.AddArgentDataSources();
builder.Services.AddArgentDomainObjects();
builder.Services.AddArgentEngineExecution();

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(WorkflowMeter.Engine.Name));

builder.Build().Run();
