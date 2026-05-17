using Argent.Models.Attributes;
using Argent.Models.Workflows.Modeler.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows.Activites;

public abstract class ServerActivity: Activity
{
    [NodeProperty("Max Retries", "The maximum number of retries if the activity fails", false, PropertyDataType.Number)]
    public int MaxRetries { get; set; }
    
    [NodeProperty("Failure Path", "The path to follow if the activity fails", false, PropertyDataType.Text)]
    public Connection? FailurePath { get; set; } = null;
}
