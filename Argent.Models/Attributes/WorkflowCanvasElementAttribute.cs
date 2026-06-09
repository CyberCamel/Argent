namespace Argent.Models.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class WorkflowCanvasElementAttribute(
    string displayName,
    string icon,
    string category,
    NodeShape shape,
    string description = "",
    string cssClass = "",
    double defaultWidth = 160,
    double defaultHeight = 80) : Attribute
{
    public string DisplayName { get; } = displayName;
    public string Icon { get; } = icon;
    public string Category { get; } = category;
    public string Description { get; } = description;
    public string CssClass { get; } = cssClass;
    public NodeShape Shape { get; init; } = shape;
    public double DefaultWidth { get; } = defaultWidth;
    public double DefaultHeight { get; } = defaultHeight;
}

public enum NodeShape { Circle, Rectangle, Diamond }
