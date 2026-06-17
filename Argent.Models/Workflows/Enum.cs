using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows
{
    public enum TokenState
    {
        Ready,
        Waiting,
        Consumed
    }

    public enum TimerState
    {
        Pending,    // persisted, not yet scheduled in memory
        Enqueued,   // Task.Delay running in memory
        Fired,      // delay elapsed, WorkItem created
        Cancelled   // token was consumed before the timer fired
    }
}
