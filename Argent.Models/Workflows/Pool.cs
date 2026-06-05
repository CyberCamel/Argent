using Argent.Models.Workflows.Modeler;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Argent.Models.Workflows;

[JsonDerivedType(typeof(Pool), typeDiscriminator: "pool")]
public class Pool : LayoutElement
{
    public List<Lane> Lane { get; set; } = [];
}
