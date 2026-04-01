using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows
{
    public class Connection : CanvasElement
    {
        public string? Expression { get; set; }
        public required Node From { get; set; }
        public required Node To { get; set; }

    }
}
