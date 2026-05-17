using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Argent.Runtime.Workflows.Modeling;

public class DesignerSession
{
    public double Zoom { get; set; } = 1.0;
    public double PanX { get; set; } = 0;
    public double PanY { get; set; } = 0;
    public double ViewportWidth { get; set; } = 1920;
    public double ViewportHeight { get; set; } = 1080;

    public string ViewBox =>
        $"{PanX.ToString(CultureInfo.InvariantCulture)} " +
        $"{PanY.ToString(CultureInfo.InvariantCulture)} " +
        $"{(ViewportWidth / Zoom).ToString(CultureInfo.InvariantCulture)} " +
        $"{(ViewportHeight / Zoom).ToString(CultureInfo.InvariantCulture)}";

    public (double X, double Y) ScreenToWorld(double clientX, double clientY, double rectLeft, double rectTop) =>
        ((clientX - rectLeft) / Zoom + PanX, (clientY - rectTop) / Zoom + PanY);
}
