using Argent.Models.Forms.Components.Base;
using Microsoft.AspNetCore.Components;

namespace Argent.WebComponents.Core.Forms;

public abstract class FormLayoutComponentBase : ComponentBase
{
    [Parameter]
    public FormLayout Metadata { get; set; } = default!;

    [Parameter]
    public EventCallback<FormLayout> OnMetadataChanged { get; set; }
}