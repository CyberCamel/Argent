using System.Globalization;
using Argent.Core.Authorization;
using Argent.Core.DataSources;
using Argent.Core.DomainObjects;
using Argent.Core.Forms;
using Argent.Core.Workflows;
using Argent.Core.Workflows.Designer;
using Argent.Core.Workflows.Execution;
using Argent.Web.Client.Services;
using Argent.WebComponents.Forms;
using Argent.WebComponents.Workflows;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

// SharedResource.resx is embedded by Argent.Core under Argent.Core.Resources.
// Keep this aligned with the server registration so IStringLocalizer<SharedResource>
// resolves the same resource base name in both render modes.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

// --- Persistence stores (HttpClient implementations for WASM) ---
builder.Services.AddScoped<IWorkflowDesignerStore, HttpWorkflowDesignerStore>();
builder.Services.AddScoped<IWorkflowInstanceViewStore, HttpWorkflowInstanceViewStore>();
builder.Services.AddScoped<IWorkflowTaskStore, HttpWorkflowTaskStore>();
builder.Services.AddScoped<IFormDesignerStore, HttpFormDesignerStore>();
builder.Services.AddScoped<ISubjectDirectory, HttpSubjectDirectory>();
builder.Services.AddScoped<IGroupService, HttpGroupService>();
builder.Services.AddScoped<IResourceOwnershipService, HttpResourceOwnershipService>();
builder.Services.AddScoped<IDomainObjectDefinitionService, HttpDomainObjectDefinitionService>();
builder.Services.AddScoped<IDomainObjectStore, HttpDomainObjectStore>();

// --- Designer services (stateful, scoped per circuit/session) ---
builder.Services.AddScoped<Argent.WebComponents.Workflows.Modeler.DesignerService>();
builder.Services.AddScoped<Argent.WebComponents.Forms.Designer.FormDesignerService>();

// --- Workflow node registry (pure reflection, no DB) ---
builder.Services.AddSingleton<IWorkflowNodeRegistry, ArgentWorkflowNodeRegistry>();

var host = builder.Build();

await ApplyCultureFromCookieAsync(host.Services.GetRequiredService<IJSRuntime>());

await host.RunAsync();

static async Task ApplyCultureFromCookieAsync(IJSRuntime js)
{
    var cookie = await js.InvokeAsync<string>("ArgentCulture.get");
    var culture = CultureInfo.GetCultureInfo(ParseCultureName(cookie) ?? "en");
    CultureInfo.DefaultThreadCurrentCulture = culture;
    CultureInfo.DefaultThreadCurrentUICulture = culture;
}

static string? ParseCultureName(string? cookie)
{
    if (string.IsNullOrWhiteSpace(cookie))
    {
        return null;
    }

    foreach (var segment in cookie.Split('|'))
    {
        var eq = segment.IndexOf('=');
        if (eq <= 0)
        {
            continue;
        }

        var key = segment[..eq].Trim();
        var value = segment[(eq + 1)..].Trim();
        if (key == "c" && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return null;
}
