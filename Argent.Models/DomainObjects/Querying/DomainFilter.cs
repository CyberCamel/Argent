namespace Argent.Models.DomainObjects.Querying;

public enum DomainFilterLogic
{
    And,
    Or
}

public enum DomainFilterOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains,
    StartsWith,
    EndsWith,
    In,
    NotIn,
    IsNull,
    IsNotNull
}

/// <summary>A single predicate against one property's value.</summary>
public class DomainFilterCondition
{
    public string Property { get; set; } = string.Empty;
    public DomainFilterOperator Operator { get; set; } = DomainFilterOperator.Equals;
    public object? Value { get; set; }
}

/// <summary>
/// A composable boolean group of conditions (and nested groups). A typed query tree that
/// both the form grid/dropdown builder and the workflow engine can construct, and that the
/// store can later translate to SQL (OPENJSON) instead of in-memory evaluation.
/// </summary>
public class DomainFilter
{
    public DomainFilterLogic Logic { get; set; } = DomainFilterLogic.And;
    public List<DomainFilterCondition> Conditions { get; set; } = [];
    public List<DomainFilter> Groups { get; set; } = [];
}
