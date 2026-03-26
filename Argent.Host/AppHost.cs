using Projects;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using System.IO; // Ensure this is present

var builder = DistributedApplication.CreateBuilder(args);

// 1. SQL Server
var sqlServer = builder.AddSqlServer("sql-server")
                       .WithDataVolume()
                       .WithLifetime(ContainerLifetime.Persistent);

var appDb = sqlServer.AddDatabase("AppDatabase");

// 2. The Backend
// FIX FOR THE 'IResourceWithConnectionString' ERROR: 
// Reference the DATABASE (appDb), not the SERVER (sqlServer). 
// Databases implement the interface, the raw Server container does not.
builder.AddProject<Projects.Argent_Web>("backend")
       .WithReference(appDb);

builder.Build().Run();