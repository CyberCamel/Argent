using Argent.Models.Attributes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Argent.Models.Workflows
{
    [WorkflowCanvasElement("Inclusive gateway", "diamond", "Control", NodeShape.Diamond, "An inclusive gateway, does stuff", "gw gw-inclusive")]
    [JsonDerivedType(typeof(InclusiveGateway), typeDiscriminator: "inclusiveGateway")]
    public class InclusiveGateway: Gateway
    {
    }
}
