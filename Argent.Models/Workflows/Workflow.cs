using System;
using System.Collections.Generic;
using System.Text;
using Argent.Models.Identity;

namespace Argent.Models.Workflows
{
    public class Workflow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "New Workflow";
        public string Description { get; set; } = string.Empty;
        public Guid? CreatedById { get; set; }
        public InternalUser? CreatedBy { get; set; }

        public Guid? UpdatedById { get; set; }
        public InternalUser? UpdatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime UpdatedOn { get; set; }
        public List<string> Tags { get; set; }
    }
}
