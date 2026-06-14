using Argent.Contracts.Authorization;
using Argent.Models.Forms.Components.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Forms;


public interface IFormContext : IAttributeBag
{
    // Data Management
    T? GetValue<T>(string key);
    new object? GetValue(string key);
    void SetValue(string key, object? value);
    void SetInitialValues(Dictionary<string, object?> values);
    Dictionary<string, object?> GetAllData();
    new Dictionary<string, object?> GetAllValues();

    // Logic & Environment
    bool IsVisible(FormComponent component);
    bool IsRequired(FormField component);
    IEnumerable<string> GetErrors(FormField component);

    // Identity/Process Context (For the HTML Templates)
    Dictionary<string, object?> Environment { get; }
    new List<string> UserRoles { get; }
    string? UserId { get; set; }
    Guid? RecordId { get; set; }

    // Reactivity
    event Action OnStateChanged;
    void NotifyStateChanged();
}
