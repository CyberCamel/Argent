namespace Argent.Models.Workflows.Auditing;

/// <summary>Standard audit event types used across the platform. Stored as strings in the EventType column.</summary>
public enum WorkflowAuditEventType
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
    TaskStarted = 11,
    NodeFailed = 12,
    InstanceSuspended = 13,
    InstanceResumed = 14,
    InstanceCancelled = 15,
    DomainObjectCreated = 16,
    DomainObjectUpdated = 17,
    DomainObjectDeleted = 18,
    WorkflowPublished = 19,
    WorkflowDeployed = 20,
    GatewayEvaluated = 21,
    TaskReleased = 22
}
