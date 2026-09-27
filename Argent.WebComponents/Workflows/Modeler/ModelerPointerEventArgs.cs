using Microsoft.AspNetCore.Components;

namespace Argent.WebComponents.Workflows.Modeler;

// Include the canvas origin with the event instead of querying JavaScript from
// .NET on every move. This also keeps the coordinates from the same DOM layout.
public sealed class ModelerPointerEventArgs : EventArgs
{
    public double ClientX { get; set; }
    public double ClientY { get; set; }
    public double CanvasLeft { get; set; }
    public double CanvasTop { get; set; }
}

[EventHandler("onmodelerpointermove", typeof(ModelerPointerEventArgs))]
public static class EventHandlers;
