namespace Argent.Core.Forms.Protocol.V2;

public sealed record FormDefinitionError(string Code, string Path, string Message);

public sealed class CompiledFormDefinition(
    FormDefinition definition,
    IReadOnlyDictionary<string, IReadOnlySet<string>> dependents)
{
    public FormDefinition Definition { get; } = definition;

    /// <summary>Source field name to component paths whose state depends on that field.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> Dependents { get; } = dependents;
}

public sealed record FormDefinitionCompilation(
    CompiledFormDefinition? Definition,
    IReadOnlyList<FormDefinitionError> Errors)
{
    public bool IsValid => Definition is not null && Errors.Count == 0;
}

public static class FormDefinitionCompiler
{
    private static readonly System.Text.RegularExpressions.Regex ProtocolIdPattern =
        new("^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex IdentifierPattern =
        new("^[A-Za-z][A-Za-z0-9_.-]{0,127}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly HashSet<string> FieldTypes =
    ["text", "integer", "decimal", "date", "timestamp", "boolean", "choice", "file"];

    private static readonly HashSet<string> LayoutTypes =
    ["section", "row", "column", "tabs", "accordion"];

    private static readonly HashSet<string> ValidatorTypes =
    ["required", "length", "range", "pattern", "email", "url", "compare"];

    public static FormDefinitionCompilation Compile(FormDefinition definition)
    {
        var errors = new List<FormDefinitionError>();
        if (definition.ProtocolVersion != "2.0")
            errors.Add(new("protocol.unsupported", "protocolVersion", $"Unsupported protocol version '{definition.ProtocolVersion}'."));
        ValidateProtocolId(definition.Id, "id", "form.id_invalid", errors);
        var viewModes = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < definition.ViewModes.Count; index++)
        {
            var mode = definition.ViewModes[index];
            ValidateIdentifier(mode, $"viewModes[{index}]", "form.view_mode_invalid", errors);
            if (!viewModes.Add(mode))
                errors.Add(new("form.view_mode_duplicate", $"viewModes[{index}]", "View mode keys must be unique."));
        }
        if (definition.Objects.Count == 0)
            ValidateExternalKey(definition.ObjectKey, "objectKey", "form.object_key_invalid", errors);
        else
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (definition.Objects.Count(binding => binding.IsPrimary) != 1)
                errors.Add(new("form.primary_object_required", "objects", "Exactly one primary object binding is required."));
            for (var index = 0; index < definition.Objects.Count; index++)
            {
                var binding = definition.Objects[index];
                ValidateIdentifier(binding.Key, $"objects[{index}].key", "object.binding_invalid", errors);
                ValidateExternalKey(binding.ObjectKey, $"objects[{index}].objectKey", "object.key_invalid", errors);
                if (!keys.Add(binding.Key))
                    errors.Add(new("object.binding_duplicate", $"objects[{index}].key", "Object binding keys must be unique."));
                if (binding.IsPrimary && binding.When is not null)
                    errors.Add(new("object.primary_conditional", $"objects[{index}].when", "The primary object cannot be conditional."));
                if ((binding.AssignToBinding is null) != (binding.AssignToProperty is null))
                    errors.Add(new("object.assignment_incomplete", $"objects[{index}]", "Both assignment target and property are required."));
                if (binding.AssignToBinding is not null && !definition.Objects.Any(target => target.Key == binding.AssignToBinding))
                    errors.Add(new("object.assignment_unknown", $"objects[{index}].assignToBinding", "Assignment target binding does not exist."));
                if (binding.AssignToProperty is not null)
                    ValidateIdentifier(binding.AssignToProperty, $"objects[{index}].assignToProperty", "object.assignment_property_invalid", errors);
            }
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var layoutIds = new HashSet<string>(StringComparer.Ordinal);
        var expressions = new List<(string Path, FormExpression Expression)>();
        var comparisons = new List<(string Path, string OtherField)>();
        Walk(definition.Components, "components", fields, layoutIds, expressions, comparisons, errors);
        foreach (var (binding, index) in definition.Objects.Select((item, index) => (item, index)))
            if (binding.When is not null)
            {
                expressions.Add(($"objects[{index}].when", binding.When));
                var ownFields = Fields(definition.Components).Where(field => field.ObjectBinding == binding.Key)
                    .Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
                if (FormExpressionEvaluator.Dependencies(binding.When).Any(ownFields.Contains))
                    errors.Add(new("object.condition_self_reference", $"objects[{index}].when",
                        "An object cannot be activated by one of its own fields."));
            }
        var boundProperties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in Fields(definition.Components))
        {
            foreach (var mode in field.ModeOverrides.Keys)
                if (!viewModes.Contains(mode))
                    errors.Add(new("field.view_mode_unknown", fields[field.Name] + ".modeOverrides",
                        $"Field '{field.Name}' refers to undefined view mode '{mode}'."));
            if (field.ObjectBinding is null) continue;
            if (!definition.Objects.Any(binding => binding.Key == field.ObjectBinding))
                errors.Add(new("field.binding_unknown", fields[field.Name] + ".objectBinding", $"Unknown object binding '{field.ObjectBinding}'."));
            if (field.PropertyKey is not null)
                ValidateIdentifier(field.PropertyKey, fields[field.Name] + ".propertyKey", "field.property_invalid", errors);
            if (!boundProperties.Add($"{field.ObjectBinding}\0{field.PropertyKey ?? field.Name}"))
                errors.Add(new("field.property_duplicate", fields[field.Name] + ".propertyKey",
                    "A domain property can be bound only once within an object binding."));
        }

        foreach (var (path, otherField) in comparisons)
            if (!fields.ContainsKey(otherField))
                errors.Add(new("validator.field_unknown", path, $"Compare validator references unknown field '{otherField}'."));

        var dependents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (path, expression) in expressions)
        {
            ValidateExpression(expression, path, errors);
            foreach (var dependency in FormExpressionEvaluator.Dependencies(expression))
            {
                if (!fields.ContainsKey(dependency))
                {
                    errors.Add(new("expression.field_unknown", path, $"Expression references unknown field '{dependency}'."));
                    continue;
                }
                if (!dependents.TryGetValue(dependency, out var targets))
                    dependents[dependency] = targets = new(StringComparer.Ordinal);
                targets.Add(ComponentPath(path));
            }
        }

        if (errors.Count > 0) return new(null, errors);
        var frozen = dependents.ToDictionary(
            item => item.Key,
            item => (IReadOnlySet<string>)item.Value,
            StringComparer.Ordinal);
        return new(new(definition, frozen), errors);
    }

    private static IEnumerable<FormField> Fields(IEnumerable<FormComponent> components)
    {
        foreach (var component in components)
            if (component is FormField field) yield return field;
            else if (component is FormLayout layout)
                foreach (var nestedField in Fields(layout.Children)) yield return nestedField;
    }

    private static void Walk(
        IReadOnlyList<FormComponent> components,
        string path,
        Dictionary<string, string> fields,
        HashSet<string> layoutIds,
        List<(string Path, FormExpression Expression)> expressions,
        List<(string Path, string OtherField)> comparisons,
        List<FormDefinitionError> errors)
    {
        for (var index = 0; index < components.Count; index++)
        {
            var componentPath = $"{path}[{index}]";
            switch (components[index])
            {
                case FormField field:
                    if (!FieldTypes.Contains(field.Type))
                        errors.Add(new("field.type_unknown", $"{componentPath}.type", $"Unknown field type '{field.Type}'."));
                    ValidateIdentifier(field.Name, $"{componentPath}.name", "field.name_invalid", errors);
                    if (!fields.TryAdd(field.Name, componentPath))
                        errors.Add(new("field.name_duplicate", $"{componentPath}.name", $"Field name '{field.Name}' is duplicated."));
                    if (string.IsNullOrWhiteSpace(field.Label))
                        errors.Add(new("field.label_required", $"{componentPath}.label", "Field label is required."));
                    AddExpressions(field, componentPath, expressions);
                    ValidateFieldConfiguration(field, componentPath, comparisons, errors);
                    break;
                case FormLayout layout:
                    if (!LayoutTypes.Contains(layout.Type))
                        errors.Add(new("layout.type_unknown", $"{componentPath}.type", $"Unknown layout type '{layout.Type}'."));
                    if (layout.Id is not null)
                    {
                        ValidateIdentifier(layout.Id, $"{componentPath}.id", "layout.id_invalid", errors);
                        if (!layoutIds.Add(layout.Id))
                            errors.Add(new("layout.id_duplicate", $"{componentPath}.id", $"Layout id '{layout.Id}' is duplicated."));
                    }
                    if (layout.VisibleWhen is not null) expressions.Add(($"{componentPath}.visibleWhen", layout.VisibleWhen));
                    Walk(layout.Children, $"{componentPath}.children", fields, layoutIds, expressions, comparisons, errors);
                    break;
                default:
                    errors.Add(new("component.kind_unknown", componentPath, "Unknown component kind."));
                    break;
            }
        }
    }

    private static void AddExpressions(FormField field, string path, List<(string, FormExpression)> expressions)
    {
        if (field.VisibleWhen is not null) expressions.Add(($"{path}.visibleWhen", field.VisibleWhen));
        if (field.RequiredWhen is not null) expressions.Add(($"{path}.requiredWhen", field.RequiredWhen));
        if (field.DisabledWhen is not null) expressions.Add(($"{path}.disabledWhen", field.DisabledWhen));
        if (field.ReadOnlyWhen is not null) expressions.Add(($"{path}.readOnlyWhen", field.ReadOnlyWhen));
        for (var index = 0; index < field.Validators.Count; index++)
            if (field.Validators[index].When is not null)
                expressions.Add(($"{path}.validators[{index}].when", field.Validators[index].When!));
    }

    private static void ValidateFieldConfiguration(FormField field, string path, List<(string Path, string OtherField)> comparisons, List<FormDefinitionError> errors)
    {
        if (field.Reference is not null)
        {
            if (field.Type != "choice")
                errors.Add(new("reference.field_type_invalid", $"{path}.type", "A reference source requires field type 'choice'."));
            ValidateExternalKey(field.Reference.ObjectKey, $"{path}.reference.objectKey", "reference.object_key_invalid", errors);
            ValidateIdentifier(field.Reference.LabelField, $"{path}.reference.labelField", "reference.label_field_invalid", errors);
        }

        if (field.Type == "choice")
        {
            var values = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < field.Options.Count; index++)
                if (!values.Add(field.Options[index].Value))
                    errors.Add(new("choice.value_duplicate", $"{path}.options[{index}].value", $"Choice value '{field.Options[index].Value}' is duplicated."));
        }

        for (var index = 0; index < field.Validators.Count; index++)
        {
            var validator = field.Validators[index];
            var validatorPath = $"{path}.validators[{index}]";
            if (!ValidatorTypes.Contains(validator.Type))
                errors.Add(new("validator.type_unknown", $"{validatorPath}.type", $"Unknown validator type '{validator.Type}'."));
            ValidateIdentifier(validator.Code, $"{validatorPath}.code", "validator.code_invalid", errors);
            if (validator.Type == "compare" && string.IsNullOrWhiteSpace(validator.OtherField))
                errors.Add(new("validator.configuration_invalid", validatorPath, "Compare validator requires otherField."));
            else if (validator.Type == "compare")
                comparisons.Add(($"{validatorPath}.otherField", validator.OtherField!));
            if (validator.Type == "pattern" && string.IsNullOrWhiteSpace(validator.Pattern))
                errors.Add(new("validator.configuration_invalid", validatorPath, "Pattern validator requires pattern."));
        }
    }

    private static void ValidateIdentifier(string? value, string path, string code, List<FormDefinitionError> errors)
    {
        if (value is null || !IdentifierPattern.IsMatch(value))
            errors.Add(new(code, path, $"'{value}' is not a valid protocol identifier."));
    }

    private static void ValidateExternalKey(string? value, string path, string code, List<FormDefinitionError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(new(code, path, "Domain object key is required."));
    }

    private static void ValidateProtocolId(string? value, string path, string code, List<FormDefinitionError> errors)
    {
        if (value is null || !ProtocolIdPattern.IsMatch(value))
            errors.Add(new(code, path, $"'{value}' is not a valid protocol ID."));
    }

    private static void ValidateExpression(FormExpression expression, string path, List<FormDefinitionError> errors)
    {
        try
        {
            _ = FormExpressionEvaluator.Dependencies(expression);
            ValidateShape(expression);
        }
        catch (FormProtocolException exception)
        {
            errors.Add(new("expression.invalid", path, exception.Message));
        }
    }

    private static void ValidateShape(FormExpression expression)
    {
        switch (expression.Operator)
        {
            case "and" or "or":
                if (expression.Arguments is not { Count: > 0 }) throw new FormProtocolException($"Operator '{expression.Operator}' requires non-empty arguments.");
                foreach (var child in expression.Arguments) ValidateShape(child);
                return;
            case "not":
                if (expression.Argument is null) throw new FormProtocolException("Operator 'not' requires argument.");
                ValidateShape(expression.Argument);
                return;
            case "equals" or "notEquals" or "greaterThan" or "greaterThanOrEqual" or "lessThan" or "lessThanOrEqual" or "contains" or "startsWith" or "endsWith":
                ValidateOperand(expression.Left, expression.Operator, "left");
                ValidateOperand(expression.Right, expression.Operator, "right");
                return;
            case "in" or "notIn":
                ValidateOperand(expression.Left, expression.Operator, "left");
                ValidateOperand(expression.Right, expression.Operator, "right");
                if (expression.Right?.Value.ValueKind != System.Text.Json.JsonValueKind.Array ||
                    expression.Right.Value.GetArrayLength() == 0 ||
                    expression.Right.Value.EnumerateArray().Any(item => item.ValueKind is not (
                        System.Text.Json.JsonValueKind.String or System.Text.Json.JsonValueKind.Number or
                        System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False or
                        System.Text.Json.JsonValueKind.Null)))
                    throw new FormProtocolException($"Operator '{expression.Operator}' requires a non-empty array of primitive values on the right.");
                return;
            case "isEmpty" or "isNotEmpty":
                ValidateOperand(expression.Operand, expression.Operator, "operand");
                return;
            default:
                throw new FormProtocolException($"Unknown form expression operator '{expression.Operator}'.");
        }
    }

    private static void ValidateOperand(FormOperand? operand, string op, string member)
    {
        if (operand is null) throw new FormProtocolException($"Operator '{op}' requires {member}.");
        var hasField = !string.IsNullOrWhiteSpace(operand.Field);
        var hasContext = !string.IsNullOrWhiteSpace(operand.Context);
        var hasValue = operand.Value.ValueKind != System.Text.Json.JsonValueKind.Undefined;
        if (Convert.ToInt32(hasField) + Convert.ToInt32(hasContext) + Convert.ToInt32(hasValue) != 1 ||
            hasContext && operand.Context != "viewMode")
            throw new FormProtocolException($"Operator '{op}' {member} must contain exactly one valid field, context or value.");
    }

    private static string ComponentPath(string expressionPath) =>
        expressionPath[..expressionPath.LastIndexOf(".", StringComparison.Ordinal)];
}
