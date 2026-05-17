using Argent.Contracts.Workflows;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Runtime.Workflows.Modeling
{
    public class DesignerLane : IDesignerItem
    {
        public string Title { get; set; } = "New Lane";
        public string Description { get; set; } = string.Empty;
        public double Width { get; set; } = 400;
        public double Height { get; set; } = 200;
        public double X { get; set; }
        public double Y { get; set; }

    }
}
