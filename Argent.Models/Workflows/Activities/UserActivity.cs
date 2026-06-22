using Argent.Models.Attributes;
using Argent.Models.Workflows.Modeler.Enums;

namespace Argent.Models.Workflows.Activities;

[WorkflowCanvasElement("User Activity", "assignment_ind", "Task", NodeShape.Rectangle, "An activity that requires user interaction", "node-user")]
public class UserActivity : Activity
{
    public UserExperience UX { get; set; } = new RedirectExperience("https://example.com");

    [NodeProperty("Task Title", "Display title for the task", false, PropertyDataType.Text)]
    public string? TaskTitle { get; set; }

    [NodeProperty("Task Description", "Description shown to the assignee", false, PropertyDataType.MultiLineText)]
    public string? TaskDescription { get; set; }

    [NodeProperty("Task Priority", "Higher values mean higher priority", false, PropertyDataType.Number)]
    public short TaskPriority { get; set; }

    // Baked in at compile time when the node is inside a role-bearing swimlane; null otherwise.
    public Guid? LaneRoleId { get; set; }
}
