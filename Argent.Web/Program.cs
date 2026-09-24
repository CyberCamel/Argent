using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Argent.Core.Authorization;
using Argent.Core.Branding;
using Argent.Core.DomainObjects;
using Argent.Core.Forms;
using Argent.Core.Forms.Protocol.V2;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Designer;
using Argent.Core.Workflows.Execution;
using Argent.Core.Serialization;
using Argent.Infrastructure.Data;
using Argent.Core.Identity;
using Argent.Runtime.Authorization;
using Argent.Runtime.Branding;
using Argent.Runtime.DependencyInjection;
using Argent.Runtime.Forms;
using Argent.Runtime.Forms.Stores;
using Argent.Runtime.Workflows;
using Argent.Runtime.Workflows.Execution;
using Argent.Runtime.Workflows.Stores;
using Argent.Web;
using Argent.Web.Api;
using Argent.Web.Extensions;
using Argent.Web.Factories;
using Argent.WebComponents.Forms.Designer;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
var connectionString = builder.Configuration.GetConnectionString("ArgentDB");

// ----- Localization -----
var supportedCultures = new[] { "en", "sv" };
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new("en");
    options.SupportedCultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();
    options.SupportedUICultures = supportedCultures.Select(c => new CultureInfo(c)).ToList();
});

// ----- Services -----
builder.Services.AddRazorPages()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = ArgentJson.Options.PropertyNamingPolicy;
    options.SerializerOptions.DefaultIgnoreCondition = ArgentJson.Options.DefaultIgnoreCondition;
    options.SerializerOptions.Converters.Add(new DomainValueJsonConverter());
});

builder.Services.AddArgentPersistence(connectionString);

builder.Services.AddCors();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
});

// ----- Identity & Security -----

builder.Services.AddArgentSecurity();
builder.Services.AddSingleton<IConditionEvaluator, ConditionEvaluator>();
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
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddSignalR();



builder.Services.AddScoped<Argent.WebComponents.Workflows.Modeler.DesignerService>();
builder.Services.AddScoped<FormDesignerService>();
// --- Persistence stores (server-side EF implementations) ---
builder.Services.AddScoped<IWorkflowDesignerStore, EfWorkflowDesignerStore>();
builder.Services.AddScoped<IWorkflowInstanceViewStore, EfWorkflowInstanceViewStore>();
builder.Services.AddScoped<IWorkflowTaskStore, EfWorkflowTaskStore>();
builder.Services.AddScoped<IFormDesignerStore, EfFormDesignerStore>();
builder.Services.AddScoped<IFormDataStore, EfFormDataStore>();
builder.Services.AddScoped<IFormRuntimeService, FormRuntimeService>();
builder.Services.AddScoped<ISubjectDirectory, EfSubjectDirectory>();
builder.Services.AddScoped<IGroupService, EfGroupService>();

// --- Workflow (web-side services only; engine runs in Argent.Engine) ---
builder.Services.AddArgentWorkflowExecution();
builder.Services.AddArgentDomainObjects();
builder.Services.AddArgentDataSources();
builder.Services.AddScoped<IWorkflowInstanceService, WorkflowInstanceService>();
builder.Services.AddScoped<Argent.WebComponents.DomainObjects.Designer.DomainObjectDesignerService>();
builder.Services.AddSingleton<IBrandingService, BrandingService>();

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

if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();

app.UseStaticFiles();
// Serve static web assets contributed by referenced projects, including the
// Svelte form runtime produced by Argent.Web.Client at /js/argent-form.js.
app.MapStaticAssets();

app.UseRouting();

app.UseRequestLocalization();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorPages();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();

app.MapGet("/api/antiforgery/token", (IAntiforgery antiforgery, HttpContext ctx) =>
{
    var tokens = antiforgery.GetAndStoreTokens(ctx);
    return Results.Ok(new { token = tokens.RequestToken });
}).AllowAnonymous();

app.MapDesignerApi();
app.MapRuntimeDataApi();

app.MapRazorComponents<Program>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode();



Debug.WriteLine("Applying migrations...");
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ArgentDbContext>();
    await context.Database.MigrateAsync();

    if (app.Environment.IsDevelopment())
    {
        // Temporary Protocol v2 cutover cleanup. Delete the disposable form aggregate only when
        // legacy JSON is present; newly-authored v2 forms survive subsequent development restarts.
        // TODO(form-runtime-v2): remove after every shared development database has crossed over.
        await context.Database.ExecuteSqlRawAsync("""
            IF EXISTS (
                SELECT 1 FROM FormDesignDrafts WHERE JSON_VALUE(Definition, '$.protocolVersion') IS NULL
                UNION ALL
                SELECT 1 FROM FormDesignVersions WHERE JSON_VALUE(Definition, '$.protocolVersion') IS NULL
            )
            BEGIN
                DELETE FROM FormCustomData;
                DELETE FROM FormDesignDrafts;
                DELETE FROM FormDesignVersions;
                DELETE FROM FormDesigns;
            END
            """);
    }
}

if (app.Environment.IsDevelopment())
{
    Debug.WriteLine("Seeding data...");
    using (var scope = app.Services.CreateScope())
    {
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<InternalUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ArgentDbContext>();
        await DbInitializer.SeedUsers(config, userManager, roleManager, dbContext);
        await DbInitializer.SeedDomainObjects(dbContext);
        await DbInitializer.RepairBoundFormFieldLabels(dbContext);
    }

    // The canonical form schema is checked in at schemas/argent-form-v2.schema.json. Do not
    // regenerate it from CLR reflection: the TypeScript and .NET runtimes share that exact contract.
}

app.Run();
