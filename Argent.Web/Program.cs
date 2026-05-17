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
using Argent.Web;
using Argent.Web.Extensions;
using Argent.Web.Factories;
using Argent.WebComponents.Core.Forms;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Schema.Generation;
using System.Diagnostics;
using System.Globalization;

var rootCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentCulture = rootCulture;
CultureInfo.DefaultThreadCurrentUICulture = rootCulture;


var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
var connectionString = builder.Configuration.GetConnectionString("AppDatabase");

// ----- MVC & Razor Pages -----
builder.Services.AddRazorPages();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();


builder.Services.AddControllersWithViews()
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (type, factory) =>
            factory.Create(typeof(SharedResource));
    });

builder.Services.AddHttpClient();

// ----- Identity & Security -----
builder.Services.AddScoped<IUserClaimsPrincipalFactory<InternalUser>, AdditionalUserClaimsPrincipalFactory>();
builder.Services.AddArgentSecurity();
builder.Services.AddIdentity<InternalUser, IdentityRole<Guid>>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddRoles<IdentityRole<Guid>>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
});

builder.Services.AddSignalR();


// ----- DbContext -----
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, x => x.MigrationsAssembly("Argent.Infrastructure")));



var componentRegistry = new ArgentFormComponentRegistry();
componentRegistry.Register("Row", typeof(ArgentRow));
componentRegistry.Register("Column", typeof(ArgentColumn));
componentRegistry.Register("HtmlBox", typeof(ArgentHtml));
componentRegistry.Register("TextField", typeof(ArgentText));
componentRegistry.Register("DropdownField", typeof(ArgentDropdown));
componentRegistry.Register("NumericField", typeof(ArgentDropdown));
componentRegistry.Register("CheckboxField", typeof(ArgentCheckbox));

builder.Services.AddSingleton<IFormComponentRegistry>(componentRegistry);
builder.Services.AddSingleton<IValidationRegistry>(new ArgentValidationRegistry());
builder.Services.AddScoped<IFormContext, ArgentFormContext>();
builder.Services.AddScoped<DesignerService, DesignerService>();
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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapControllers();
app.MapRazorPages();

app.MapRazorComponents<Program>()
    .AddInteractiveServerRenderMode();



Debug.WriteLine("Seeding data...");
using (var scope = app.Services.CreateScope())
{

    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<InternalUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    await DbInitializer.SeedUsers(userManager, roleManager);

    var serializer = new JSchemaGenerator();
    var formSchema = serializer.Generate(typeof(FormDefinition));
    File.WriteAllLines(".\\resources\\form-schema.json", formSchema.ToString().Split('\n'));
}

app.Run();