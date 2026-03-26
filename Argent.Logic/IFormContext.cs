using Argent.Core.Forms.Components.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Logic;


public interface IFormContext
{
    // Data Management
    T? GetValue<T>(string key);
    void SetValue(string key, object? value);
    Dictionary<string, object?> GetAllData();

    // Logic & Environment
    bool IsVisible(FormComponent component);
    bool IsRequired(FormComponent component);
    IEnumerable<string> GetErrors(string componentId);

    // Identity/Process Context (For the HTML Templates)
    Dictionary<string, object?> Environment { get; }

    // Reactivity
    event Action OnStateChanged;
    void NotifyStateChanged();
}
