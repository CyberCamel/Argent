using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows
{
    public class Workflow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "New Workflow";
        public string Description { get; set; } = string.Empty;
        public WorkflowDefinition Definition { get; set; } = new WorkflowDefinition();
    }
}
