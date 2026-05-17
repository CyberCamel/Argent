using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Models.Workflows.Auditing;

public enum WorkflowAuditEventType : byte
{
    None = 0,
    InstanceStarted = 1,
    InstanceCompleted = 2,
    TokenCreated = 3,
    TokenMoved = 4,
    TokenConsumed = 5,
    TaskCreated = 6,
    TaskReassigned = 7,
    TaskCompleted = 8,
    TaskFailed = 9,
    TaskCancelled = 10,
    TaskStarted = 11

}
