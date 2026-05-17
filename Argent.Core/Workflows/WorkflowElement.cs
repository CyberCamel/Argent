using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows;

public abstract class WorkflowElement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
}
