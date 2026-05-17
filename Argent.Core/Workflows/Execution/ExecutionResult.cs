using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows.Execution;

public record ExecutionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; } = string.Empty;
    public DateTime TimeStamp { get; set; } = DateTime.Now;
}
