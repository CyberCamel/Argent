using Argent.Contracts.Forms;
using Argent.Models.Forms.Components.Base;
using Argent.Models.Forms.Components.Configuration;

namespace Argent.WebComponents.Core.Forms;

public class DesignerFormContext(IConditionEvaluator conditionEvaluator) : IFormContext
{
    public event Action? OnStateChanged;
    public Dictionary<string, object?> Environment { get; } = new();
    public List<string> UserRoles { get; set; } = [];
    public string? UserId { get; set; }
    public Guid? RecordId { get; set; }

    public T? GetValue<T>(string key) => default;
    public object? GetValue(string key) => null;
    public void SetValue(string key, object? value) { }
    public void SetInitialValues(Dictionary<string, object?> values) { }
    public void NotifyStateChanged() => OnStateChanged?.Invoke();
    public Dictionary<string, object?> GetAllData() => new();
    public Dictionary<string, object?> GetAllValues() => new();

    public bool IsVisible(FormComponent component)
    {
        if (component is FormField field)
            return !field.Hidden;
        return true;
    }

    public bool IsRequired(FormField component)
    {
        if (!component.AllowBlank) return true;
        if (component.RequiredWhen != null)
            return conditionEvaluator.Evaluate(component.RequiredWhen, this);
        return false;
    }

    public IEnumerable<string> GetErrors(FormField component) => [];
}