using Argent.Contracts.Forms;
using Argent.Models.Forms.Components.Base;
using Argent.Models.Forms.Components.Configuration;
using System.Text.RegularExpressions;

namespace Argent.Runtime.Forms;

public class ArgentFormContext(
    IFormValidatorRegistry formValidatorRegistry,
    IConditionEvaluator conditionEvaluator) : IFormContext
{
    private readonly Dictionary<string, object?> _data = new();
    public Dictionary<string, object?> Environment { get; } = new();
    public List<string> UserRoles { get; set; } = [];

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

    public void SetValue(string key, object? value)
    {
        if (_data.TryGetValue(key, out var existing) && Equals(existing, value))
            return;
        _data[key] = value;
        NotifyStateChanged();
    }

    public void NotifyStateChanged() => OnStateChanged?.Invoke();

    public bool IsVisible(FormComponent component)
    {
        if (component is FormField field)
            return conditionEvaluator.EvaluateFieldVisible(field, this);
        return true;
    }

    public bool IsRequired(FormField component)
    {
        return conditionEvaluator.EvaluateFieldRequired(component, this);
    }

    public bool IsValid(FormField component)
    {
        var errors = new List<string>();
        bool validationState = true;

        if (component.Name is null)
            return true;

        var value = GetValue<string>(component.Name) ?? "";

        foreach (var validator in component.Validators)
        {
            var conditionMet = validator.Condition == null
                || conditionEvaluator.Evaluate(validator.Condition, this);

            bool fails = false;

            if (conditionMet && !string.IsNullOrEmpty(validator.ErrorMessage))
            {
                var expr = validator.ErrorKey;
                if (!string.IsNullOrEmpty(expr))
                {
                    try
                    {
                        var e = new NCalc.Expression(expr);
                        foreach (var kvp in GetCombinedContext())
                            e.Parameters[kvp.Key] = kvp.Value;
                        var result = e.Evaluate();
                        if (result is bool b) fails = !b;
                        else fails = !Convert.ToBoolean(result);
                    }
                    catch
                    {
                        fails = true;
                    }
                }
                else
                {
                    fails = true;
                }
            }

            if (fails)
            {
                errors.Add(validator.ErrorMessage ?? "");
                validationState = false;
            }
        }

        if (IsRequired(component))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add("This field is required.");
                validationState = false;
            }
        }

        Errors.Clear();
        Errors[component.Name] = errors;
        return validationState;
    }

    public IEnumerable<string> GetErrors(FormField component)
    {
        IsValid(component);
        if (component.Name is not null && Errors.TryGetValue(component.Name, out var e))
            return e;
        return [];
    }

    public Dictionary<string, object?> GetAllData() => _data;

    public Dictionary<string, object?> GetAllValues()
    {
        var combined = new Dictionary<string, object?>(Environment);
        foreach (var kvp in _data) combined[kvp.Key] = kvp.Value;
        return combined;
    }

    private Dictionary<string, object?> GetCombinedContext()
    {
        var combined = new Dictionary<string, object?>(Environment);
        foreach (var kvp in _data) combined[kvp.Key] = kvp.Value;
        return combined;
    }

    private readonly Dictionary<string, List<string>> Errors = [];
}