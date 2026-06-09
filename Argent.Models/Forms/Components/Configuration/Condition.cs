using System.Text.Json.Serialization;

namespace Argent.Models.Forms.Components.Configuration;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AndCondition), "and")]
[JsonDerivedType(typeof(OrCondition), "or")]
[JsonDerivedType(typeof(NotCondition), "not")]
[JsonDerivedType(typeof(CompareCondition), "compare")]
[JsonDerivedType(typeof(RoleCondition), "role")]
[JsonDerivedType(typeof(ExpressionCondition), "expression")]
public abstract class Condition
{
    public string Type { get; set; } = "compare";
}

public class AndCondition : Condition
{
    public List<Condition> All { get; set; } = [];
    public AndCondition() => Type = "and";
}

public class OrCondition : Condition
{
    public List<Condition> Any { get; set; } = [];
    public OrCondition() => Type = "or";
}

public class NotCondition : Condition
{
    public Condition Not { get; set; } = new ExpressionCondition();
    public NotCondition() => Type = "not";
}

public class CompareCondition : Condition
{
    public string Field { get; set; } = "";
    public string Operator { get; set; } = "==";
    public object? Value { get; set; }
    public string? ValueField { get; set; }
    public CompareCondition() => Type = "compare";
}

public class RoleCondition : Condition
{
    public List<string> Roles { get; set; } = [];
    public List<string> NotRoles { get; set; } = [];
    public RoleCondition() => Type = "role";
}

public class ExpressionCondition : Condition
{
    public string Expression { get; set; } = "";
    public ExpressionCondition() => Type = "expression";
}

public static class CompareOperators
{
    public static readonly string[] All = 
    [
        "==", "!=", ">", "<", ">=", "<=",
        "contains", "startsWith", "endsWith",
        "in", "notIn", "isEmpty", "isNotEmpty"
    ];
}