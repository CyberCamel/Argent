using Argent.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Execution;

/// <summary>
/// Spins up a real SQL Server in a container so the raw-T-SQL claim path and the
/// serializable-transaction join logic are exercised against the actual database engine
/// (the SQLite suite can run neither). If Docker is unavailable the fixture reports
/// <see cref="Available"/> = false and dependent tests skip rather than fail.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool Available { get; private set; }
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            ConfigureContainerRuntime();

            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();

            await using var db = CreateContext();
            await db.Database.EnsureCreatedAsync();

            Available = true;
        }
        catch
        {
            // No Docker / cannot pull image in this environment — dependent tests skip.
            Available = false;
        }
    }

    /// <summary>
    /// Point Testcontainers at a rootless Podman socket when no Docker host is configured, so
    /// the suite runs unchanged on machines with Podman instead of Docker. Honors an explicit
    /// DOCKER_HOST (real Docker / CI) and no-ops if no Podman socket is present. The socket is
    /// started with <c>systemctl --user start podman.socket</c>.
    /// </summary>
    private static void ConfigureContainerRuntime()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
            return;

        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtimeDir))
            return;

        var podmanSocket = Path.Combine(runtimeDir, "podman", "podman.sock");
        if (!File.Exists(podmanSocket))
            return;

        Environment.SetEnvironmentVariable("DOCKER_HOST", $"unix://{podmanSocket}");
        // Ryuk (the resource reaper) wants privileges rootless Podman doesn't grant; this
        // fixture disposes its own container, so disabling it is safe.
        Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
    }

    public ArgentDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ArgentDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ArgentDbContext(options);
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
            await _container.DisposeAsync();
    }
}

[CollectionDefinition("SqlServer")]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>;
