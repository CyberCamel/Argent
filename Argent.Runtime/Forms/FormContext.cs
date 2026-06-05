using NCalc;
using System.Text.RegularExpressions;
using Argent.Contracts.Forms;
using Argent.Models.Forms.Components.Base;
using Argent.Models.Forms.Components.Configuration;

namespace Argent.Runtime.Forms;

public class ArgentFormContext(IValidationRegistry validationRegistry) : IFormContext
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
        if (string.IsNullOrWhiteSpace(component.VisibleIf))
            return true;

        return EvaluateExpression<bool>(component.VisibleIf);
    }

    public bool IsRequired(FormInputComponent component)
    {
        if (string.IsNullOrWhiteSpace(component.RequiredIf))
            return false;

        return EvaluateExpression<bool>(component.RequiredIf);
    }

    public bool IsValid(FormInputComponent component)
    {

        var errors = new List<string>();
        bool validationState = true;
        if (component.DataKey is null)
        {
            return true;
        }

        var value = GetValue<string>(component.DataKey) ?? "";
        foreach (var validator in component.Validators)
        {
            switch (validator.Type)
            {
                case ValidationType.Required:
                    if (value.Trim() != string.Empty) 
                    { 
                        errors.Add(validator.ErrorMessage ?? ""); 
                        validationState = false; 
                    }; 
                    break;
                case ValidationType.Regex:
                    if(!Regex.IsMatch(value, validator.Pattern ?? "")) 
                    { 
                        errors.Add(validator.ErrorMessage ?? ""); 
                        validationState = false; }; 
                    break;
                case ValidationType.Expression:
                    if(!(validator.Expression is null || EvaluateExpression<bool>(validator.Expression))){
                        errors.Add(validator.ErrorMessage ?? ""); 
                        validationState = false; 
                    }; 
                    break;
                case ValidationType.Specific:
                    if (!(validator.Handler is not null && validationRegistry.GetService(validator.Handler).Validate(value)))
                    {
                        errors.Add(validator.ErrorMessage ?? "");
                        validationState = false;
                    }
                    break;
            }
        }
        if (IsRequired(component))
        {
            errors.Add("This field is required.");
            validationState = false;
        }
        Errors.Clear();
        Errors[component.DataKey] = errors;
        return validationState;



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

    private readonly Dictionary<string, List<string>> Errors = [];

    public IEnumerable<string> GetErrors(FormInputComponent comp) {
        IsValid(comp);
        if(comp.DataKey is not null && Errors.TryGetValue(comp.DataKey, out var e))
        {
            return e;
        }
        else
        {
            return [];
        }
    }
    public Dictionary<string, object?> GetAllData() => _data;
}
