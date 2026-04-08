using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows;

public interface INode
{
    public List<Connection> OutgoingPaths { get; set; }
    public List<Connection> IncomingPaths { get; set; }

}
