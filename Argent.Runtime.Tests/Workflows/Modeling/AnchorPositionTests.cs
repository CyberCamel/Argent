using System;
using Xunit;
using System.Collections.Generic;

namespace Argent.Logic.Tests.Workflows.Modeling
{
    public class AnchorPositionTests
    {
        [Theory]
        [InlineData(100, 50, 0, 0, 100, 50)] // World coords with no pan offset
        [InlineData(100, 50, 10, 20, 90, 30)] // World coords adjusted for pan offset
        [InlineData(100, 50, 50, 50, 50, 0)] // World coords with larger pan offset
        public void SVGCoordinatesForAnchor_AccountsForViewBoxOffset(double anchorX, double anchorY, double panX, double panY, double expectedSvgX, double expectedSvgY)
        {
            // CRITICAL: When the SVG viewBox includes PanX/PanY offset, 
            // to render at a world coordinate, we need to subtract the pan:
            // SVG coordinate = World coordinate - PanOffset
            double svgX = anchorX - panX;
            double svgY = anchorY - panY;

            Assert.Equal(expectedSvgX, svgX, 2);
            Assert.Equal(expectedSvgY, svgY, 2);
        }
    }
}


