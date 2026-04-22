using System;
using Xunit;
using Argent.Logic.Workflows.Modeling;

namespace Argent.Logic.Tests.Workflows.Modeling
{
    public class DesignerSessionTests
    {
        [Theory]
        [InlineData(100, 100, 0, 0, 1.0, 100, 100)] // No pan/zoom
        [InlineData(200, 150, 10, 20, 1.0, 190, 130)] // Pan only: (200-0)/1 - 10 = 190 ✓
        [InlineData(300, 200, 0, 0, 2.0, 150, 100)] // Zoom only: 300/2 - 0 = 150 ✓
        [InlineData(400, 300, 50, 50, 2.0, 150, 100)] // Pan + Zoom: 400/2 - 50 = 150 ✓
        public void ScreenToWorld_ProducesExpectedResults(double clientX, double clientY, double panX, double panY, double zoom, double expectedX, double expectedY)
        {
            var session = new DesignerSession
            {
                PanX = panX,
                PanY = panY,
                Zoom = zoom
            };
            // Assume canvas top-left at (0,0)
            var (worldX, worldY) = session.ScreenToWorld(clientX, clientY, 0, 0);
            Assert.Equal(expectedX, worldX, 2);
            Assert.Equal(expectedY, worldY, 2);
        }

        [Theory]
        [InlineData(100, 100, 0, 0, 1.0, 100, 100)]
        [InlineData(190, 130, 10, 20, 1.0, 200, 150)] // Pan only: (190+10)*1 = 200 ✓
        [InlineData(150, 100, 0, 0, 2.0, 300, 200)] // Zoom only: 150*2 + 0 = 300 ✓
        [InlineData(150, 100, 50, 50, 2.0, 400, 300)] // Pan + Zoom: (150+50)*2 = 400 ✓
        public void WorldToScreen_ProducesExpectedResults(double worldX, double worldY, double panX, double panY, double zoom, double expectedClientX, double expectedClientY)
        {
            var session = new DesignerSession
            {
                PanX = panX,
                PanY = panY,
                Zoom = zoom
            };
            // Assume canvas top-left at (0,0)
            var (clientX, clientY) = session.WorldToScreen(worldX, worldY, 0, 0);
            Assert.Equal(expectedClientX, clientX, 2);
            Assert.Equal(expectedClientY, clientY, 2);
        }

        [Theory]
        [InlineData(100, 100, 0, 0, 1.0)] // No pan/zoom
        [InlineData(200, 150, 10, 20, 1.0)] // Pan only
        [InlineData(300, 200, 0, 0, 2.0)] // Zoom only
        [InlineData(400, 300, 50, 50, 2.0)] // Pan + Zoom
        public void RoundTrip_ScreenToWorldToScreen_ReturnsSamePoint(double clientX, double clientY, double panX, double panY, double zoom)
        {
            var session = new DesignerSession
            {
                PanX = panX,
                PanY = panY,
                Zoom = zoom
            };

            // Convert screen -> world -> screen and verify we get the same screen coordinate
            var (worldX, worldY) = session.ScreenToWorld(clientX, clientY, 0, 0);
            var (backToClientX, backToClientY) = session.WorldToScreen(worldX, worldY, 0, 0);

            Assert.Equal(clientX, backToClientX, 2);
            Assert.Equal(clientY, backToClientY, 2);
        }
    }
}
