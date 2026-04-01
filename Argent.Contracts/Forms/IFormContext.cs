using Argent.Core.Forms.Components.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Forms;


public interface IFormContext
{
    // Data Management
    T? GetValue<T>(string key);
    void SetValue(string key, object? value);
    Dictionary<string, object?> GetAllData();

    // Logic & Environment
    bool IsVisible(FormComponent component);
    bool IsRequired(FormInputComponent component);
    IEnumerable<string> GetErrors(FormInputComponent component);

    // Identity/Process Context (For the HTML Templates)
    Dictionary<string, object?> Environment { get; }

    // Reactivity
    event Action OnStateChanged;
    void NotifyStateChanged();
}
