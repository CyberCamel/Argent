using Argent.Contracts.Forms;
using Argent.Contracts.Workflows;
using Argent.Contracts.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Models.Forms.Components;
using Argent.Models.Identity;
using Argent.Runtime.Forms;
using Argent.Runtime.Workflows;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Modeling;
using Argent.Runtime.Forms.Modeling;
using Argent.Web;
using Argent.Web.Extensions;
using Argent.Web.Factories;
using Argent.WebComponents.Core.Forms;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Schema.Generation;
using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Internal;

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
builder.Services.AddSingleton<IFormValidatorRegistry>(new ArgentFormValidatorRegistry());
builder.Services.AddSingleton<IConditionEvaluator, ConditionEvaluator>();
builder.Services.AddScoped<IFormContext>(sp =>
{
    var validatorRegistry = sp.GetRequiredService<IFormValidatorRegistry>();
    var conditionEvaluator = sp.GetRequiredService<IConditionEvaluator>();
    return new ArgentFormContext(validatorRegistry, conditionEvaluator);
});
builder.Services.AddScoped<DesignerService, DesignerService>();
builder.Services.AddScoped<FormDesignerService, FormDesignerService>();
builder.Services.AddSingleton<IWorkflowNodeRegistry, ArgentWorkflowNodeRegistry>();

builder.Services.AddScoped<IWorkItemRepository, WorkItemRepository>();
builder.Services.AddScoped<IWorkRouter, WorkRouter>();
builder.Services.AddHostedService<WorkflowEngine>();

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