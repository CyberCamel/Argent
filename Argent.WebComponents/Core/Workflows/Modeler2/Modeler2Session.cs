using System.Globalization;

namespace Argent.WebComponents.Core.Workflows.Modeler2;

public sealed class Modeler2Session
{
    public double Zoom { get; set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public double CanvasWidth { get; set; } = 1920;
    public double CanvasHeight { get; set; } = 1080;

    public string ViewBox =>
        $"{PanX.ToString(CultureInfo.InvariantCulture)} " +
        $"{PanY.ToString(CultureInfo.InvariantCulture)} " +
        $"{(CanvasWidth / Zoom).ToString(CultureInfo.InvariantCulture)} " +
        $"{(CanvasHeight / Zoom).ToString(CultureInfo.InvariantCulture)}";

    public (double X, double Y) ScreenToWorld(double clientX, double clientY, double rectLeft, double rectTop) =>
        ((clientX - rectLeft) / Zoom + PanX, (clientY - rectTop) / Zoom + PanY);
}
