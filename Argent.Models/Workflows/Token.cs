using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows;

public class Token
{
    public Guid Id { get; set; }
    public NodeBase? Position { get; set; }
    public int Steps { get; set; } = 0;
     
    public Guid? CorrelationId = Guid.Empty;
    public TokenState State { get; set; }

}
