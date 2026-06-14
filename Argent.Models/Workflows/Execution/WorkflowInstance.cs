using Argent.Models.Authorization;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Argent.Models.Attributes;

namespace Argent.Models.Workflows.Execution;

[PbacResource]
public record WorkflowInstance
{
    [Key]
    [PbacProperty]
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    [ForeignKey(nameof(Workflow))]
    [PbacProperty]
    public Guid WorkflowId { get; set; }
    /// <summary>
    /// The specific workflow version this instance is pinned to for its entire lifetime.
    /// Captured at start; the engine always executes against this version even after a newer
    /// version is deployed (which un-deploys the old one).
    /// </summary>
    public Guid VersionId { get; set; }
    [PbacProperty]
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    [PbacProperty]
    public InstanceState State { get; set; } = InstanceState.Running;
    [PbacProperty]
    public DateTime StartTime { get; set; } = DateTime.Now;
    [PbacProperty]
    public DateTime? EndTime { get; set; }
    public Guid RecordId { get; set; }
}
