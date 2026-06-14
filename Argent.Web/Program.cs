using System.Diagnostics;
using System.Globalization;
using Argent.Contracts.Authorization;
using Argent.Contracts.DataSources;
using Argent.Contracts.DomainObjects;
using Argent.Contracts.Forms;
using Argent.Contracts.Workflows;
using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Forms.Components;
using Argent.Models.Identity;
using Argent.Runtime.Authorization;
using Argent.Runtime.DataSources;
using Argent.Runtime.DomainObjects;
using Argent.Runtime.Forms;
using Argent.Runtime.Forms.Modeling;
using Argent.Runtime.Workflows;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Handlers;
using Argent.Runtime.Workflows.Modeling;
using Argent.Web;
using Argent.Web.Extensions;
using Argent.Web.Factories;
using Argent.Web.Services;
using Argent.WebComponents.Core.Forms;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Schema.Generation;

var rootCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentCulture = rootCulture;
CultureInfo.DefaultThreadCurrentUICulture = rootCulture;


var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
var connectionString = builder.Configuration.GetConnectionString("ArgentDB");

// ----- Services -----
builder.Services.AddRazorPages();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.AddCors();
builder.Services.AddDataProtection();


// ----- DbContext -----
builder.Services.AddDbContextFactory<ArgentDbContext>(options =>
    options.UseSqlServer(connectionString, x => x.MigrationsAssembly("Argent.Infrastructure")));

// This ensures that components/services expecting a standard scoped DbContext 
// (like ASP.NET Core Identity) can still resolve it normally.
builder.Services.AddScoped(p => 
    p.GetRequiredService<IDbContextFactory<ArgentDbContext>>().CreateDbContext());

// ----- Identity & Security -----

builder.Services.AddArgentSecurity();
builder.Services.AddIdentity<InternalUser, IdentityRole<Guid>>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireNonAlphanumeric  = false;
})
.AddEntityFrameworkStores<ArgentDbContext>()
.AddRoles<IdentityRole<Guid>>()
.AddDefaultTokenProviders();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<InternalUser>, AdditionalUserClaimsPrincipalFactory>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
});

builder.Services.AddSignalR();



var componentRegistry = new ArgentFormComponentRegistry();
componentRegistry.Register("Row", typeof(ArgentRow));
componentRegistry.Register("Column", typeof(ArgentColumn));
componentRegistry.Register("Flex", typeof(ArgentFlex));
componentRegistry.Register("Fieldset", typeof(ArgentFieldset));
componentRegistry.Register("Tabs", typeof(ArgentTabs));
componentRegistry.Register("Accordion", typeof(ArgentAccordion));
componentRegistry.Register("HtmlBox", typeof(ArgentHtml));
componentRegistry.Register("TextField", typeof(ArgentText));
componentRegistry.Register("DropdownField", typeof(ArgentDropdown));
componentRegistry.Register("NumericField", typeof(ArgentNumeric));
componentRegistry.Register("CheckboxField", typeof(ArgentCheckbox));

builder.Services.AddSingleton<IFormComponentRegistry>(componentRegistry);
builder.Services.AddSingleton<IConditionEvaluator, ConditionEvaluator>();
builder.Services.AddSingleton<IFormValidator, FormValidationService>();
builder.Services.AddScoped<IFormContext>(sp => new ArgentFormContext(
    sp.GetRequiredService<IFormValidator>(),
    sp.GetRequiredService<IConditionEvaluator>()));
builder.Services.AddScoped<DesignerService, DesignerService>();
builder.Services.AddScoped<FormDesignerService, FormDesignerService>();
builder.Services.AddSingleton<IWorkflowNodeRegistry, ArgentWorkflowNodeRegistry>();

// --- Workflow Engine ---
builder.Services.AddSingleton<IWorkClaimer>(sp =>
{
    var factory = sp.GetRequiredService<IDbContextFactory<ArgentDbContext>>();
    using var ctx = factory.CreateDbContext();
    return new WorkClaimer(ctx.Database.GetConnectionString()!);
});
builder.Services.AddSingleton<ITokenRunner, TokenRunner>();
builder.Services.AddScoped<ITokenMovement, TokenMovement>();
builder.Services.AddScoped<IWorkflowInstanceManager, WorkflowInstanceManager>();
builder.Services.AddTransient<RecoveryPass>();
builder.Services.AddSingleton<IUserTaskManager, UserTaskManager>();
builder.Services.AddSingleton<IAuditService, AuditService>();
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
    .AddMeter(WorkflowMeter.Engine.Name));
builder.Services.AddHostedService<WorkflowEngine>();

// --- Workflow Handlers ---
builder.Services.AddTransient<INodeHandler, StartEventHandler>();
builder.Services.AddTransient<INodeHandler, EndEventHandler>();
builder.Services.AddTransient<INodeHandler, ExclusiveGatewayEvaluator>();
builder.Services.AddTransient<INodeHandler, InclusiveGatewayEvaluator>();
builder.Services.AddTransient<INodeHandler, ParallelGatewayEvaluator>();
builder.Services.AddTransient<INodeHandler, SQLActivityHandler>();
builder.Services.AddTransient<INodeHandler, RestActivityHandler>();
builder.Services.AddTransient<INodeHandler, JintActivityHandler>();
builder.Services.AddTransient<INodeHandler, UserActivityHandler>();

builder.Services.AddScoped<IDomainObjectDefinitionService, DomainObjectDefinitionService>();
builder.Services.AddScoped<IDomainObjectStore, DomainObjectStore>();
builder.Services.AddScoped<DomainObjectDesignerService, DomainObjectDesignerService>();
builder.Services.AddSingleton<IPolicyDecisionService, PolicyDecisionService>();

builder.Services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddScoped<IDataSourceProvider, SqlDataSourceProvider>();
builder.Services.AddScoped<IDataSourceProvider, RestDataSourceProvider>();
builder.Services.AddScoped<IDataSourceProvider, SoapDataSourceProvider>();
builder.Services.AddScoped<IDataSourceCatalog, DataSourceCatalog>();
builder.Services.AddScoped<IDataSourceRunner, DataSourceRunner>();

builder.Services.AddLogging(config =>
{
    config.AddConsole();
    config.SetMinimumLevel(LogLevel.Debug);
});

var app = builder.Build();

// ----- Middleware pipeline -----
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorPages();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

// --- User Task API ---
app.MapPost("/api/tasks/{taskId:guid}/complete", async (
    Guid taskId,
    IUserTaskManager taskManager,
    HttpRequest request,
    CancellationToken ct) =>
{
    var completedBy = request.Headers["X-Completed-By"].FirstOrDefault() ?? "system";
    string? resultData = null;

    if (request.HasJsonContentType())
    {
        var body = await request.ReadFromJsonAsync<CompleteTaskRequest>(ct);
        resultData = body?.Result;
    }

    await taskManager.CompleteTaskAsync(taskId, completedBy, [], resultData, ct);
    return Results.Ok(new { status = "completed" });
});

app.MapRazorComponents<Program>()
    .AddInteractiveServerRenderMode();



Debug.WriteLine("Seeding data...");
using (var scope = app.Services.CreateScope())
{
    // Resolve the factory instead of the raw context to guarantee isolation
    var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ArgentDbContext>>();
    await using var context = contextFactory.CreateDbContext();

    context.Database.EnsureDeleted();
    context.Database.EnsureCreated();

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<InternalUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    await DbInitializer.SeedUsers(userManager, roleManager);

    var serializer = new JSchemaGenerator();
    var formSchema = serializer.Generate(typeof(FormDefinition));
    File.WriteAllLines(@".\Resources\form-schema.json", formSchema.ToString().Split('\n'));
}

app.Run();

namespace Argent.Web
{
    record CompleteTaskRequest(string? Result);
}