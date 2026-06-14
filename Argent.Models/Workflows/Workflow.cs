using System;
using System.Collections.Generic;
using System.Text;
using Argent.Models.Attributes;
using Argent.Models.Identity;

namespace Argent.Models.Workflows;

[PbacResource]
public class Workflow
{
    [PbacProperty]
    public Guid Id { get; set; } = Guid.NewGuid();
    [PbacProperty]
    public string Name { get; set; } = "New Workflow";
    [PbacProperty]
    public string Description { get; set; } = string.Empty;
    [PbacProperty]
    public Guid? CreatedById { get; set; }
    public InternalUser? CreatedBy { get; set; }

    public Guid? UpdatedById { get; set; }
    public InternalUser? UpdatedBy { get; set; }
    [PbacProperty]
    public DateTime CreatedOn { get; set; }
    public DateTime UpdatedOn { get; set; }
    [PbacProperty]
    public List<string> Tags { get; set; }
}

