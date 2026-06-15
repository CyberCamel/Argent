using Argent.Contracts.Authorization;
using Argent.Contracts.Forms;
using Argent.Models.Authorization;
using Argent.Models.DomainObjects;
using Argent.Models.Forms.Components.Base;
using Argent.Runtime.DomainObjects;

namespace Argent.Runtime.Forms;

public class ArgentFormContext(
    IFormValidator _validator,
    IConditionEvaluator _conditionEvaluator,
    IPolicyDecisionService _policyService) : IFormContext
{
    private readonly Dictionary<string, object?> _data = new();
    private readonly HashSet<string> _touched = [];
    private readonly Dictionary<string, List<string>> _serverErrors = new();
    private bool _showAllErrors;

    /// <summary>When set, domain-level validation (required + type compat) runs live in GetErrors.</summary>
    public DomainObjectDefinition? DomainDefinition { get; set; }

    public Dictionary<string, object?> Environment { get; } = new();
    public List<string> UserRoles { get; set; } = [];
    public string? UserId { get; set; }
    public Guid? RecordId { get; set; }

    public event Action? OnStateChanged;

    public T? GetValue<T>(string key)
    {
        if (_data.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return default;
    }

    public object? GetValue(string key)
    {
        _data.TryGetValue(key, out var value);
        return value;
    }

    public void SetInitialValues(Dictionary<string, object?> values)
    {
        foreach (var kvp in values)
        {
            if (!_data.ContainsKey(kvp.Key))
                _data[kvp.Key] = kvp.Value;
        }
    }

    public void SetValue(string key, object? value)
    {
        if (_data.TryGetValue(key, out var existing) && Equals(existing, value))
            return;
        _data[key] = value;
        _touched.Add(key);
        _serverErrors.Remove(key);
        NotifyStateChanged();
    }

    /// <summary>
    /// Stores server-side validation errors (e.g. uniqueness constraint failures) keyed by field name.
    /// Each field's errors are cleared automatically when the user next edits that field.
    /// </summary>
    public void SetServerErrors(Dictionary<string, List<string>> errors)
    {
        _serverErrors.Clear();
        foreach (var kvp in errors) _serverErrors[kvp.Key] = kvp.Value;
        NotifyStateChanged();
    }

    public void NotifyStateChanged() => OnStateChanged?.Invoke();

    public bool IsVisible(FormComponent component)
    {
        if (component is FormField field)
        {
            if (!_conditionEvaluator.EvaluateFieldVisible(field, this))
                return false;

            if (!string.IsNullOrWhiteSpace(field.RequiredPermission))
                return CheckPermission(field.RequiredPermission).GetAwaiter().GetResult();
        }
        return true;
    }

    public bool IsRequired(FormField component) =>
        _conditionEvaluator.EvaluateFieldRequired(component, this);

    /// <summary>
    /// Errors for a single field. Pristine (untouched) fields report no errors until
    /// <see cref="RevealAllErrors"/> is called — typically on a submit attempt.
    /// </summary>
    public IEnumerable<string> GetErrors(FormField component)
    {
        if (string.IsNullOrEmpty(component.Name)) return [];
        if (!_showAllErrors && !_touched.Contains(component.Name)) return [];

        var errors = _validator.ValidateField(component, this);

        // Domain validation (required + type compatibility) runs live when a definition is bound.
        // Only applied when the form layer has no errors already — avoids duplicate "required" messages.
        if (DomainDefinition != null && errors.Count == 0)
        {
            var domainErrors = DomainRecordValidator.Validate(DomainDefinition, GetAllValues())
                .Where(e => string.Equals(e.Property, component.Name, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Message)
                .ToList();
            if (domainErrors.Count > 0)
                errors = [.. errors, .. domainErrors];
        }

        // Server errors (e.g. uniqueness) persist until the user edits the field.
        if (_serverErrors.TryGetValue(component.Name, out var serverErrs))
            errors = [.. errors, .. serverErrs];

        return errors;
    }

    /// <summary>After this call every field shows its errors, touched or not.</summary>
    public void RevealAllErrors()
    {
        _showAllErrors = true;
        NotifyStateChanged();
    }

    public Dictionary<string, object?> GetAllData() => _data;

    public Dictionary<string, object?> GetAllValues()
    {
        var combined = new Dictionary<string, object?>(Environment);
        foreach (var kvp in _data) combined[kvp.Key] = kvp.Value;
        return combined;
    }

    private async Task<bool> CheckPermission(string permission)
    {
        var parts = permission.Split(':', 2);
        var resourceTypeStr = parts.Length > 0 ? parts[0] : "";
        var action = parts.Length > 1 ? parts[1] : "read";

        if (string.IsNullOrWhiteSpace(resourceTypeStr))
            return false;

        var userId = UserId ?? "unknown";
        var resourceAttrs = new Dictionary<string, object?>
        {
            ["action"] = action
        };

        var result = await _policyService.EvaluateAsync(userId, UserRoles, resourceTypeStr, resourceAttrs, action);
        return result == PolicyDecision.Allow;
    }
}
