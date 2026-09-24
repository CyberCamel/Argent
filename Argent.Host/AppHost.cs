using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var sqlServer = builder.AddSqlServer("sql-server")
                       .WithDataVolume()
                       .WithLifetime(ContainerLifetime.Persistent);

var appDb = sqlServer.AddDatabase("ArgentDB");

var web = builder.AddProject<Argent_Web>("argent-web")
                 .WithReference(appDb)
                 .WaitFor(appDb);

builder.AddProject<Argent_Engine>("argent-engine")
       .WithReference(appDb)
       .WaitFor(web);   // let the web app run migrations first

builder.Build().Run();