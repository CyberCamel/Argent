using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sql-server")
                       .WithDataVolume()
                       .WithLifetime(ContainerLifetime.Persistent);

var appDb = sqlServer.AddDatabase("AppDatabase");

builder.AddProject<Argent_Web>("backend")
       .WithReference(appDb);

builder.Build().Run();