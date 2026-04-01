using Argent.Core.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows.Activites;

[WorkflowCanvasElement("User Activity", "assignment_ind", "Task", "An activity that requires user interaction")]
public class UserActivity : Activity
{
    public UserExperience UX { get; set; } = new RedirectExperience("https://example.com");

}
