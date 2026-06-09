using Argent.Models.Forms.Components.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Forms;


public interface IFormContext
{
    // Data Management
    T? GetValue<T>(string key);
    object? GetValue(string key);
    void SetValue(string key, object? value);
    Dictionary<string, object?> GetAllData();
    Dictionary<string, object?> GetAllValues();

    // Logic & Environment
    bool IsVisible(FormComponent component);
    bool IsRequired(FormField component);
    IEnumerable<string> GetErrors(FormField component);

    // Identity/Process Context (For the HTML Templates)
    Dictionary<string, object?> Environment { get; }
    List<string> UserRoles { get; }

    // Reactivity
    event Action OnStateChanged;
    void NotifyStateChanged();
}
