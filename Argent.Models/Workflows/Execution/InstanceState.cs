namespace Argent.Models.Workflows.Execution;

public enum InstanceState : byte
{
    Running = 0,
    Suspended = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}
