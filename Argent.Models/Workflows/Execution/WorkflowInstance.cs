using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Argent.Models.Workflows.Execution;

public record WorkflowInstance
{
    [Key]
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    [ForeignKey(nameof(Workflow))]
    public Guid WorkflowId { get; set; }
    /// <summary>
    /// The specific workflow version this instance is pinned to for its entire lifetime.
    /// Captured at start; the engine always executes against this version even after a newer
    /// version is deployed (which un-deploys the old one).
    /// </summary>
    public Guid VersionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public InstanceState State { get; set; } = InstanceState.Running;
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime? EndTime { get; set; }
}
