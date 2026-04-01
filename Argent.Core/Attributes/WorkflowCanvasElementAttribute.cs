using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class WorkflowCanvasElementAttribute : Attribute
{
    public string DisplayName { get; }
    public string Icon { get; } // e.g., "bi-person-badge"
    public string Category { get; } // e.g., "Tasks", "Gateways"
    public string Description { get; }

    public WorkflowCanvasElementAttribute(string displayName, string icon, string category, string description = "")
    {
        DisplayName = displayName;
        Icon = icon;
        Category = category;
        Description = description;
    }
}
