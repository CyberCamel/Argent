using System.Text.Json;

namespace Argent.Core.Forms.Protocol.V2;

public static class FormViewModeProjector
{
    public static FormDefinition Apply(FormDefinition definition, string? viewMode)
    {
        var copy = JsonSerializer.Deserialize<FormDefinition>(JsonSerializer.Serialize(definition))!;
        if (!string.IsNullOrWhiteSpace(viewMode) && !copy.ViewModes.Contains(viewMode, StringComparer.Ordinal))
            throw new InvalidOperationException($"View mode '{viewMode}' is not defined on this form.");
        ApplyTo(copy.Components, viewMode);
        foreach (var binding in copy.Objects) ResolveContext(binding.When, viewMode);
        return copy;
    }

    private static void ApplyTo(IEnumerable<FormComponent> components, string? viewMode)
    {
        foreach (var component in components)
        {
            if (component is FormLayout layout)
            {
                ResolveContext(layout.VisibleWhen, viewMode);
                ApplyTo(layout.Children, viewMode);
            }
            else if (component is FormField field)
            {
                ResolveContext(field.VisibleWhen, viewMode);
                ResolveContext(field.RequiredWhen, viewMode);
                ResolveContext(field.DisabledWhen, viewMode);
                ResolveContext(field.ReadOnlyWhen, viewMode);
                foreach (var validator in field.Validators) ResolveContext(validator.When, viewMode);
                if (viewMode is not null && field.ModeOverrides.TryGetValue(viewMode, out var mode))
                {
                    field.Hidden = mode.Hidden;
                    if (mode.Required.HasValue)
                    {
                        field.Required = mode.Required.Value;
                        field.RequiredWhen = null;
                    }
                }
            }
        }
    }

    private static void ResolveContext(FormExpression? expression, string? viewMode)
    {
        if (expression is null) return;
        ResolveOperand(expression.Left, viewMode);
        ResolveOperand(expression.Right, viewMode);
        ResolveOperand(expression.Operand, viewMode);
        ResolveContext(expression.Argument, viewMode);
        if (expression.Arguments is not null)
            foreach (var argument in expression.Arguments) ResolveContext(argument, viewMode);
    }

    private static void ResolveOperand(FormOperand? operand, string? viewMode)
    {
        if (operand?.Context != "viewMode") return;
        operand.Context = null;
        operand.Value = JsonSerializer.SerializeToElement(viewMode ?? string.Empty);
    }
}
