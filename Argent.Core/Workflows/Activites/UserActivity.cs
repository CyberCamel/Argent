using Argent.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows.Activites;

[WorkflowCanvasElement("User Activity", "assignment_ind", "Task", NodeShape.Rectangle, "An activity that requires user interaction", "node-user")]
public class UserActivity : Activity
{
    public UserExperience UX { get; set; } = new RedirectExperience("https://example.com");

}
