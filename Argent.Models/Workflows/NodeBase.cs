using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using Argent.Models.Workflows.Activities;
using Argent.Models.Workflows.Modeler;

namespace Argent.Models.Workflows;

[JsonDerivedType(typeof(JintActivity), typeDiscriminator: "jint")]
[JsonDerivedType(typeof(RestActivity), typeDiscriminator: "rest")]
[JsonDerivedType(typeof(UserActivity), typeDiscriminator: "user")]
[JsonDerivedType(typeof(SQLActivity), typeDiscriminator:"sql")]
[JsonDerivedType(typeof(InclusiveGateway), typeDiscriminator:"incl-gw")]
[JsonDerivedType(typeof(StartEvent), typeDiscriminator:"start")]
[JsonDerivedType(typeof(EndEvent), typeDiscriminator:"end")]
public abstract class NodeBase : WorkflowElement
{

    public List<Connection> Inbound { get; set; } = [];
    public List<Connection> Outbound { get; set; } = [];

}
