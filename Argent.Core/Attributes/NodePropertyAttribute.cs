using Argent.Core.Workflows.Modeler.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class NodePropertyAttribute(string name, string description, bool required = false, PropertyDataType dataType = PropertyDataType.Text, int order = 100) : Attribute
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public bool Required { get; } = required;
    public PropertyDataType DataType { get; } = dataType;
    public int Order { get; } = order;
}
