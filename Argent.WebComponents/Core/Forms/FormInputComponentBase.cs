using Argent.Contracts.Forms;
using Argent.Models.Forms.Components.Base;
using Microsoft.AspNetCore.Components;

namespace Argent.WebComponents.Core.Forms;

public abstract class FormInputComponentBase : ComponentBase, IDisposable
{
    [CascadingParameter]
    public IFormContext Context { get; set; } = default!;

    [Parameter]
    public FormField Metadata { get; set; } = default!;

    [Parameter]
    public string? DisplayLabel { get; set; }

    [Parameter]
    public EventCallback<FormField> OnMetadataChanged { get; set; }

    protected string Label => DisplayLabel ?? Metadata.FieldLabel ?? "";

    protected override void OnInitialized()
    {
        Context.OnStateChanged += StateHasChanged;
    }

    public void Dispose()
    {
        Context.OnStateChanged -= StateHasChanged;
    }
}