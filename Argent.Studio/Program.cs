using Argent.Studio;
using Elsa.Studio.Contracts;
using Elsa.Studio.Core.BlazorWasm.Extensions;
using Elsa.Studio.Dashboard.Extensions;
using Elsa.Studio.Extensions;
using Elsa.Studio.Login.BlazorWasm.Extensions;
using Elsa.Studio.Login.Contracts;
using Elsa.Studio.Login.Extensions;
using Elsa.Studio.Login.HttpMessageHandlers;
using Elsa.Studio.Models;
using Elsa.Studio.Shell;
using Elsa.Studio.Shell.Extensions;
using Elsa.Studio.Workflows.Designer.Extensions;
using Elsa.Studio.Workflows.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var configuration = builder.Configuration;

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.RootComponents.RegisterCustomElsaStudioElements();

var backendUrl = configuration.GetSection("Auth:Url").Value ?? "https://localhost:7010";

var backendApiConfig = new BackendApiConfig
{
    ConfigureBackendOptions = options =>
    {
        builder.Configuration.GetSection("ElsaBackend").Bind(options);
    },
    ConfigureHttpClientBuilder = options =>
    {
        options.AuthenticationHandler = typeof(AuthenticatingApiHttpMessageHandler);
    }
};

builder.Services.AddCore();
builder.Services.AddShell();
builder.Services.AddRemoteBackend(backendApiConfig);



builder.Services.AddHttpClient<CustomTokenCredentialsValidator>(client =>
{
    client.BaseAddress = new Uri(backendUrl);
});



// Configure ElsaIdentity first
builder.Services.UseElsaIdentity();

builder.Services.AddScoped<ICredentialsValidator, CustomTokenCredentialsValidator>(sp => sp.GetRequiredService<CustomTokenCredentialsValidator>());
// Then register your custom credentials validator - this replaces the default


builder.Services.AddLoginModule();
builder.Services.AddDashboardModule();
builder.Services.AddWorkflowsModule();

var app = builder.Build();

var startupTaskRunner = app.Services.GetRequiredService<IStartupTaskRunner>();
await startupTaskRunner.RunStartupTasksAsync();

await app.RunAsync();

public record LoginResponse(string AccessToken, string RefreshToken);