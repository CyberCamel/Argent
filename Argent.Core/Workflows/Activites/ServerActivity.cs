using Argent.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows.Activites;

public abstract class ServerActivity: Activity
{
    public int MaxRetries { get; set; }
    public Connection? FailurePath { get; set; } = null;
}
