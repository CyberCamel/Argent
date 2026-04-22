using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class WorkflowCanvasElementAttribute(string displayName, string icon, string category, NodeShape shape, string description = "", string cssClass = "") : Attribute
{
    public string DisplayName { get; } = displayName;
    public string Icon { get; } = icon;
    public string Category { get; } = category;
    public string Description { get; } = description;
    public string CssClass { get; } = cssClass;
    public NodeShape Shape { get; init; } = shape;
}
public enum NodeShape { Circle, Rectangle, Diamond }