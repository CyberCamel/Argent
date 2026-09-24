using System.Data;
using System.Security.Claims;
using System.Text;
using Argent.Core.DomainObjects;
using Argent.Core.DomainObjects.Querying;
using Argent.Core.DataSources;
using Argent.Core.Authorization;
using Argent.Core.Workflows.Execution;
using Argent.Infrastructure.Data;
using Argent.Runtime.DomainObjects;
using Argent.Runtime.Tests.Workflows.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Argent.Runtime.Tests.DomainObjects;

/// <summary>
/// Seeds a DomainObject + 100k records once per class via a static flag + lock.
/// The seed data lives for the duration of the test run and is reused by all 4 tests.
/// </summary>
[Trait("Category", "Sql")]
[Collection("SqlServer")]
public class DomainObjectStoreSqlQueryTests
{
    private readonly SqlServerFixture _fixture;
    private static readonly object SeedLock = new();
    private static bool _seeded;
    private static bool _seedAttempted;

    private static readonly Guid DomainObjectId = Guid.NewGuid();
    private const string ObjectKey = "testPushdownSql";
    private const int RecordCount = 10_000;

    public DomainObjectStoreSqlQueryTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Seed once per test-run, not once per test method.</summary>
    private async Task EnsureSeededAsync()
    {
        if (_seeded) return;
        if (!_fixture.Available) return;

        lock (SeedLock)
        {
            if (_seeded) return;
            if (_seedAttempted) return;
            _seedAttempted = true;
        }

        await using var db = _fixture.CreateContext();
        await db.Database.EnsureCreatedAsync();

        var obj = new DomainObject
        {
            Id = DomainObjectId,
            Key = ObjectKey,
            Name = "Pushdown Test Object",
            CreatedOn = DateTime.UtcNow,
            UpdatedOn = DateTime.UtcNow,
        };
        db.DomainObjects.Add(obj);
        await db.SaveChangesAsync();

        db.DomainObjectVersions.Add(new DomainObjectVersion
        {
            Id = Guid.NewGuid(),
            DomainObjectId = DomainObjectId,
            Version = new Version(1, 0),
            Name = "v1",
            State = DomainObjectState.Published,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            Definition = new DomainObjectDefinition
            {
                Key = ObjectKey,
                Properties =
                [
                    new() { Key = "name", Type = DomainPropertyType.Text },
                    new() { Key = "age", Type = DomainPropertyType.Number },
                    new() { Key = "active", Type = DomainPropertyType.Boolean },
                    new() { Key = "department", Type = DomainPropertyType.Text },
                    new() { Key = "salary", Type = DomainPropertyType.Number },
                ],
            },
        });

        await db.SaveChangesAsync();

        // Bulk-insert 10k records using a batched approach (much faster than row-by-row)
        var batchSize = 1000;
        var totalBatches = RecordCount / batchSize;
        for (int batch = 0; batch < totalBatches; batch++)
        {
            var sb = new StringBuilder();
            sb.Append("INSERT INTO DomainObjectRecords (Id, DomainObjectId, [Values], CreatedAt, UpdatedAt) VALUES");
            var start = batch * batchSize;
            for (int i = 0; i < batchSize; i++)
            {
                var n = start + i;
                if (i > 0) sb.Append(',');
                sb.Append("(NEWID(), @domainObjectId, N'{\"name\":\"Person");
                sb.Append(n);
                sb.Append("\",\"age\":");
                sb.Append(20 + (n % 40));
                sb.Append(",\"active\":");
                sb.Append(n % 2 == 0 ? "true" : "false");
                sb.Append(",\"department\":");
                sb.Append((n % 3) switch { 0 => "\"Engineering\"", 1 => "\"Marketing\"", _ => "\"Sales\"" });
                sb.Append(",\"salary\":");
                sb.Append(30000 + (n * 997 % 100000));
                sb.Append("}', GETUTCDATE(), GETUTCDATE())");
            }

            var conn = db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sb.ToString();
            cmd.CommandTimeout = 120;
            var p = cmd.CreateParameter();
            p.ParameterName = "@domainObjectId";
            p.Value = DomainObjectId;
            cmd.Parameters.Add(p);
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();
            await cmd.ExecuteNonQueryAsync();
        }
        _seeded = true;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private IDomainObjectStore CreateStore()
    {
        var factoryMock = new Mock<IDbContextFactory<ArgentDbContext>>();
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_fixture.CreateContext);

        var httpContextMock = new Mock<IHttpContextAccessor>();
        httpContextMock.Setup(h => h.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-user"), new Claim(ClaimTypes.Name, "test"), new Claim(ClaimTypes.Role, "Admin")]))
        });

        return new DomainObjectStore(
            factoryMock.Object,
            httpContextMock.Object,
            Mock.Of<IDataSourceRunner>(),
            Mock.Of<IPolicyDecisionService>(),
            Mock.Of<IAuditService>());
    }

    /// <summary>Cross-validate: load all records in-memory and run ApplyQuery (the in-memory reference).</summary>
    private async Task<DomainQueryResult> LoadInMemoryAsync(DomainQuery query)
    {
        await using var db = _fixture.CreateContext();
        var all = await db.DomainObjectRecords.AsNoTracking()
            .Where(r => r.DomainObjectId == DomainObjectId)
            .ToListAsync();

        var records = all.Select(e => new DomainRecord
        {
            Id = e.Id,
            ObjectKey = ObjectKey,
            Values = e.Values,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
        }).ToList();

        return DomainQueryCompiler.ApplyQuery(records, query);
    }

    // ── Acceptance tests ────────────────────────────────────────────

    [SkippableFact]
    public async Task FilteredQuery_DoesNotMaterializeEntireTable()
    {
        Skip.IfNot(_fixture.Available, "SQL Server container not available");
        await EnsureSeededAsync();

        var store = CreateStore();
        var dept = "Engineering";

        var result = await store.QueryAsync(ObjectKey, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "department", Operator = DomainFilterOperator.Equals, Value = dept }]
            }
        });

        Assert.InRange(result.TotalCount, 3_333, 3_334);
        Assert.All(result.Records, r =>
        {
            Assert.Equal(dept, r.Values["department"]);
            Assert.StartsWith("Person", (string)r.Values["name"]!);
        });
    }

    [SkippableFact]
    public async Task FilteredQuery_WithAllOperators_MatchesInMemoryReference()
    {
        Skip.IfNot(_fixture.Available, "SQL Server container not available");
        await EnsureSeededAsync();

        var store = CreateStore();

        var operators = new (DomainFilterOperator Op, object? Val, string property)[]
        {
            (DomainFilterOperator.Equals, "Engineering", "department"),
            (DomainFilterOperator.NotEquals, "Engineering", "department"),
            (DomainFilterOperator.GreaterThan, 30, "age"),
            (DomainFilterOperator.GreaterThanOrEqual, 30, "age"),
            (DomainFilterOperator.LessThan, 30, "age"),
            (DomainFilterOperator.LessThanOrEqual, 30, "age"),
            (DomainFilterOperator.Contains, "son", "name"),
            (DomainFilterOperator.StartsWith, "Person1", "name"),
            (DomainFilterOperator.EndsWith, "7", "name"),
            (DomainFilterOperator.IsNull, null, "nonexistent"),
            (DomainFilterOperator.IsNotNull, null, "department"),
        };

        foreach (var (op, val, prop) in operators)
        {
            var query = new DomainQuery
            {
                Filter = new DomainFilter
                {
                    Conditions = [new() { Property = prop, Operator = op, Value = val }]
                },
                Sort = [new() { Property = "name" }],
                Take = 50,
            };

            var pushdown = await store.QueryAsync(ObjectKey, query);
            var inMemory = await LoadInMemoryAsync(query);

            Assert.Equal(inMemory.TotalCount, pushdown.TotalCount);
            Assert.Equal(inMemory.Records.Count, pushdown.Records.Count);

            var pushdownIds = pushdown.Records.Select(r => r.Id).OrderBy(id => id).ToList();
            var inMemoryIds = inMemory.Records.Select(r => r.Id).OrderBy(id => id).ToList();
            Assert.Equal(inMemoryIds, pushdownIds);
        }
    }

    [SkippableFact]
    public async Task FilteredQuery_WithSortAndPaging_WorksCorrectly()
    {
        Skip.IfNot(_fixture.Available, "SQL Server container not available");
        await EnsureSeededAsync();

        var store = CreateStore();

        var query = new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "active", Operator = DomainFilterOperator.Equals, Value = true }]
            },
            Sort = [new() { Property = "name", Descending = true }],
            Skip = 100,
            Take = 25,
        };

        var pushdown = await store.QueryAsync(ObjectKey, query);
        var inMemory = await LoadInMemoryAsync(query);

        Assert.Equal(inMemory.TotalCount, pushdown.TotalCount);
        Assert.Equal(25, pushdown.Records.Count);
        Assert.Equal(inMemory.Records.Select(r => r.Id).ToList(),
            pushdown.Records.Select(r => r.Id).ToList());
    }

    [SkippableFact]
    public async Task FilteredQuery_WithOrGroup_WorksCorrectly()
    {
        Skip.IfNot(_fixture.Available, "SQL Server container not available");
        await EnsureSeededAsync();

        var store = CreateStore();

        var query = new DomainQuery
        {
            Filter = new DomainFilter
            {
                Logic = DomainFilterLogic.Or,
                Conditions =
                [
                    new() { Property = "department", Operator = DomainFilterOperator.Equals, Value = "Engineering" },
                    new() { Property = "department", Operator = DomainFilterOperator.Equals, Value = "Sales" },
                ]
            },
            Take = 50,
        };

        var pushdown = await store.QueryAsync(ObjectKey, query);
        var inMemory = await LoadInMemoryAsync(query);

        Assert.Equal(inMemory.TotalCount, pushdown.TotalCount);
        Assert.Equal(inMemory.Records.Count, pushdown.Records.Count);
        var pushdownIds = pushdown.Records.Select(r => r.Id).OrderBy(id => id).ToList();
        var inMemoryIds = inMemory.Records.Select(r => r.Id).OrderBy(id => id).ToList();
        Assert.Equal(inMemoryIds, pushdownIds);
    }

    // ── WP-2.2: DB-enforced uniqueness ─────────────────────────────

    [SkippableFact]
    public async Task UniqueProperty_PreventsDuplicateInsert()
    {
        Skip.IfNot(_fixture.Available, "SQL Server container not available");

        var uniqueKey = "testUnique_" + Guid.NewGuid().ToString("N")[..8];
        var store = CreateStore();
        var defService = CreateDefinitionService();

        await defService.CreateAsync(uniqueKey, "Unique Test");
        await defService.SaveDraftAsync(
            (await defService.GetSummariesAsync()).First(s => s.Key == uniqueKey).Id,
            new DomainObjectDefinition
            {
                Key = uniqueKey,
                Properties =
                [
                    new() { Key = "email", Type = DomainPropertyType.Text, Unique = true }
                ]
            });
        await defService.PublishAsync(
            (await defService.GetSummariesAsync()).First(s => s.Key == uniqueKey).Id);

        var record1 = await store.CreateAsync(uniqueKey,
            new Dictionary<string, object?> { ["email"] = "alice@example.com" });
        Assert.NotNull(record1);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            store.CreateAsync(uniqueKey,
                new Dictionary<string, object?> { ["email"] = "alice@example.com" }));
        Assert.Contains("email", ex.Errors.Select(e => e.Property));
        Assert.Contains("unique", ex.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task UniqueProperty_ConcurrentInserts_RaceSafe()
    {
        Skip.IfNot(_fixture.Available, "SQL Server container not available");

        var uniqueKey = "testRace_" + Guid.NewGuid().ToString("N")[..8];
        var store = CreateStore();
        var defService = CreateDefinitionService();

        await defService.CreateAsync(uniqueKey, "Race Test");
        await defService.SaveDraftAsync(
            (await defService.GetSummariesAsync()).First(s => s.Key == uniqueKey).Id,
            new DomainObjectDefinition
            {
                Key = uniqueKey,
                Properties =
                [
                    new() { Key = "email", Type = DomainPropertyType.Text, Unique = true }
                ]
            });
        await defService.PublishAsync(
            (await defService.GetSummariesAsync()).First(s => s.Key == uniqueKey).Id);

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            store.CreateAsync(uniqueKey,
                new Dictionary<string, object?> { ["email"] = "race@example.com" }));
        var results = await Task.WhenAll(tasks.Select(async t =>
        {
            try { return new CaptureResult { Success = true, Record = await t }; }
            catch (Exception ex) { return new CaptureResult { Success = false, Exception = ex }; }
        }));

        var successes = results.Count(r => r.Success);
        var failures = results.Count(r => !r.Success);
        Assert.Equal(1, successes);
        Assert.Equal(9, failures);
        Assert.All(results.Where(r => !r.Success), r =>
        {
            var dex = r.Exception!;
            Assert.IsType<DomainValidationException>(dex);
            Assert.Contains("email", ((DomainValidationException)dex).Errors.Select(e => e.Property));
        });
    }

    private class CaptureResult
    {
        public bool Success { get; set; }
        public DomainRecord? Record { get; set; }
        public Exception? Exception { get; set; }
    }

    private IDomainObjectDefinitionService CreateDefinitionService()
    {
        var factoryMock = new Mock<IDbContextFactory<ArgentDbContext>>();
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_fixture.CreateContext);

        var httpContextMock = new Mock<IHttpContextAccessor>();
        httpContextMock.Setup(h => h.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-user"), new Claim(ClaimTypes.Name, "test"), new Claim(ClaimTypes.Role, "Admin")]))
        });

        return new DomainObjectDefinitionService(factoryMock.Object, httpContextMock.Object);
    }
}
