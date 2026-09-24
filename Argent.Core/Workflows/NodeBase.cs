using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using Argent.Core.Workflows.Activities;
using Argent.Core.Workflows.BoundaryEvents;
using Argent.Core.Workflows.Intermediates;

namespace Argent.Core.Workflows;

[JsonDerivedType(typeof(JintActivity), typeDiscriminator: "jint")]
[JsonDerivedType(typeof(RestActivity), typeDiscriminator: "rest")]
[JsonDerivedType(typeof(UserActivity), typeDiscriminator: "user")]
[JsonDerivedType(typeof(SQLActivity), typeDiscriminator: "sql")]
[JsonDerivedType(typeof(ScriptActivity), typeDiscriminator: "script")]
[JsonDerivedType(typeof(InclusiveGateway), typeDiscriminator: "incl-gw")]
[JsonDerivedType(typeof(ExclusiveGateway), typeDiscriminator: "excl-gw")]
[JsonDerivedType(typeof(ParallelGateway), typeDiscriminator: "par-gw")]
[JsonDerivedType(typeof(StartEvent), typeDiscriminator: "start")]
[JsonDerivedType(typeof(EndEvent), typeDiscriminator: "end")]
[JsonDerivedType(typeof(CatchingTimerEvent), typeDiscriminator: "timer-catch")]
[JsonDerivedType(typeof(TimerBoundaryEvent), typeDiscriminator: "timer-boundary")]
public abstract class NodeBase : WorkflowElement
{

    public List<Connection> Inbound { get; set; } = [];
    public List<Connection> Outbound { get; set; } = [];

}
