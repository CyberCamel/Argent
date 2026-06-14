using Argent.Contracts.Authorization;
using Argent.Contracts.Forms;
using Argent.Models.Authorization;
using Argent.Models.Forms.Components.Base;

namespace Argent.Runtime.Forms;

public class ArgentFormContext(
    IFormValidator _validator,
    IConditionEvaluator _conditionEvaluator,
    IPolicyDecisionService _policyService) : IFormContext
{
    private readonly Dictionary<string, object?> _data = new();
    private readonly HashSet<string> _touched = [];
    private bool _showAllErrors;

    public Dictionary<string, object?> Environment { get; } = new();
    public List<string> UserRoles { get; set; } = [];
    public string? UserId { get; set; }

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
        return _validator.ValidateField(component, this);
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

        if (!Enum.TryParse<ResourceType>(resourceTypeStr, ignoreCase: true, out var resourceType))
            return false;

        var userId = UserId ?? "unknown";
        var resourceAttrs = new Dictionary<string, object?>
        {
            ["action"] = action
        };

        var result = await _policyService.EvaluateAsync(userId, UserRoles, resourceType, resourceAttrs, action);
        return result == PolicyDecision.Allow;
    }
}
