namespace Argent.Core.Workers;

/// <summary>Operational state of a registered worker. <see cref="Offline"/> is derived from the heartbeat, not set by the worker.</summary>
public enum WorkerStatus
{
    Offline = 0,
    Idle = 1,
    Busy = 2,
    Disabled = 3
}
