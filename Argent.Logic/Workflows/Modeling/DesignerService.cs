using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Logic.Workflows.Modeling;

public class DesignerService
{
    public List<DesignerNode> CanvasNodes { get; } = new();
    public event Action? OnChange;

    public void AddNode(string type, string title, double x, double y)
    {
        CanvasNodes.Add(new DesignerNode { NodeType = type, Title = title, X = x, Y = y });
        OnChange?.Invoke();
    }
}
