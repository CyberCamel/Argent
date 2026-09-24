using System.Text.Json;

namespace Argent.Core.Forms.Protocol.V2;

public static class FormExpressionEvaluator
{
    public static bool Evaluate(FormExpression expression, IReadOnlyDictionary<string, JsonElement> values) =>
        expression.Operator switch
        {
            "and" => RequiredArguments(expression).All(item => Evaluate(item, values)),
            "or" => RequiredArguments(expression).Any(item => Evaluate(item, values)),
            "not" => !Evaluate(expression.Argument ?? throw Invalid(expression, "argument"), values),
            "equals" => Equal(expression, values),
            "notEquals" => !Equal(expression, values),
            "greaterThan" => Order(expression, values) > 0,
            "greaterThanOrEqual" => Order(expression, values) >= 0,
            "lessThan" => Order(expression, values) < 0,
            "lessThanOrEqual" => Order(expression, values) <= 0,
            "contains" => TextOperation(expression, values, static (left, right) => left.Contains(right, StringComparison.Ordinal)),
            "startsWith" => TextOperation(expression, values, static (left, right) => left.StartsWith(right, StringComparison.Ordinal)),
            "endsWith" => TextOperation(expression, values, static (left, right) => left.EndsWith(right, StringComparison.Ordinal)),
            "isEmpty" => IsEmpty(Resolve(expression.Operand ?? throw Invalid(expression, "operand"), values)),
            "isNotEmpty" => !IsEmpty(Resolve(expression.Operand ?? throw Invalid(expression, "operand"), values)),
            _ => throw new FormProtocolException($"Unknown form expression operator '{expression.Operator}'.")
        };

    public static IReadOnlySet<string> Dependencies(FormExpression expression)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        CollectDependencies(expression, fields);
        return fields;
    }

    private static void CollectDependencies(FormExpression expression, HashSet<string> fields)
    {
        Add(expression.Left, fields);
        Add(expression.Right, fields);
        Add(expression.Operand, fields);
        if (expression.Argument is not null) CollectDependencies(expression.Argument, fields);
        if (expression.Arguments is not null)
            foreach (var argument in expression.Arguments) CollectDependencies(argument, fields);
    }

    private static void Add(FormOperand? operand, HashSet<string> fields)
    {
        if (!string.IsNullOrWhiteSpace(operand?.Field)) fields.Add(operand.Field);
    }

    private static IReadOnlyList<FormExpression> RequiredArguments(FormExpression expression)
    {
        if (expression.Arguments is not { Count: > 0 }) throw Invalid(expression, "non-empty arguments");
        return expression.Arguments;
    }

    private static bool Equal(FormExpression expression, IReadOnlyDictionary<string, JsonElement> values)
    {
        var left = Resolve(expression.Left ?? throw Invalid(expression, "left"), values);
        var right = Resolve(expression.Right ?? throw Invalid(expression, "right"), values);

        if (IsNullish(left) || IsNullish(right))
            return IsNullish(left) == IsNullish(right);

        if (!SamePrimitiveKind(left.ValueKind, right.ValueKind))
            throw new FormProtocolException($"Operator '{expression.Operator}' cannot compare {left.ValueKind} with {right.ValueKind}.");

        return left.ValueKind switch
        {
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => left.GetDecimal() == right.GetDecimal(),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            _ => throw new FormProtocolException($"Operator '{expression.Operator}' does not support {left.ValueKind} operands.")
        };
    }

    private static bool SamePrimitiveKind(JsonValueKind left, JsonValueKind right) =>
        left == right || (left is JsonValueKind.True or JsonValueKind.False && right is JsonValueKind.True or JsonValueKind.False);

    private static int Order(FormExpression expression, IReadOnlyDictionary<string, JsonElement> values)
    {
        var left = Resolve(expression.Left ?? throw Invalid(expression, "left"), values);
        var right = Resolve(expression.Right ?? throw Invalid(expression, "right"), values);
        if (IsNullish(left) || IsNullish(right))
            throw new FormProtocolException($"Operator '{expression.Operator}' does not order null or missing values.");
        if (left.ValueKind != right.ValueKind)
            throw new FormProtocolException($"Operator '{expression.Operator}' cannot compare {left.ValueKind} with {right.ValueKind}.");
        return left.ValueKind switch
        {
            JsonValueKind.String => string.Compare(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => left.GetDecimal().CompareTo(right.GetDecimal()),
            _ => throw new FormProtocolException($"Operator '{expression.Operator}' does not order {left.ValueKind} operands.")
        };
    }

    private static bool TextOperation(
        FormExpression expression,
        IReadOnlyDictionary<string, JsonElement> values,
        Func<string, string, bool> operation)
    {
        var left = Resolve(expression.Left ?? throw Invalid(expression, "left"), values);
        var right = Resolve(expression.Right ?? throw Invalid(expression, "right"), values);
        if (left.ValueKind != JsonValueKind.String || right.ValueKind != JsonValueKind.String)
            throw new FormProtocolException($"Operator '{expression.Operator}' requires string operands.");
        return operation(left.GetString()!, right.GetString()!);
    }

    private static JsonElement Resolve(FormOperand operand, IReadOnlyDictionary<string, JsonElement> values)
    {
        if (!string.IsNullOrWhiteSpace(operand.Field))
            return values.TryGetValue(operand.Field, out var value) ? value : default;
        if (operand.Value.ValueKind != JsonValueKind.Undefined) return operand.Value;
        throw new FormProtocolException("A form operand must contain exactly one of 'field' or 'value'.");
    }

    private static bool IsEmpty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => true,
        JsonValueKind.String => string.IsNullOrEmpty(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        _ => false
    };

    private static bool IsNullish(JsonElement value) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;

    private static FormProtocolException Invalid(FormExpression expression, string member) =>
        new($"Operator '{expression.Operator}' requires {member}.");
}
