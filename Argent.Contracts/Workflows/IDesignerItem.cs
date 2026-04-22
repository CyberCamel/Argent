using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Contracts.Workflows;

public interface IDesignerItem
{
    public string Title { get; set; }
    public string Description { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}
