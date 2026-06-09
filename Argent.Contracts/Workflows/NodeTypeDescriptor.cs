namespace Argent.Contracts.Workflows;

public enum NodeShape
{
    Circle,
    Rectangle,
    Diamond
}

public enum PropertyDataType
{
    Text,
    Number,
    Boolean,
    DateTime,
    MultiLineText,
    KeyValuePairs,
    Code
}

public class NodeTypeDescriptor
{
    public required Type NodeType { get; init; }
    public required string DisplayName { get; init; }
    public required string Icon { get; init; }
    public required string Category { get; init; }
    public required NodeShape Shape { get; init; }
    public string Description { get; init; } = "";
    public string CssClass { get; init; } = "";
    public double DefaultWidth { get; init; } = 160;
    public double DefaultHeight { get; init; } = 80;
    public List<PropertyDescriptor> Properties { get; set; } = [];
}

public class PropertyDescriptor
{
    public required string PropertyName { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = "";
    public bool Required { get; init; }
    public PropertyDataType DataType { get; init; } = PropertyDataType.Text;
    public int Order { get; init; } = 100;
}
