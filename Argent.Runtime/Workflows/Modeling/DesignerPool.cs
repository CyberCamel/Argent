using Argent.Contracts.Workflows;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Runtime.Workflows.Modeling
{
    public class DesignerPool : IDesignerItem
    {
        public List<DesignerLane> Lanes { get; }
        public string Title { get; set; } = "New Pool";
        public string Description { get; set; } = string.Empty;
        public double Height { get; set; }
        public double Width { get; set; }
        public double Y { get; set; }
        public double X { get; set; }
    }
}
