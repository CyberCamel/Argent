using Argent.Models.Forms.Components.Configuration;

namespace Argent.Runtime.Workflows;

public static class ConditionCompiler
{
    public static string Compile(Condition condition) => condition switch
    {
        ExpressionCondition e              => string.IsNullOrWhiteSpace(e.Expression) ? "false" : e.Expression,
        CompareCondition c                 => CompileCompare(c),
        AndCondition { All.Count: 0 }     => "false",
        AndCondition a                     => "(" + string.Join(" And ", a.All.Select(Compile)) + ")",
        OrCondition  { Any.Count: 0 }     => "false",
        OrCondition o                      => "(" + string.Join(" Or ",  o.Any.Select(Compile)) + ")",
        NotCondition n                     => "Not (" + Compile(n.Not) + ")",
        _                                  => "false"
    };

    private static string CompileCompare(CompareCondition c)
    {
        var field = c.Field ?? "''";
        var raw   = c.Value?.ToString() ?? "";
        var value = IsLiteral(raw) ? raw : $"'{raw.Replace("'", "\\'")}'";

        return c.Operator switch
        {
            "==" or "!=" or ">" or "<" or ">=" or "<=" => $"{field} {c.Operator} {value}",
            "isEmpty"    => $"({field} == null Or {field} == '')",
            "isNotEmpty" => $"({field} != null And {field} != '')",
            _            => $"{field} == {value}"
        };
    }

    private static bool IsLiteral(string v) =>
        v is "true" or "false" or "null" || double.TryParse(v, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out _);
}
