using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sql-server")
                       .WithDataVolume()
                       .WithLifetime(ContainerLifetime.Persistent);

var appDb = sqlServer.AddDatabase("ArgentDB");

builder.AddProject<Argent_Web>("Argent")
       .WithReference(appDb)
       .WaitFor(appDb);

builder.Build().Run();