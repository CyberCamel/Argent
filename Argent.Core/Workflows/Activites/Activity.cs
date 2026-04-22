using Argent.Core.Workflows.Modeler;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows.Activites
{
    public class Activity : CanvasElement, INode
    {
        public List<Connection> OutgoingPaths { get; set; } = [];
        public List<Connection> IncomingPaths { get; set; } = [];
    }
}
