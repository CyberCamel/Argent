using Argent.WebComponents.Workflows.Modeler.Interaction;

namespace Argent.WebComponents.Workflows.Modeler.Navigation;

/// <summary>
/// Zoom, pan and framing shared by the designer and the read-only instance overview, so
/// both react to the wheel the same way and both frame content identically.
/// </summary>
public static class CanvasNavigation
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 3.0;

    /// <summary>
    /// Zooms about the pointer so the world position under the cursor stays put. All
    /// zoom-dependent distances are expressed in screen pixels by the callers, so this
    /// does not change how a gesture feels between zoom levels.
    /// </summary>
    public static void ZoomToCursor(
        ModelerSession session, double clientX, double clientY,
        double rectLeft, double rectTop, double deltaY)
    {
        var before = session.ScreenToWorld(clientX, clientY, rectLeft, rectTop);
        double factor = Math.Pow(1.001, -deltaY);
        session.Zoom = Math.Clamp(session.Zoom * factor, MinZoom, MaxZoom);
        var after = session.ScreenToWorld(clientX, clientY, rectLeft, rectTop);
        session.PanX += before.X - after.X;
        session.PanY += before.Y - after.Y;
    }

    /// <summary>Frames every supplied rectangle, including pool and lane backgrounds.</summary>
    public static bool FitToContent(ModelerSession session, IEnumerable<GeometryEditor.Bounds> rects, double padding = 40)
    {
        var list = rects.ToList();
        if (list.Count == 0 || session.CanvasWidth <= 0 || session.CanvasHeight <= 0) return false;

        double minX = list.Min(r => r.X);
        double minY = list.Min(r => r.Y);
        double maxX = list.Max(r => r.Right);
        double maxY = list.Max(r => r.Bottom);

        double width = maxX - minX + padding * 2;
        double height = maxY - minY + padding * 2;
        if (width < 1 || height < 1) return false;

        session.Zoom = Math.Clamp(
            Math.Min(session.CanvasWidth / width, session.CanvasHeight / height), MinZoom, MaxZoom);
        session.PanX = minX - padding - (session.CanvasWidth / session.Zoom - width) / 2;
        session.PanY = minY - padding - (session.CanvasHeight / session.Zoom - height) / 2;
        return true;
    }

    /// <summary>Frames a specific area, used by "focus" and by validation navigation.</summary>
    public static bool Focus(ModelerSession session, GeometryEditor.Bounds bounds, double padding = 80)
    {
        if (bounds.Width <= 0 && bounds.Height <= 0) return false;
        return FitToContent(session, [bounds], padding);
    }
}
