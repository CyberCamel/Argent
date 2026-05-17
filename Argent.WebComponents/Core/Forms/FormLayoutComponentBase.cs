using Argent.Contracts.Forms;
using Argent.Models.Forms.Components.Base;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.WebComponents.Core.Forms;

public abstract class FormLayoutComponentBase : ComponentBase
{
    [CascadingParameter]
    public IFormContext Context { get; set; } = default!;
    
    [Parameter, EditorRequired]
    public FormLayoutComponent Metadata { get; set; }

    protected bool IsVisible => Context.IsVisible(Metadata);

}
