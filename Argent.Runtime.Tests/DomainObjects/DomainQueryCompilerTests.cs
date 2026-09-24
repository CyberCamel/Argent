using Argent.Core.DomainObjects;
using Argent.Core.DomainObjects.Querying;
using Argent.Runtime.DomainObjects;
using Xunit;

namespace Argent.Runtime.Tests.DomainObjects;

public class DomainQueryCompilerTests
{
    // ── CanUseSqlPushdown ──────────────────────────────────────────

    [Fact]
    public void CanUseSqlPushdown_ReturnsTrue_ForSqlServer() =>
        Assert.True(DomainQueryCompiler.CanUseSqlPushdown("Microsoft.EntityFrameworkCore.SqlServer"));

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData("Microsoft.EntityFrameworkCore.InMemory")]
    [InlineData(null)]
    [InlineData("")]
    public void CanUseSqlPushdown_ReturnsFalse_ForOtherProviders(string? provider) =>
        Assert.False(DomainQueryCompiler.CanUseSqlPushdown(provider));

    // ── ApplyQuery (in-memory fallback) ────────────────────────────

    private static readonly List<DomainRecord> TestRecords =
    [
        new() { Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), ObjectKey = "test", Values = new() { ["name"] = "Alice", ["age"] = 30, ["active"] = true, ["salary"] = 75000.5, ["joinDate"] = "2024-01-15", ["department"] = "Engineering", ["score"] = 95.5 } },
        new() { Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), ObjectKey = "test", Values = new() { ["name"] = "Bob", ["age"] = 25, ["active"] = false, ["salary"] = 50000.0, ["joinDate"] = "2024-06-01", ["department"] = "Marketing", ["score"] = 80.0 } },
        new() { Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), ObjectKey = "test", Values = new() { ["name"] = "Charlie", ["age"] = 35, ["active"] = true, ["salary"] = 95000.75, ["joinDate"] = "2023-03-10", ["department"] = "Engineering", ["score"] = null } },
        new() { Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"), ObjectKey = "test", Values = new() { ["name"] = "Diana", ["age"] = 28, ["active"] = false, ["salary"] = null, ["joinDate"] = null, ["department"] = "Sales", ["score"] = 88.0 } },
        new() { Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005"), ObjectKey = "test", Values = new() { ["name"] = "Eve", ["age"] = 22, ["active"] = true, ["salary"] = 42000.0, ["joinDate"] = "2025-01-01", ["department"] = "Marketing", ["score"] = 92.0 } },
        new() { Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006"), ObjectKey = "test", Values = new() { ["name"] = "Frank", ["age"] = 40, ["active"] = false, ["salary"] = 120000.0, ["joinDate"] = "2022-11-20", ["department"] = "Engineering", ["score"] = 75.0 } },
    ];

    [Fact]
    public void ApplyQuery_EmptyQuery_ReturnsAllRecords()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, null);
        Assert.Equal(6, result.TotalCount);
        Assert.Equal(6, result.Records.Count);
    }

    [Fact]
    public void ApplyQuery_Equals_String()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.Equals, Value = "alice" }]
            }
        });
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Alice", result.Records[0].Values["name"]);
    }

    [Fact]
    public void ApplyQuery_Equals_Null()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "salary", Operator = DomainFilterOperator.Equals, Value = null }]
            }
        });
        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Diana", result.Records[0].Values["name"]);
    }

    [Fact]
    public void ApplyQuery_NotEquals_String()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "department", Operator = DomainFilterOperator.NotEquals, Value = "Engineering" }]
            }
        });
        Assert.Equal(3, result.TotalCount);
        Assert.DoesNotContain(result.Records, r => r.Values["department"] as string == "Engineering");
    }

    [Fact]
    public void ApplyQuery_GreaterThan_Number()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "age", Operator = DomainFilterOperator.GreaterThan, Value = 30 }]
            }
        });
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Records, r => r.Values["name"] as string == "Charlie");
        Assert.Contains(result.Records, r => r.Values["name"] as string == "Frank");
    }

    [Fact]
    public void ApplyQuery_GreaterThanOrEqual_Number()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "age", Operator = DomainFilterOperator.GreaterThanOrEqual, Value = 30 }]
            }
        });
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public void ApplyQuery_LessThan_Number()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "age", Operator = DomainFilterOperator.LessThan, Value = 28 }]
            }
        });
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Records, r => r.Values["name"] as string == "Bob");
        Assert.Contains(result.Records, r => r.Values["name"] as string == "Eve");
    }

    [Fact]
    public void ApplyQuery_LessThanOrEqual_Number()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "age", Operator = DomainFilterOperator.LessThanOrEqual, Value = 28 }]
            }
        });
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public void ApplyQuery_Contains_String()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.Contains, Value = "li" }]
            }
        });
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Records, r => r.Values["name"] as string == "Alice");
        Assert.Contains(result.Records, r => r.Values["name"] as string == "Charlie");
    }

    [Fact]
    public void ApplyQuery_StartsWith_String()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.StartsWith, Value = "A" }]
            }
        });
        Assert.Single(result.Records);
        Assert.Equal("Alice", result.Records[0].Values["name"]);
    }

    [Fact]
    public void ApplyQuery_EndsWith_String()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.EndsWith, Value = "e" }]
            }
        });
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public void ApplyQuery_In_String()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.In, Value = new[] { "Alice", "Bob", "Nobody" } }]
            }
        });
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public void ApplyQuery_NotIn_String()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.NotIn, Value = new[] { "Alice", "Bob" } }]
            }
        });
        Assert.Equal(4, result.TotalCount);
    }

    [Fact]
    public void ApplyQuery_IsNull()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "salary", Operator = DomainFilterOperator.IsNull }]
            }
        });
        Assert.Single(result.Records);
        Assert.Equal("Diana", result.Records[0].Values["name"]);
    }

    [Fact]
    public void ApplyQuery_IsNotNull()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "salary", Operator = DomainFilterOperator.IsNotNull }]
            }
        });
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public void ApplyQuery_Bool_Equals_True()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "active", Operator = DomainFilterOperator.Equals, Value = true }]
            }
        });
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public void ApplyQuery_Bool_Equals_False()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "active", Operator = DomainFilterOperator.Equals, Value = false }]
            }
        });
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public void ApplyQuery_Float_Equals()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "salary", Operator = DomainFilterOperator.Equals, Value = 50000.0 }]
            }
        });
        Assert.Single(result.Records);
        Assert.Equal("Bob", result.Records[0].Values["name"]);
    }

    [Fact]
    public void ApplyQuery_NotEquals_ExcludesExactMatch_IncludesNull()
    {
        // In-memory: !(null == 50000) is true, so null-value records are included
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "salary", Operator = DomainFilterOperator.NotEquals, Value = 50000.0 }]
            }
        });
        Assert.Equal(5, result.TotalCount);
        Assert.DoesNotContain(result.Records, r => r.Values["name"] as string == "Bob");
    }

    // ── Filter groups (AND / OR logic) ─────────────────────────────

    [Fact]
    public void ApplyQuery_AndGroup()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Logic = DomainFilterLogic.And,
                Conditions =
                [
                    new() { Property = "department", Operator = DomainFilterOperator.Equals, Value = "Engineering" },
                    new() { Property = "active", Operator = DomainFilterOperator.Equals, Value = true },
                ]
            }
        });
        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Records, r => r.Values["name"] as string == "Alice");
        Assert.Contains(result.Records, r => r.Values["name"] as string == "Charlie");
    }

    [Fact]
    public void ApplyQuery_OrGroup()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Logic = DomainFilterLogic.Or,
                Conditions =
                [
                    new() { Property = "department", Operator = DomainFilterOperator.Equals, Value = "Sales" },
                    new() { Property = "department", Operator = DomainFilterOperator.Equals, Value = "Marketing" },
                ]
            }
        });
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public void ApplyQuery_NestedGroups()
    {
        // (department = Engineering AND active = true) OR (department = Marketing)
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Logic = DomainFilterLogic.Or,
                Groups =
                [
                    new() { Logic = DomainFilterLogic.And, Conditions = [new() { Property = "department", Operator = DomainFilterOperator.Equals, Value = "Engineering" }, new() { Property = "active", Operator = DomainFilterOperator.Equals, Value = true }] },
                    new() { Conditions = [new() { Property = "department", Operator = DomainFilterOperator.Equals, Value = "Marketing" }] },
                ]
            }
        });
        Assert.Equal(4, result.TotalCount);
    }

    // ── Sorting ─────────────────────────────────────────────────────

    [Fact]
    public void ApplyQuery_Sort_Ascending()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Sort = [new() { Property = "age", Descending = false }]
        });
        Assert.Equal(6, result.Records.Count);
        Assert.Equal([22, 25, 28, 30, 35, 40],
            result.Records.Select(r => r.Values["age"]).ToList());
    }

    [Fact]
    public void ApplyQuery_Sort_Descending()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Sort = [new() { Property = "age", Descending = true }]
        });
        Assert.Equal([40, 35, 30, 28, 25, 22],
            result.Records.Select(r => r.Values["age"]).ToList());
    }

    [Fact]
    public void ApplyQuery_Sort_Multiple()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Sort =
            [
                new() { Property = "department", Descending = false },
                new() { Property = "age", Descending = true },
            ]
        });
        var names = result.Records.Select(r => (string)r.Values["name"]!).ToList();
        // Engineering (3) → Marketing (2) → Sales (1); within dept, age descending
        Assert.Equal("Frank", names[0]);   // Engineering, 40
        Assert.Equal("Charlie", names[1]); // Engineering, 35
        Assert.Equal("Alice", names[2]);   // Engineering, 30
        Assert.Equal("Bob", names[3]);     // Marketing, 25
        Assert.Equal("Eve", names[4]);     // Marketing, 22
        Assert.Equal("Diana", names[5]);   // Sales, 28
    }

    // ── Paging ──────────────────────────────────────────────────────

    [Fact]
    public void ApplyQuery_Skip()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Sort = [new() { Property = "age" }],
            Skip = 2,
        });
        Assert.Equal(6, result.TotalCount);
        Assert.Equal(4, result.Records.Count);
        Assert.Equal(28, result.Records[0].Values["age"]);
    }

    [Fact]
    public void ApplyQuery_Take()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Sort = [new() { Property = "age" }],
            Take = 2,
        });
        Assert.Equal(6, result.TotalCount);
        Assert.Equal(2, result.Records.Count);
        Assert.Equal(22, result.Records[0].Values["age"]);
    }

    [Fact]
    public void ApplyQuery_SkipAndTake()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Sort = [new() { Property = "age" }],
            Skip = 1,
            Take = 3,
        });
        Assert.Equal(6, result.TotalCount);
        Assert.Equal(3, result.Records.Count);
        Assert.Equal([25, 28, 30], result.Records.Select(r => r.Values["age"]).ToList());
    }

    // ── Empty / edge cases ──────────────────────────────────────────

    [Fact]
    public void ApplyQuery_NoMatchingRecords_ReturnsEmpty()
    {
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.Equals, Value = "Zoe" }]
            }
        });
        Assert.Empty(result.Records);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public void ApplyQuery_EmptyRecords_ReturnsEmpty()
    {
        var result = DomainQueryCompiler.ApplyQuery(new List<DomainRecord>(), new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.Equals, Value = "Alice" }]
            }
        });
        Assert.Empty(result.Records);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public void ApplyQuery_UnknownProperty_TreatedAsNull_NoMatch()
    {
        // A missing property key yields null from TryGetValue, which doesn't equal "anything"
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "nonexistent", Operator = DomainFilterOperator.Equals, Value = "anything" }]
            }
        });
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public void ApplyQuery_DoubleValue_Comparison_WithInt()
    {
        // Values stored as int (age=30) compared against double (30.0)
        var result = DomainQueryCompiler.ApplyQuery(TestRecords, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "age", Operator = DomainFilterOperator.GreaterThan, Value = 29.5 }]
            }
        });
        Assert.Equal(3, result.TotalCount);
    }

    // ── BuildQuery (SQL generation) ─────────────────────────────────

    [Fact]
    public void BuildQuery_GeneratesBaseWhere()
    {
        var id = Guid.NewGuid();
        var result = DomainQueryCompiler.BuildQuery(id, null, null);

        Assert.Contains("DomainObjectRecords r WHERE r.DomainObjectId = {0}", result.CommandText);
        Assert.Equal(id, result.Parameters[0]);
    }

    [Fact]
    public void BuildQuery_WithoutFilter_HasCountQuery()
    {
        var id = Guid.NewGuid();
        var result = DomainQueryCompiler.BuildQuery(id, null, null);

        Assert.Equal("SELECT COUNT(*) AS [Value] FROM DomainObjectRecords r WHERE r.DomainObjectId = {0}", result.CountCommandText);
    }

    [Fact]
    public void BuildQuery_Equals_StringCondition()
    {
        var id = Guid.NewGuid();
        var result = DomainQueryCompiler.BuildQuery(id, null, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.Equals, Value = "Alice" }]
            }
        });

        Assert.Contains("JSON_VALUE(r.[Values], '$.name') = {1}", result.CommandText);
        Assert.Contains("AND", result.CommandText);
        Assert.Equal(2, result.Parameters.Length);
        Assert.Equal("Alice", result.Parameters[1]);
    }

    [Fact]
    public void BuildQuery_IsNull()
    {
        var id = Guid.NewGuid();
        var result = DomainQueryCompiler.BuildQuery(id, null, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "salary", Operator = DomainFilterOperator.IsNull }]
            }
        });

        Assert.Contains("JSON_VALUE(r.[Values], '$.salary') IS NULL", result.CommandText);
    }

    [Fact]
    public void BuildQuery_LikeOperators_ContainWildcards()
    {
        var id = Guid.NewGuid();

        var contains = DomainQueryCompiler.BuildQuery(id, null, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.Contains, Value = "li" }]
            }
        });
        Assert.Contains("LIKE {1}", contains.CommandText);
        Assert.Equal("%li%", contains.Parameters[1]);

        var starts = DomainQueryCompiler.BuildQuery(id, null, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.StartsWith, Value = "A" }]
            }
        });
        Assert.Equal("A%", starts.Parameters[1]);

        var ends = DomainQueryCompiler.BuildQuery(id, null, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.EndsWith, Value = "e" }]
            }
        });
        Assert.Equal("%e", ends.Parameters[1]);
    }

    [Fact]
    public void BuildQuery_Sort_GeneratesOrderBy()
    {
        var id = Guid.NewGuid();
        var result = DomainQueryCompiler.BuildQuery(id, null, new DomainQuery
        {
            Sort = [new() { Property = "age", Descending = true }]
        });

        Assert.Contains("ORDER BY", result.CommandText);
        Assert.Contains("JSON_VALUE(r.[Values], '$.age') DESC", result.CommandText);
    }

    [Fact]
    public void BuildQuery_Paging_GeneratesOffsetFetch()
    {
        var id = Guid.NewGuid();
        var result = DomainQueryCompiler.BuildQuery(id, null, new DomainQuery
        {
            Skip = 10,
            Take = 25,
        });

        Assert.Contains("OFFSET {1} ROWS", result.CommandText);
        Assert.Contains("FETCH NEXT {2} ROWS ONLY", result.CommandText);
        Assert.Equal(10, result.Parameters[1]);
        Assert.Equal(25, result.Parameters[2]);
    }

    [Fact]
    public void BuildQuery_FilterWithAndGroups()
    {
        var id = Guid.NewGuid();
        var result = DomainQueryCompiler.BuildQuery(id, null, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Logic = DomainFilterLogic.And,
                Conditions =
                [
                    new() { Property = "department", Operator = DomainFilterOperator.Equals, Value = "Engineering" },
                    new() { Property = "active", Operator = DomainFilterOperator.Equals, Value = true },
                ]
            }
        });

        Assert.Contains("AND", result.CommandText);
        Assert.Equal(3, result.Parameters.Length); // id + department + active
    }

    [Fact]
    public void BuildQuery_FilterWithCast_ForNumberProperty()
    {
        var id = Guid.NewGuid();
        var schema = new DomainObjectDefinition
        {
            Properties =
            [
                new() { Key = "age", Type = DomainPropertyType.Number },
            ]
        };
        var result = DomainQueryCompiler.BuildQuery(id, schema, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "age", Operator = DomainFilterOperator.GreaterThan, Value = 30 }]
            }
        });

        Assert.Contains("TRY_CAST", result.CommandText);
        Assert.Contains("AS FLOAT", result.CommandText);
    }

    [Fact]
    public void BuildQuery_InClause_GeneratesMultipleParams()
    {
        var id = Guid.NewGuid();
        var result = DomainQueryCompiler.BuildQuery(id, null, new DomainQuery
        {
            Filter = new DomainFilter
            {
                Conditions = [new() { Property = "name", Operator = DomainFilterOperator.In, Value = new[] { "Alice", "Bob", "Charlie" } }]
            }
        });

        Assert.Contains("IN ({1},{2},{3})", result.CommandText);
        Assert.Equal(4, result.Parameters.Length); // id + 3 values
        Assert.Equal("Alice", result.Parameters[1]);
        Assert.Equal("Bob", result.Parameters[2]);
        Assert.Equal("Charlie", result.Parameters[3]);
    }

    // ── ValuesEqual ─────────────────────────────────────────────────

    [Fact]
    public void ValuesEqual_SameString_CaseInsensitive()
    {
        Assert.True(DomainQueryCompiler.ValuesEqual("Hello", "hello"));
    }

    [Fact]
    public void ValuesEqual_StringAndInt_EqualViaNumericCoercion()
    {
        // Values come from JSON deserialization where "42" and 42 are equivalent
        Assert.True(DomainQueryCompiler.ValuesEqual("42", 42));
    }

    [Fact]
    public void ValuesEqual_IntAndDouble_Equal()
    {
        Assert.True(DomainQueryCompiler.ValuesEqual(42, 42.0));
    }

    [Fact]
    public void ValuesEqual_BothNull_Equal()
    {
        Assert.True(DomainQueryCompiler.ValuesEqual(null, null));
    }

    [Fact]
    public void ValuesEqual_OneNull_NotEqual()
    {
        Assert.False(DomainQueryCompiler.ValuesEqual("hello", null));
        Assert.False(DomainQueryCompiler.ValuesEqual(null, "hello"));
    }

    [Fact]
    public void ValuesEqual_Bool_Equal()
    {
        Assert.True(DomainQueryCompiler.ValuesEqual(true, true));
        Assert.True(DomainQueryCompiler.ValuesEqual(false, false));
    }

    [Fact]
    public void ValuesEqual_Bool_NotEqual()
    {
        Assert.False(DomainQueryCompiler.ValuesEqual(true, false));
    }
}
