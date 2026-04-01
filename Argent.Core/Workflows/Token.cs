using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows;

public class Token
{
    public Guid Id { get; set; }
    public Node Position { get; set; }
    public int Steps { get; set; } = 0;
    // 
    public Guid? CorrelationId = Guid.Empty;
}
