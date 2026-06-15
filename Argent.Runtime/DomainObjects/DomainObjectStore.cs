using System.Collections;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Argent.Contracts.Authorization;
using Argent.Contracts.DataSources;
using Argent.Contracts.DomainObjects;
using Argent.Infrastructure.Data;
using Argent.Models.Authorization;
using Argent.Models.DataSources;
using Argent.Models.DomainObjects;
using Argent.Models.DomainObjects.Querying;
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
    IPolicyDecisionService _policyService) : IDomainObjectStore
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
        var entities = await dbContext.DomainObjectRecords.AsNoTracking()
            .Where(r => r.DomainObjectId == obj.Id).ToListAsync();

        var records = entities.Select(e => ToRecord(e, objectKey, version?.Definition));
        return ApplyQuery(records, query);
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
        return ApplyQuery(records, query);
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
        await dbContext.SaveChangesAsync();

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
        await EnsureUniqueAsync(obj.Id, def, coerced, excludeId: id);

        entity.Values = coerced;
        entity.DefinitionVersion = version?.Version;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = user ?? CurrentUser;
        await dbContext.SaveChangesAsync();

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

    private async Task EnsureUniqueAsync(Guid objectId, DomainObjectDefinition? def, IDictionary<string, object?> values, Guid? excludeId)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var uniqueProps = def?.Properties.Where(p => p.Unique).ToList();
        if (uniqueProps is not { Count: > 0 }) return;

        // v1: load and compare in memory, matching the in-memory filtering stance.
        var all = await dbContext.DomainObjectRecords.AsNoTracking()
            .Where(r => r.DomainObjectId == objectId && r.Id != excludeId).ToListAsync();

        var errors = new List<DomainValidationError>();
        foreach (var p in uniqueProps)
        {
            if (!values.TryGetValue(p.Key, out var v) || v is null) continue;
            if (all.Any(r => r.Values.TryGetValue(p.Key, out var ev) && ValuesEqual(ev, v)))
                errors.Add(new DomainValidationError(p.Key,
                    $"{(string.IsNullOrWhiteSpace(p.DisplayName) ? p.Key : p.DisplayName)} must be unique."));
        }
        if (errors.Count > 0) throw new DomainValidationException(errors);
    }

    // ── In-memory query pipeline (filter → sort → page) ────────────

    private static DomainQueryResult ApplyQuery(IEnumerable<DomainRecord> records, DomainQuery? query)
    {
        IEnumerable<DomainRecord> seq = records;

        if (query?.Filter is { } filter)
            seq = seq.Where(r => EvaluateFilter(filter, r.Values));

        if (query?.Sort is { Count: > 0 } sorts)
            seq = ApplySort(seq, sorts);

        var list = seq.ToList();
        var total = list.Count;

        if (query?.Skip is int skip and > 0) list = list.Skip(skip).ToList();
        if (query?.Take is int take and >= 0) list = list.Take(take).ToList();

        return new DomainQueryResult { TotalCount = total, Records = list };
    }

    private static IEnumerable<DomainRecord> ApplySort(IEnumerable<DomainRecord> seq, List<DomainSort> sorts)
    {
        var comparer = Comparer<object?>.Create(CompareValues);
        IOrderedEnumerable<DomainRecord>? ordered = null;

        foreach (var sort in sorts)
        {
            Func<DomainRecord, object?> key = r => r.Values.GetValueOrDefault(sort.Property);
            ordered = ordered is null
                ? (sort.Descending ? seq.OrderByDescending(key, comparer) : seq.OrderBy(key, comparer))
                : (sort.Descending ? ordered.ThenByDescending(key, comparer) : ordered.ThenBy(key, comparer));
        }

        return ordered ?? seq;
    }

    private static bool EvaluateFilter(DomainFilter filter, IDictionary<string, object?> values)
    {
        var results = filter.Conditions.Select(c => EvaluateCondition(c, values))
            .Concat(filter.Groups.Select(g => EvaluateFilter(g, values)))
            .ToList();

        if (results.Count == 0) return true;
        return filter.Logic == DomainFilterLogic.And ? results.All(x => x) : results.Any(x => x);
    }

    private static bool EvaluateCondition(DomainFilterCondition condition, IDictionary<string, object?> values)
    {
        values.TryGetValue(condition.Property, out var actual);

        return condition.Operator switch
        {
            DomainFilterOperator.IsNull => Normalize(actual) is null,
            DomainFilterOperator.IsNotNull => Normalize(actual) is not null,
            DomainFilterOperator.Equals => ValuesEqual(actual, condition.Value),
            DomainFilterOperator.NotEquals => !ValuesEqual(actual, condition.Value),
            DomainFilterOperator.GreaterThan => CompareValues(actual, condition.Value) > 0,
            DomainFilterOperator.GreaterThanOrEqual => CompareValues(actual, condition.Value) >= 0,
            DomainFilterOperator.LessThan => CompareValues(actual, condition.Value) < 0,
            DomainFilterOperator.LessThanOrEqual => CompareValues(actual, condition.Value) <= 0,
            DomainFilterOperator.Contains => Str(actual).Contains(Str(condition.Value), StringComparison.OrdinalIgnoreCase),
            DomainFilterOperator.StartsWith => Str(actual).StartsWith(Str(condition.Value), StringComparison.OrdinalIgnoreCase),
            DomainFilterOperator.EndsWith => Str(actual).EndsWith(Str(condition.Value), StringComparison.OrdinalIgnoreCase),
            DomainFilterOperator.In => AsEnumerable(condition.Value).Any(x => ValuesEqual(actual, x)),
            DomainFilterOperator.NotIn => !AsEnumerable(condition.Value).Any(x => ValuesEqual(actual, x)),
            _ => false
        };
    }

    // ── Value comparison helpers ───────────────────────────────────

    private static object? Normalize(object? v) => v switch
    {
        null => null,
        JsonElement e => e.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => e.TryGetDouble(out var d) ? d : null,
            JsonValueKind.String => e.GetString(),
            _ => e.GetRawText()
        },
        _ => v
    };

    private static bool ValuesEqual(object? a, object? b)
    {
        a = Normalize(a);
        b = Normalize(b);
        if (a is null || b is null) return a is null && b is null;
        if (TryNum(a, out var na) && TryNum(b, out var nb)) return na.Equals(nb);
        if (a is bool ba && b is bool bb) return ba == bb;
        return string.Equals(Str(a), Str(b), StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareValues(object? a, object? b)
    {
        a = Normalize(a);
        b = Normalize(b);
        if (a is null || b is null) return a is null ? (b is null ? 0 : -1) : 1;
        if (TryNum(a, out var na) && TryNum(b, out var nb)) return na.CompareTo(nb);
        if (TryDate(a, out var da) && TryDate(b, out var db)) return da.CompareTo(db);
        return string.Compare(Str(a), Str(b), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNum(object? v, out double d)
    {
        switch (v)
        {
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return true;
            case string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d):
                return true;
            default:
                d = 0;
                return false;
        }
    }

    private static bool TryDate(object? v, out DateTime dt)
    {
        switch (v)
        {
            case DateTime d:
                dt = d;
                return true;
            case string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt):
                return true;
            default:
                dt = default;
                return false;
        }
    }

    private static string Str(object? v) => Normalize(v) switch
    {
        null => string.Empty,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        var x => x.ToString() ?? string.Empty
    };

    private static IEnumerable<object?> AsEnumerable(object? v)
    {
        if (v is IEnumerable e and not string)
            foreach (var x in e) yield return x;
        else
            yield return v;
    }
}
