using System.Data.Common;
using System.Text;
using Argent.Core.DomainObjects;
using Argent.Core.DomainObjects.Querying;
using Microsoft.EntityFrameworkCore;

namespace Argent.Runtime.DomainObjects;

/// <summary>
/// Translates <see cref="DomainQuery"/> into SQL pushdown via <c>JSON_VALUE</c> on the
/// <c>Values</c> column when running on SQL Server. Falls back to in-memory evaluation
/// (the <see cref="ApplyQuery"/> delegate) for SQLite / InMemory test providers.
/// </summary>
internal static class DomainQueryCompiler
{
    public static bool CanUseSqlPushdown(string? providerName) =>
        providerName is "Microsoft.EntityFrameworkCore.SqlServer";

    /// <summary>
    /// Builds a <see cref="FormattableString"/> that can be used with
    /// <c>FromSqlInterpolated</c> / <c>FromSqlRaw</c> to produce the full query.
    /// </summary>
    public static SqlQuery BuildQuery(
        Guid domainObjectId,
        DomainObjectDefinition? schema,
        DomainQuery? query)
    {
        var sql = new StringBuilder();
        var parameters = new List<object?>();
        var paramIndex = 0;

        // ── Base WHERE ──
        sql.Append("SELECT r.Id, r.DomainObjectId, r.[Values], r.DefinitionVersion, r.CreatedAt, r.CreatedBy, r.UpdatedAt, r.UpdatedBy FROM DomainObjectRecords r WHERE r.DomainObjectId = {");
        sql.Append(paramIndex);
        sql.Append('}');
        parameters.Add(domainObjectId);
        paramIndex++;

        // ── Filter ──
        if (query?.Filter is { } filter)
            AppendFilter(sql, parameters, ref paramIndex, "r", schema, filter);

        // ── Count (always before ORDER BY / OFFSET-FETCH) ──
        var countSql = new StringBuilder();
        countSql.Append("SELECT COUNT(*) AS [Value] FROM DomainObjectRecords r WHERE r.DomainObjectId = {0}");
        if (query?.Filter is { } filterForCount)
        {
            var countParams = new List<object?> { domainObjectId };
            var ci = 1;
            AppendFilter(countSql, countParams, ref ci, "r", schema, filterForCount);
        }

        // ── Sort (required for OFFSET/FETCH in SQL Server) ──
        var hasSkip = query?.Skip is > 0;
        var hasTake = query?.Take is >= 0;
        bool hasPaging = hasSkip || hasTake;
        if (query?.Sort is { Count: > 0 } sorts && sorts.Count > 0)
        {
            sql.Append(" ORDER BY");
            AppendSort(sql, schema, sorts);
        }
        else if (hasPaging)
        {
            sql.Append(" ORDER BY (SELECT NULL)");
        }

        // ── Paging ──
        if (query?.Skip is int skip and > 0)
        {
            sql.Append(" OFFSET {");
            sql.Append(paramIndex);
            sql.Append("} ROWS");
            parameters.Add(skip);
            paramIndex++;
        }

        if (query?.Take is int take and >= 0)
        {
            if (!hasSkip)
                sql.Append(" OFFSET 0 ROWS");
            sql.Append(" FETCH NEXT {");
            sql.Append(paramIndex);
            sql.Append("} ROWS ONLY");
            parameters.Add(take);
            paramIndex++;
        }

        return new SqlQuery(
            sql.ToString(),
            parameters.ToArray(),
            countSql.ToString());
    }

    private static void AppendFilter(
        StringBuilder sql,
        List<object?> parameters,
        ref int paramIndex,
        string alias,
        DomainObjectDefinition? schema,
        DomainFilter filter)
    {
        var clauses = new List<string>();

        foreach (var condition in filter.Conditions)
        {
            var clause = BuildConditionClause(parameters, ref paramIndex, alias, schema, condition);
            if (clause != null)
                clauses.Add(clause);
        }

        foreach (var group in filter.Groups)
        {
            var childSql = new StringBuilder();
            var childParams = new List<object?>();
            var childIndex = 0;
            AppendFilter(childSql, childParams, ref childIndex, alias, schema, group);
            if (childSql.Length > 0)
            {
                clauses.Add('(' + childSql.ToString() + ')');
                parameters.AddRange(childParams);
                paramIndex += childIndex;
            }
        }

        if (clauses.Count == 0) return;

        if (sql.Length > 0)
            sql.Append(" AND ");

        var logic = filter.Logic == DomainFilterLogic.And ? " AND " : " OR ";
        if (clauses.Count == 1)
            sql.Append(clauses[0]);
        else
            sql.Append('(').Append(string.Join(logic, clauses)).Append(')');
    }

    private static string? BuildConditionClause(
        List<object?> parameters,
        ref int paramIndex,
        string alias,
        DomainObjectDefinition? schema,
        DomainFilterCondition condition)
    {
        if (string.IsNullOrWhiteSpace(condition.Property))
            return null;

        var propPath = condition.Property.Replace("'", "''");
        var valueRef = $"{{{paramIndex}}}";
        var jsonRef = $"JSON_VALUE({alias}.[Values], '$.{propPath}')";

        // Determine the SQL type for CAST when needed
        var propType = schema?.Properties
            .FirstOrDefault(p => string.Equals(p.Key, condition.Property, StringComparison.Ordinal))
            ?.Type;

        switch (condition.Operator)
        {
            case DomainFilterOperator.IsNull:
                return $"{jsonRef} IS NULL";

            case DomainFilterOperator.IsNotNull:
                return $"{jsonRef} IS NOT NULL";

            case DomainFilterOperator.Equals:
                parameters.Add(condition.Value);
                paramIndex++;
                return propType is DomainPropertyType.Number or DomainPropertyType.Date or DomainPropertyType.DateTime
                    ? $"CAST({jsonRef} AS NVARCHAR(4000)) = CAST({valueRef} AS NVARCHAR(4000))"
                    : $"{jsonRef} = {valueRef}";

            case DomainFilterOperator.NotEquals:
                parameters.Add(condition.Value);
                paramIndex++;
                return propType is DomainPropertyType.Number or DomainPropertyType.Date or DomainPropertyType.DateTime
                    ? $"(NOT {jsonRef} = {valueRef} OR {jsonRef} IS NULL)"
                    : $"({jsonRef} <> {valueRef} OR {jsonRef} IS NULL)";

            case DomainFilterOperator.GreaterThan:
                parameters.Add(condition.Value);
                paramIndex++;
                return CastedCompare(jsonRef, valueRef, propType, " > ");

            case DomainFilterOperator.GreaterThanOrEqual:
                parameters.Add(condition.Value);
                paramIndex++;
                return CastedCompare(jsonRef, valueRef, propType, " >= ");

            case DomainFilterOperator.LessThan:
                parameters.Add(condition.Value);
                paramIndex++;
                return CastedCompare(jsonRef, valueRef, propType, " < ");

            case DomainFilterOperator.LessThanOrEqual:
                parameters.Add(condition.Value);
                paramIndex++;
                return CastedCompare(jsonRef, valueRef, propType, " <= ");

            case DomainFilterOperator.Contains:
                parameters.Add($"%{condition.Value}%");
                paramIndex++;
                return $"{jsonRef} LIKE {valueRef}";

            case DomainFilterOperator.StartsWith:
                parameters.Add($"{condition.Value}%");
                paramIndex++;
                return $"{jsonRef} LIKE {valueRef}";

            case DomainFilterOperator.EndsWith:
                parameters.Add($"%{condition.Value}");
                paramIndex++;
                return $"{jsonRef} LIKE {valueRef}";

            case DomainFilterOperator.In:
                return BuildInClause(parameters, ref paramIndex, jsonRef, condition.Value, negate: false);

            case DomainFilterOperator.NotIn:
                return BuildInClause(parameters, ref paramIndex, jsonRef, condition.Value, negate: true);

            default:
                return null;
        }
    }

    private static string CastedCompare(string jsonRef, string valueRef, DomainPropertyType? propType, string op)
    {
        if (propType is DomainPropertyType.Number)
            return $"TRY_CAST({jsonRef} AS FLOAT) {op} TRY_CAST({valueRef} AS FLOAT)";
        if (propType is DomainPropertyType.Date or DomainPropertyType.DateTime)
            return $"TRY_CAST({jsonRef} AS DATETIME2) {op} TRY_CAST({valueRef} AS DATETIME2)";
        return $"{jsonRef} {op} {valueRef}";
    }

    private static string BuildInClause(
        List<object?> parameters,
        ref int paramIndex,
        string jsonRef,
        object? values,
        bool negate)
    {
        if (values is not System.Collections.IEnumerable enumerable || values is string)
        {
            parameters.Add(values);
            paramIndex++;
            return negate
                ? $"({jsonRef} <> {{{paramIndex - 1}}} OR {jsonRef} IS NULL)"
                : $"{jsonRef} = {{{paramIndex - 1}}}";
        }

        var valueRefs = new List<string>();
        foreach (var v in enumerable)
        {
            parameters.Add(v);
            valueRefs.Add($"{{{paramIndex}}}");
            paramIndex++;
        }

        if (valueRefs.Count == 0)
            return negate ? "1=1" : "1=0";

        var joined = string.Join(",", valueRefs);
        var not = negate ? "NOT " : "";
        return $"{jsonRef} IS NOT NULL AND {not}{jsonRef} IN ({joined})";
    }

    private static void AppendSort(
        StringBuilder sql,
        DomainObjectDefinition? schema,
        List<DomainSort> sorts)
    {
        var first = true;
        foreach (var sort in sorts)
        {
            if (!first) sql.Append(',');
            first = false;

            var propPath = sort.Property.Replace("'", "''");
            sql.Append($" JSON_VALUE(r.[Values], '$.{propPath}')");
            if (sort.Descending)
                sql.Append(" DESC");
        }
    }

    public static bool ValuesEqual(object? a, object? b) => InMemoryValuesEqual(a, b);

public static Func<IEnumerable<DomainRecord>, DomainQuery?, DomainQueryResult> ApplyQuery { get; } =
        static (records, query) =>
        {
            IEnumerable<DomainRecord> seq = records;

            if (query?.Filter is { } filter)
                seq = seq.Where(r => InMemoryFilter(filter, r.Values));

            if (query?.Sort is { Count: > 0 } sorts)
                seq = InMemorySort(seq, sorts);

            var list = seq.ToList();
            var total = list.Count;

            if (query?.Skip is int skip and > 0) list = list.Skip(skip).ToList();
            if (query?.Take is int take and >= 0) list = list.Take(take).ToList();

            return new DomainQueryResult { TotalCount = total, Records = list };
        };

    private static bool InMemoryFilter(DomainFilter filter, IDictionary<string, object?> values)
    {
        var results = filter.Conditions.Select(c => InMemoryCondition(c, values))
            .Concat(filter.Groups.Select(g => InMemoryFilter(g, values)))
            .ToList();

        if (results.Count == 0) return true;
        return filter.Logic == DomainFilterLogic.And ? results.All(x => x) : results.Any(x => x);
    }

    private static bool InMemoryCondition(DomainFilterCondition condition, IDictionary<string, object?> values)
    {
        values.TryGetValue(condition.Property, out var actual);
        var a = Normalize(actual);
        var b = Normalize(condition.Value);

        return condition.Operator switch
        {
            DomainFilterOperator.IsNull => a is null,
            DomainFilterOperator.IsNotNull => a is not null,
            DomainFilterOperator.Equals => InMemoryValuesEqual(a, b),
            DomainFilterOperator.NotEquals => !InMemoryValuesEqual(a, b),
            DomainFilterOperator.GreaterThan => CompareValues(a, b) > 0,
            DomainFilterOperator.GreaterThanOrEqual => CompareValues(a, b) >= 0,
            DomainFilterOperator.LessThan => CompareValues(a, b) < 0,
            DomainFilterOperator.LessThanOrEqual => CompareValues(a, b) <= 0,
            DomainFilterOperator.Contains => Str(a).Contains(Str(b), StringComparison.OrdinalIgnoreCase),
            DomainFilterOperator.StartsWith => Str(a).StartsWith(Str(b), StringComparison.OrdinalIgnoreCase),
            DomainFilterOperator.EndsWith => Str(a).EndsWith(Str(b), StringComparison.OrdinalIgnoreCase),
            DomainFilterOperator.In => InEnumerable(b).Any(x => InMemoryValuesEqual(a, x)),
            DomainFilterOperator.NotIn => !InEnumerable(b).Any(x => InMemoryValuesEqual(a, x)),
            _ => false
        };
    }

    private static IEnumerable<DomainRecord> InMemorySort(IEnumerable<DomainRecord> seq, List<DomainSort> sorts)
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

    private static object? Normalize(object? v) => v switch
    {
        null => null,
        System.Text.Json.JsonElement e => e.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null,
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Number => e.TryGetDouble(out var d) ? d : null,
            System.Text.Json.JsonValueKind.String => e.GetString(),
            _ => e.GetRawText()
        },
        _ => v
    };

    private static bool InMemoryValuesEqual(object? a, object? b)
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
                d = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case string s when double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d):
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
            case string s when DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt):
                return true;
            default:
                dt = default;
                return false;
        }
    }

    private static string Str(object? v) => Normalize(v) switch
    {
        null => string.Empty,
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        var x => x.ToString() ?? string.Empty
    };

    private static IEnumerable<object?> InEnumerable(object? v)
    {
        if (v is System.Collections.IEnumerable e and not string)
            foreach (var x in e) yield return x;
        else
            yield return v;
    }
}

public readonly record struct SqlQuery(string CommandText, object?[] Parameters, string CountCommandText);
