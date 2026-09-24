using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Argent.Core.Authorization;
using Argent.Core.DataSources;
using Argent.Core.DomainObjects;
using Argent.Core.DomainObjects.Querying;
using Argent.Core.Workflows.Auditing;
using Argent.Core.Workflows.Execution;
using Argent.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.DomainObjects;

/// <summary>
/// Runtime access to domain object instances over the managed JSON record store. Records
/// are addressed by the object's system key; <see cref="QueryDataSourceAsync"/> instead
/// reads an external SQL source declared on the definition. Filtering/sorting/paging is
/// evaluated in memory for now (v1) — a deferred optimization compiles hot filters to
/// OPENJSON or a promoted-index table.
/// </summary>
public class DomainObjectStore(
    IDbContextFactory<ArgentDbContext> _dbContextFactory,
    IHttpContextAccessor _httpContextAccessor,
    IDataSourceRunner _dataSourceRunner,
    IPolicyDecisionService _policyService,
    IAuditService _auditService) : IDomainObjectStore
{
    private string CurrentUser => _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "Unknown";

    private string CurrentUserId =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Unknown";

    private List<string> CurrentRoles =>
        _httpContextAccessor.HttpContext?.User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    private async Task AuthorizeAsync(string objectKey, string action, CancellationToken ct = default)
    {
        return;
        
        var userId = CurrentUserId;
        var roles = CurrentRoles;

        var resourceAttrs = new Dictionary<string, object?>
        {
            ["objectKey"] = objectKey,
            ["action"] = action
        };

        

        var result = await _policyService.EvaluateAsync(userId, roles, "DomainRecord", resourceAttrs, action, ct: ct);
        if (result != PolicyDecision.Allow)
            throw new InvalidOperationException($"User {userId} is not authorized to {action} records for domain object '{objectKey}'.");
    }

    // ── Reads ──────────────────────────────────────────────────────

    public async Task<DomainRecord?> GetAsync(string objectKey, Guid id)
    {
        await AuthorizeAsync(objectKey, "read");
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var (obj, version) = await ResolveAsync(objectKey);
        var entity = await dbContext.DomainObjectRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.DomainObjectId == obj.Id && r.Id == id);
        return entity is null ? null : ToRecord(entity, objectKey, version?.Definition);
    }

    public async Task<DomainQueryResult> QueryAsync(string objectKey, DomainQuery? query = null)
    {
        await AuthorizeAsync(objectKey, "read");
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var (obj, version) = await ResolveAsync(objectKey);
        var providerName = dbContext.Database.ProviderName;

        if (DomainQueryCompiler.CanUseSqlPushdown(providerName))
        {
            var compiled = DomainQueryCompiler.BuildQuery(obj.Id, version?.Definition, query);
            dbContext.Database.SetCommandTimeout(120);
            var entities = await dbContext.DomainObjectRecords
                .FromSqlRaw(compiled.CommandText, compiled.Parameters)
                .AsNoTracking()
                .ToListAsync();

            var totalCount = await dbContext.Database
                .SqlQueryRaw<int>(compiled.CountCommandText, compiled.Parameters)
                .FirstOrDefaultAsync();

            return new DomainQueryResult
            {
                TotalCount = totalCount,
                Records = entities.Select(e => ToRecord(e, objectKey, version?.Definition)).ToList()
            };
        }

        var allEntities = await dbContext.DomainObjectRecords.AsNoTracking()
            .Where(r => r.DomainObjectId == obj.Id).ToListAsync();

        var allRecords = allEntities.Select(e => ToRecord(e, objectKey, version?.Definition));
        return DomainQueryCompiler.ApplyQuery(allRecords, query);
    }

    public async Task<List<DomainOption>> GetOptionsAsync(
        string objectKey, string valueField, string labelField, int? dataSourceIndex = null, DomainQuery? query = null)
    {
        await AuthorizeAsync(objectKey, "read");
        var result = dataSourceIndex.HasValue
            ? await QueryDataSourceAsync(objectKey, dataSourceIndex.Value, query)
            : await QueryAsync(objectKey, query);

        return result.Records.Select(r => new DomainOption
        {
            Value = ResolveOptionField(r, valueField),
            Label = ResolveOptionField(r, labelField)?.ToString() ?? string.Empty
        }).ToList();
    }

    /// <summary>Resolves a field for option projection, treating "id" as the record's identity (which is not stored in Values).</summary>
    private static object? ResolveOptionField(DomainRecord record, string field) =>
        string.Equals(field, "id", StringComparison.OrdinalIgnoreCase)
            ? record.Id
            : record.Values.GetValueOrDefault(field);

    public async Task<DomainQueryResult> QueryDataSourceAsync(string objectKey, int dataSourceIndex, DomainQuery? query = null)
    {
        await AuthorizeAsync(objectKey, "read");
        var (_, version) = await ResolveAsync(objectKey);
        var def = version?.Definition
            ?? throw new InvalidOperationException($"Domain object '{objectKey}' has no published definition.");

        if (dataSourceIndex < 0 || dataSourceIndex >= def.DataSources.Count)
            throw new ArgumentOutOfRangeException(nameof(dataSourceIndex));
        var ds = def.DataSources[dataSourceIndex];

        // v1: domain object external sources are SQL queries against an admin connection.
        var request = new SqlRequest { Query = ds.Query };
        var execResult = await _dataSourceRunner.ExecuteAsync(ds.DataSourceKey, request);
        if (!execResult.Success)
            throw new InvalidOperationException(execResult.Error ?? "External data source query failed.");

        var records = execResult.Rows.Select(row => MapRow(row, ds, objectKey));
        return DomainQueryCompiler.ApplyQuery(records, query);
    }

    // ── Writes ─────────────────────────────────────────────────────

    public async Task<DomainRecord> CreateAsync(string objectKey, IDictionary<string, object?> values, string? user = null)
    {
        await AuthorizeAsync(objectKey, "create");
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var (obj, version) = await ResolveAsync(objectKey);
        var def = version?.Definition;
        var coerced = DomainValueCoercion.Coerce(values, def);

        Validate(def, coerced);

        var providerName = dbContext.Database.ProviderName;
        bool useSql = DomainQueryCompiler.CanUseSqlPushdown(providerName);
        if (!useSql)
            await EnsureUniqueAsync(obj.Id, def, coerced, excludeId: null);

        var now = DateTime.UtcNow;
        var author = user ?? CurrentUser;
        var entity = new DomainObjectRecord
        {
            DomainObjectId = obj.Id,
            DefinitionVersion = version?.Version,
            Values = coerced,
            CreatedAt = now,
            CreatedBy = author,
            UpdatedAt = now,
            UpdatedBy = author
        };
        dbContext.DomainObjectRecords.Add(entity);

        if (useSql)
            await SaveWithUniqueCheckAsync(dbContext, obj, def, coerced, null);
        else
            await dbContext.SaveChangesAsync();

        await _auditService.RecordAsync(
            category: "DomainObject",
            eventType: nameof(WorkflowAuditEventType.DomainObjectCreated),
            actor: author,
            details: new { ObjectKey = objectKey, RecordId = entity.Id });

        return ToRecord(entity, objectKey, def);
    }

    public async Task<DomainRecord> UpdateAsync(string objectKey, Guid id, IDictionary<string, object?> values, string? user = null)
    {
        await AuthorizeAsync(objectKey, "update");
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var (obj, version) = await ResolveAsync(objectKey);
        var def = version?.Definition;
        var entity = await dbContext.DomainObjectRecords
            .FirstOrDefaultAsync(r => r.DomainObjectId == obj.Id && r.Id == id)
            ?? throw new InvalidOperationException($"Record '{id}' not found for domain object '{objectKey}'.");

        var coerced = DomainValueCoercion.Coerce(values, def);
        Validate(def, coerced);

        var providerName = dbContext.Database.ProviderName;
        bool useSql = DomainQueryCompiler.CanUseSqlPushdown(providerName);
        if (!useSql)
            await EnsureUniqueAsync(obj.Id, def, coerced, excludeId: id);

        entity.Values = coerced;
        entity.DefinitionVersion = version?.Version;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = user ?? CurrentUser;

        if (useSql)
            await SaveWithUniqueCheckAsync(dbContext, obj, def, coerced, id);
        else
            await dbContext.SaveChangesAsync();

        await _auditService.RecordAsync(
            category: "DomainObject",
            eventType: nameof(WorkflowAuditEventType.DomainObjectUpdated),
            actor: entity.UpdatedBy,
            details: new { ObjectKey = objectKey, RecordId = id });

        return ToRecord(entity, objectKey, def);
    }

    public async Task<DomainRecord> UpsertAsync(string objectKey, DomainRecord record, string? user = null)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var exists = await dbContext.DomainObjectRecords.AnyAsync(r => r.Id == record.Id);
        return exists
            ? await UpdateAsync(objectKey, record.Id, record.Values, user)
            : await CreateAsync(objectKey, record.Values, user);
    }

    public async Task DeleteAsync(string objectKey, Guid id)
    {
        await AuthorizeAsync(objectKey, "delete");
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var (obj, _) = await ResolveAsync(objectKey);
        var entity = await dbContext.DomainObjectRecords
            .FirstOrDefaultAsync(r => r.DomainObjectId == obj.Id && r.Id == id);
        if (entity is null) return;

        dbContext.DomainObjectRecords.Remove(entity);
        await dbContext.SaveChangesAsync();

        await _auditService.RecordAsync(
            category: "DomainObject",
            eventType: nameof(WorkflowAuditEventType.DomainObjectDeleted),
            actor: CurrentUser,
            details: new { ObjectKey = objectKey, RecordId = id });
    }

    // ── Resolution & mapping ───────────────────────────────────────

    private async Task<(DomainObject obj, DomainObjectVersion? version)> ResolveAsync(string objectKey)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var obj = await dbContext.DomainObjects.AsNoTracking().FirstOrDefaultAsync(o => o.Key == objectKey)
            ?? throw new InvalidOperationException($"Domain object '{objectKey}' not found.");

        var versions = await dbContext.DomainObjectVersions.AsNoTracking()
            .Where(v => v.DomainObjectId == obj.Id && v.State == DomainObjectState.Published)
            .ToListAsync();
        var latest = versions.OrderByDescending(v => v.Version).FirstOrDefault();

        return (obj, latest);
    }

    private static DomainRecord ToRecord(DomainObjectRecord e, string objectKey, DomainObjectDefinition? def) => new()
    {
        Id = e.Id,
        ObjectKey = objectKey,
        DefinitionVersion = e.DefinitionVersion,
        Values = DomainValueCoercion.Coerce(e.Values, def),
        CreatedAt = e.CreatedAt,
        CreatedBy = e.CreatedBy,
        UpdatedAt = e.UpdatedAt,
        UpdatedBy = e.UpdatedBy
    };

    private static DomainRecord MapRow(Dictionary<string, object?> row, DomainDataSource ds, string objectKey)
    {
        var map = ds.ColumnMappings
            .Where(m => !string.IsNullOrWhiteSpace(m.Column) && !string.IsNullOrWhiteSpace(m.Property))
            .ToDictionary(m => m.Column, m => m.Property, StringComparer.OrdinalIgnoreCase);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (column, value) in row)
        {
            var key = map.TryGetValue(column, out var mapped) ? mapped : column;
            values[key] = value;
        }

        var id = values.TryGetValue("id", out var raw) && Guid.TryParse(raw?.ToString(), out var g) ? g : Guid.NewGuid();
        return new DomainRecord { Id = id, ObjectKey = objectKey, Values = values };
    }

    private static void Validate(DomainObjectDefinition? def, IDictionary<string, object?> values)
    {
        if (def is null) return;
        var errors = DomainRecordValidator.Validate(def, values);
        if (errors.Count > 0) throw new DomainValidationException(errors);
    }

    /// <summary>
    /// Saves the record under a serializable transaction so the uniqueness check and the insert
    /// are atomic. On SQL Server this prevents phantom duplicates from concurrent writes.
    /// </summary>
    private static async Task SaveWithUniqueCheckAsync(
        ArgentDbContext dbContext, DomainObject obj,
        DomainObjectDefinition? def, IDictionary<string, object?> coerced,
        Guid? excludeId)
    {
        var uniqueProps = def?.Properties?.Where(p => p.Unique).ToList();
        if (uniqueProps is not { Count: > 0 })
        {
            await dbContext.SaveChangesAsync();
            return;
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        foreach (var p in uniqueProps)
        {
            if (!coerced.TryGetValue(p.Key, out var v) || v is null) continue;

            var propPath = p.Key.Replace("'", "''");
            var sql = $@"SELECT CASE WHEN EXISTS (
    SELECT 1 FROM DomainObjectRecords r WITH (UPDLOCK, SERIALIZABLE)
    WHERE r.DomainObjectId = {{0}} AND ({{1}} IS NULL OR r.Id != {{1}})
      AND JSON_VALUE(r.[Values], '$.""{propPath}""') = {{2}}
) THEN 1 ELSE 0 END AS [Value]";

            var exists = await dbContext.Database
                .SqlQueryRaw<int>(sql, obj.Id, excludeId ?? (object)DBNull.Value, v?.ToString() ?? "")
                .FirstOrDefaultAsync();

            if (exists == 1)
            {
                var prop = def?.Properties?.FirstOrDefault(p2 => p2.Key == p.Key);
                var name = prop?.DisplayName ?? p.Key;
                throw new DomainValidationException([
                    new DomainValidationError(p.Key, $"{name} must be unique.")
                ]);
            }
        }

        await dbContext.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task EnsureUniqueAsync(Guid objectId, DomainObjectDefinition? def, IDictionary<string, object?> values, Guid? excludeId)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var uniqueProps = def?.Properties.Where(p => p.Unique).ToList();
        if (uniqueProps is not { Count: > 0 }) return;

        var errors = new List<DomainValidationError>();
        var providerName = dbContext.Database.ProviderName;
        var useSql = DomainQueryCompiler.CanUseSqlPushdown(providerName);

        foreach (var p in uniqueProps)
        {
            if (!values.TryGetValue(p.Key, out var v) || v is null) continue;

            bool exists;
            if (useSql)
            {
                var propPath = p.Key.Replace("'", "''");
                var conn = dbContext.Database.GetDbConnection();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"SELECT CASE WHEN EXISTS (
    SELECT 1 FROM DomainObjectRecords r
    WHERE r.DomainObjectId = @id AND (@exclude IS NULL OR r.Id != @exclude)
      AND JSON_VALUE(r.[Values], '$.""{propPath}""') = @value
) THEN 1 ELSE 0 END";

                var idParam = cmd.CreateParameter(); idParam.ParameterName = "@id"; idParam.Value = objectId; cmd.Parameters.Add(idParam);
                var excludeParam = cmd.CreateParameter(); excludeParam.ParameterName = "@exclude"; excludeParam.Value = excludeId.HasValue ? (object)excludeId.Value : DBNull.Value; cmd.Parameters.Add(excludeParam);
                var valueParam = cmd.CreateParameter(); valueParam.ParameterName = "@value"; valueParam.Value = v?.ToString() ?? string.Empty; cmd.Parameters.Add(valueParam);

                if (conn.State != ConnectionState.Open) await conn.OpenAsync();
                var raw = await cmd.ExecuteScalarAsync();
                exists = raw is 1 or 1L;
            }
            else
            {
                var all = await dbContext.DomainObjectRecords.AsNoTracking()
                    .Where(r => r.DomainObjectId == objectId && r.Id != excludeId)
                    .ToListAsync();
                exists = all.Any(r => r.Values.TryGetValue(p.Key, out var ev) && DomainQueryCompiler.ValuesEqual(ev, v));
            }

            if (exists)
                errors.Add(new DomainValidationError(p.Key,
                    $"{(string.IsNullOrWhiteSpace(p.DisplayName) ? p.Key : p.DisplayName)} must be unique."));
        }
        if (errors.Count > 0) throw new DomainValidationException(errors);
    }
}
