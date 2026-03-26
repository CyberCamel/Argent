using System;
using System.Collections.Generic;
using System.Text;
using Argent.Core.Forms.Components.Configuration;

namespace Argent.Core.Forms.Components.Base;



public class FormComponent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = string.Empty; // e.g., "TextField"
    public required string DataKey { get; set; }

    public string? Label { get; set; }
    public string? LabelKey { get; set; }
    public string? Description { get; set; }

    public object? DefaultValue { get; set; }

    public ComponentLogic Logic { get; set; } = new();
    public LayoutConfig Layout { get; set; } = new();


    public DataProviderConfig? DataProvider { get; set; }
    public List<HtmlTemplate> Templates { get; set; } = new();

    public List<Validator> Validators { get; set; } = new();


    // Recursion
    public List<FormComponent> Children { get; set; } = new();
    public List<FormComponent> Template { get; set; } = new(); // For Repeaters
}
