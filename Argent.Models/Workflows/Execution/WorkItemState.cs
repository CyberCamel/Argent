namespace Argent.Models.Workflows.Execution;

public enum WorkItemState : byte
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Waiting = 4
}
