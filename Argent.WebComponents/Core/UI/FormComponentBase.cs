using Argent.Core.Forms.Components.Base;
using Argent.Logic;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.WebComponents.Core.UI;

public abstract class FormComponentBase : ComponentBase, IDisposable
{
    [CascadingParameter]
    public IFormContext Context { get; set; } = default!;

    [Parameter, EditorRequired]
    public FormComponent Metadata { get; set; } = default!;

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
