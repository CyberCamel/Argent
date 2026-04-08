using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class WorkflowCanvasElementAttribute : Attribute
{
    public string DisplayName { get; }
    public string Icon { get; }
    public string Category { get; }
    public string Description { get; }
    public string CssClass { get; }
    public NodeShape Shape { get; init; } = NodeShape.Rectangle;

    public WorkflowCanvasElementAttribute(string displayName, string icon, string category, NodeShape shape, string description = "", string cssClass = null)
    {
        DisplayName = displayName;
        Icon = icon;
        Category = category;
        Description = description;
        CssClass = cssClass;
        Shape = shape;

    }
}
public enum NodeShape { Circle, Rectangle, Diamond }