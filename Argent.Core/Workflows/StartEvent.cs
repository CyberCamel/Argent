using Argent.Core.Attributes;
using Argent.Core.Workflows.Activites;
using System;
using System.Collections.Generic;
using System.Text;

namespace Argent.Core.Workflows;

[WorkflowCanvasElement("Start Event", "play_arrow", "Start", NodeShape.Circle, "An event that starts a workflow", "workflow-node node-event")]
public class StartEvent: Activity, INode
{

}
