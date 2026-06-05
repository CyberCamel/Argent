using Argent.Models.Workflows.Modeler;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Argent.Models.Workflows;

[JsonDerivedType(typeof(Lane), typeDiscriminator: "lane")]
public class Lane : LayoutElement
{
    
}
