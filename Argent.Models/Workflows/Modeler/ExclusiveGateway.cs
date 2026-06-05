using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Argent.Models.Workflows.Modeler
{
    [JsonDerivedType(typeof(ExclusiveGateway), typeDiscriminator: "exclusiveGateway")]
    public class ExclusiveGateway : Gateway
    {
    }
}
