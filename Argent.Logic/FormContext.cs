using Argent.Core.Forms.Components.Base;
using Argent.Core.Forms.Components.Configuration;
using NCalc;
using System.Text.RegularExpressions;

namespace Argent.Logic;

public class ArgentFormContext : IFormContext
{
    private readonly Dictionary<string, object?> _data = new();
    public Dictionary<string, object?> Environment { get; } = new();

    public event Action? OnStateChanged;

    public T? GetValue<T>(string key)
    {
        if (_data.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return default;
    }

    public void SetValue(string key, object? value)
    {
        // Avoid infinite loops/unnecessary renders
        if (_data.TryGetValue(key, out var existing) && Equals(existing, value))
            return;

        _data[key] = value;
        NotifyStateChanged();
    }

    public void NotifyStateChanged() => OnStateChanged?.Invoke();

    public bool IsVisible(FormComponent component)
    {
        if (string.IsNullOrWhiteSpace(component.Logic?.VisibleIf))
            return true;

        return EvaluateExpression<bool>(component.Logic.VisibleIf);
    }

    public bool IsRequired(FormComponent component)
    {
        if (string.IsNullOrWhiteSpace(component.Logic?.RequiredIf))
            return false;

        return EvaluateExpression<bool>(component.Logic.RequiredIf);
    }

    public bool IsValid(FormComponent component)
    {
        var value = GetValue<string>(component.DataKey) ?? string.Empty;
        foreach (var validator in component.Validators)
        {
            switch (validator.Type)
            {
                case ValidationType.Required:
                    return value.Trim() != string.Empty;
                case ValidationType.Regex:
                    return value.GetType() != typeof(string) || Regex.IsMatch(value.ToString() ?? "", validator.Pattern ?? "");
                case ValidationType.Expression:
                    return validator.Expression is null || EvaluateExpression<bool>(validator.Expression);
                case ValidationType.Specific:
                    var validationService = 
            }
            return true;
        }
    }

    private T EvaluateExpression<T>(string expression)
    {
        try
        {
            var exp = new Expression(expression)
            {
                Parameters = GetCombinedContext()
            };
            var result = exp.Evaluate();
            return (T)Convert.ChangeType(result, typeof(T));
        }
        catch
        {
            return default!;
        }
    }

    private Dictionary<string, object?> GetCombinedContext()
    {
        // Merge form data and environment (User/Task) for the evaluator
        var combined = new Dictionary<string, object?>(Environment);
        foreach (var kvp in _data) combined[kvp.Key] = kvp.Value;
        return combined;
    }

    public IEnumerable<string> GetErrors(string componentId) => Enumerable.Empty<string>(); // Placeholder
    public Dictionary<string, object?> GetAllData() => _data;
}
