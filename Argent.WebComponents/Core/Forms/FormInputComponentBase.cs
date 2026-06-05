using Argent.Contracts.Forms;
using Argent.Models.Forms.Components.Base;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.WebComponents.Core.Forms;

public abstract class FormInputComponentBase : ComponentBase, IDisposable
{
    [CascadingParameter]
    public IFormContext Context { get; set; } = default!;

    [Parameter, EditorRequired]
    public FormInputComponent Metadata { get; set; } = default!;

    protected string DisplayLabel => Context.IsRequired(Metadata) ? $"{Metadata.Label} *" : Metadata.Label ?? string.Empty;

    protected bool IsVisible => Context.IsVisible(Metadata);

    protected override void OnInitialized()
    {
        if (Context != null)
        {
            Context.OnStateChanged += OnNotifyStateChanged;
        }
    }

    // We use StateHasChanged so Blazor re-evaluates the 'IsVisible' 
    // property whenever the global form state updates.
    protected virtual void OnNotifyStateChanged()
    {
        InvokeAsync(StateHasChanged);
    }

    public virtual void Dispose()
    {
        if (Context != null)
        {
            Context.OnStateChanged -= OnNotifyStateChanged;
        }
    }
}
