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
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public InstanceState State { get; set; } = InstanceState.Running;
    public int CurrentTokenCount { get; set; }
    public DateTime StartTime { get; set; } = DateTime.Now;
    public DateTime? EndTime { get; set; }
}
