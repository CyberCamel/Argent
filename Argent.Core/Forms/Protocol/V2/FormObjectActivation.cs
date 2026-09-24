using System.Text.Json;

namespace Argent.Core.Forms.Protocol.V2;

public static class FormObjectActivation
{
    public static IReadOnlySet<string> ActiveBindings(FormDefinition definition,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var fields = Fields(definition.Components).ToList();
        var active = definition.Objects.Where(binding => binding.IsPrimary ||
            (binding.When is not null
                ? FormExpressionEvaluator.Evaluate(binding.When, values)
                : fields.Any(field => field.ObjectBinding == binding.Key && IsPopulated(field, values))))
            .Select(binding => binding.Key).ToHashSet(StringComparer.Ordinal);

        // A child record can supply the only value of its parent record.
        bool changed;
        do
        {
            changed = false;
            foreach (var binding in definition.Objects.Where(binding => active.Contains(binding.Key)))
                if (binding.AssignToBinding is { } parent && active.Add(parent)) changed = true;
        } while (changed);

        return active;
    }

    private static bool IsPopulated(FormField field, IReadOnlyDictionary<string, JsonElement> values)
    {
        if (field.Hidden) return false;
        if (field.VisibleWhen is not null && !FormExpressionEvaluator.Evaluate(field.VisibleWhen, values)) return false;
        if (field.DisabledWhen is not null && FormExpressionEvaluator.Evaluate(field.DisabledWhen, values)) return false;
        if (!values.TryGetValue(field.Name, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False => false,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Array => value.GetArrayLength() > 0,
            _ => true
        };
    }

    private static IEnumerable<FormField> Fields(IEnumerable<FormComponent> components)
    {
        foreach (var component in components)
            if (component is FormField field) yield return field;
            else if (component is FormLayout layout)
                foreach (var child in Fields(layout.Children)) yield return child;
    }
}
