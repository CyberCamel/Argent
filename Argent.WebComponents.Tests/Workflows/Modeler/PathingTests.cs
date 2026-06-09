using System;
using Xunit;
using System.Collections.Generic;
using System.Linq;

namespace Argent.WebComponents.Core.Workflows.Modeler.Tests
{
    public class PathingTests
    {
        // --- Helpers ---

        public enum AnchorDirection { Left, Right, Top, Bottom }
        private double GetDirDx(AnchorDirection dir) => dir switch
        {
            AnchorDirection.Right => 1, AnchorDirection.Left => -1, _ => 0
        };
        private double GetDirDy(AnchorDirection dir) => dir switch
        {
            AnchorDirection.Bottom => 1, AnchorDirection.Top => -1, _ => 0
        };
        private bool IsHorizontal(AnchorDirection dir) =>
            dir is AnchorDirection.Left or AnchorDirection.Right;

        private List<(double X, double Y)> AutoRoute(
            (double X, double Y) source, AnchorDirection sourceDir,
            (double X, double Y) target, AnchorDirection targetDir,
            double offset = 40)
        {
            var (sx, sy) = source; var (tx, ty) = target;
            bool sH = IsHorizontal(sourceDir), tH = IsHorizontal(targetDir);
            if (sH && tH) { double mx = sx + offset * GetDirDx(sourceDir); return [(sx, sy), (mx, sy), (mx, ty), (tx, ty)]; }
            if (!sH && !tH) { double my = sy + offset * GetDirDy(sourceDir); return [(sx, sy), (sx, my), (tx, my), (tx, ty)]; }
            if (sH) return [(sx, sy), (tx, sy), (tx, ty)];
            return [(sx, sy), (sx, ty), (tx, ty)];
        }

        private string WaypointsToSvgPath(List<(double X, double Y)> wps)
        {
            if (wps.Count < 2) return "";
            var parts = new List<string> { $"M {wps[0].X} {wps[0].Y}" };
            for (int i = 1; i < wps.Count; i++) parts.Add($"L {wps[i].X} {wps[i].Y}");
            return string.Join(" ", parts);
        }

        private const double Eps = 0.001;
        private bool SharesY(double a, double b) => Math.Abs(a - b) < Eps;

        // --- FindNearestSegment (simulated) ---

        private int FindNearestSegment(List<(double X, double Y)> wps, double mx, double my, double threshold = 20.0)
        {
            double bestDist = threshold;
            int bestIdx = -1;
            for (int i = 0; i < wps.Count - 1; i++)
            {
                double dist;
                if (Math.Abs(wps[i].Y - wps[i + 1].Y) < Eps)
                {
                    double minX = Math.Min(wps[i].X, wps[i + 1].X);
                    double maxX = Math.Max(wps[i].X, wps[i + 1].X);
                    dist = Math.Abs(my - wps[i].Y);
                    if (dist < bestDist)
                    {
                        double xTol = threshold * 0.5;
                        if (mx >= minX - xTol && mx <= maxX + xTol) { bestDist = dist; bestIdx = i; }
                    }
                }
                else
                {
                    double minY = Math.Min(wps[i].Y, wps[i + 1].Y);
                    double maxY = Math.Max(wps[i].Y, wps[i + 1].Y);
                    dist = Math.Abs(mx - wps[i].X);
                    if (dist < bestDist)
                    {
                        double yTol = threshold * 0.5;
                        if (my >= minY - yTol && my <= maxY + yTol) { bestDist = dist; bestIdx = i; }
                    }
                }
            }
            return bestIdx;
        }

        // --- DragSegment (simulated) ---

        private bool DragSegment(List<(double X, double Y)> wps, int i,
            ref double sx, ref double sy, double wx, double wy)
        {
            int n = wps.Count;
            if (i <= 0 || i >= n - 2) return false;
            double dx = wx - sx, dy = wy - sy;
            var a = wps[i]; var b = wps[i + 1];
            if (Math.Abs(a.Y - b.Y) < Eps)
            {
                wps[i] = (a.X, a.Y + dy);
                wps[i + 1] = (b.X, b.Y + dy);
            }
            else
            {
                wps[i] = (a.X + dx, a.Y);
                wps[i + 1] = (b.X + dx, b.Y);
            }
            sx = wx; sy = wy; return true;
        }

        // --- DragWaypoint (simulated) ---

        private void DragWaypoint(List<(double X, double Y)> wps, int i, double wx, double wy)
        {
            int n = wps.Count;
            if (i <= 0 || i >= n - 1) return;
            var old = wps.ToArray();
            wps[i] = (wx, wy);
            if (i > 0)
            {
                if (Math.Abs(old[i].X - old[i - 1].X) < Eps)
                {
                    if (i - 1 == 0) { var cur = wps[i]; wps[i] = (wps[0].X, cur.Y); }
                    else { var cur = wps[i - 1]; wps[i - 1] = (wx, cur.Y); }
                }
                else if (Math.Abs(old[i].Y - old[i - 1].Y) < Eps)
                {
                    if (i - 1 == 0) { var cur = wps[i]; wps[i] = (cur.X, wps[0].Y); }
                    else { var cur = wps[i - 1]; wps[i - 1] = (cur.X, wy); }
                }
            }
            if (i < n - 1)
            {
                if (Math.Abs(old[i].X - old[i + 1].X) < Eps)
                {
                    if (i + 1 == n - 1) { var cur = wps[i]; wps[i] = (wps[n - 1].X, cur.Y); }
                    else { var cur = wps[i + 1]; wps[i + 1] = (wx, cur.Y); }
                }
                else if (Math.Abs(old[i].Y - old[i + 1].Y) < Eps)
                {
                    if (i + 1 == n - 1) { var cur = wps[i]; wps[i] = (cur.X, wps[n - 1].Y); }
                    else { var cur = wps[i + 1]; wps[i + 1] = (cur.X, wy); }
                }
            }
        }

        // --- InsertWaypointAtMidpoint (simulated) ---

        private int InsertWaypoint(List<(double X, double Y)> wps, int segIdx)
        {
            var a = wps[segIdx]; var b = wps[segIdx + 1];
            wps.Insert(segIdx + 1, ((a.X + b.X) / 2, (a.Y + b.Y) / 2));
            return segIdx + 1;
        }

        // =================================================================
        // AutoRoute tests
        // =================================================================

        [Theory]
        [InlineData(0, 0, 100, 0, AnchorDirection.Right, AnchorDirection.Left,
            "M 0 0 L 40 0 L 40 0 L 100 0")]
        [InlineData(0, 0, 0, 100, AnchorDirection.Bottom, AnchorDirection.Top,
            "M 0 0 L 0 40 L 0 40 L 0 100")]
        [InlineData(0, 0, 100, 100, AnchorDirection.Right, AnchorDirection.Bottom,
            "M 0 0 L 100 0 L 100 100")]
        [InlineData(0, 0, 100, 100, AnchorDirection.Bottom, AnchorDirection.Left,
            "M 0 0 L 0 100 L 100 100")]
        [InlineData(0, 0, -100, -100, AnchorDirection.Left, AnchorDirection.Right,
            "M 0 0 L -40 0 L -40 -100 L -100 -100")]
        public void AutoRoute_ProducesExpectedPath(
            double sx, double sy, double tx, double ty,
            AnchorDirection sDir, AnchorDirection tDir, string expected)
        {
            var wps = AutoRoute((sx, sy), sDir, (tx, ty), tDir);
            Assert.Equal(expected, WaypointsToSvgPath(wps));
        }

        [Theory]
        [InlineData(0, 0, 100, 0, AnchorDirection.Right, AnchorDirection.Left, 4)]
        [InlineData(0, 0, 0, 100, AnchorDirection.Top, AnchorDirection.Bottom, 4)]
        [InlineData(0, 0, 100, 100, AnchorDirection.Right, AnchorDirection.Bottom, 3)]
        [InlineData(0, 0, 100, 100, AnchorDirection.Bottom, AnchorDirection.Left, 3)]
        [InlineData(100, 100, 0, 0, AnchorDirection.Right, AnchorDirection.Left, 4)]
        [InlineData(100, 100, 0, 0, AnchorDirection.Top, AnchorDirection.Bottom, 4)]
        public void AutoRoute_CorrectWaypointCount(
            double sx, double sy, double tx, double ty,
            AnchorDirection sDir, AnchorDirection tDir, int expectedCount)
        {
            Assert.Equal(expectedCount, AutoRoute((sx, sy), sDir, (tx, ty), tDir).Count);
        }

        [Fact]
        public void AutoRoute_ConsecutivePairsShareAxis()
        {
            var cases = new[] {
                ((0,0), (100,0), AnchorDirection.Right, AnchorDirection.Left),
                ((0,0), (0,100), AnchorDirection.Top, AnchorDirection.Bottom),
                ((0,0), (100,100), AnchorDirection.Right, AnchorDirection.Bottom),
                ((50,50), (200,150), AnchorDirection.Right, AnchorDirection.Left),
                ((50,50), (200,150), AnchorDirection.Bottom, AnchorDirection.Top),
            };
            foreach (var (s, t, sd, td) in cases)
            {
                var wps = AutoRoute(s, sd, t, td);
                for (int i = 0; i < wps.Count - 1; i++)
                {
                    bool shareX = Math.Abs(wps[i].X - wps[i + 1].X) < 0.001;
                    bool shareY = Math.Abs(wps[i].Y - wps[i + 1].Y) < 0.001;
                    Assert.True(shareX || shareY,
                        $"Pair {i}-{i+1}: ({wps[i].X},{wps[i].Y})→({wps[i+1].X},{wps[i+1].Y}) shares neither");
                }
            }
        }

        // =================================================================
        // FindNearestSegment tests
        // =================================================================

        [Fact]
        public void FindNearestSegment_FindsMiddleOfHorizontal()
        {
            Assert.Equal(0, FindNearestSegment([(0, 50), (100, 50)], 50, 50));
        }

        [Fact]
        public void FindNearestSegment_FindsMiddleOfVertical()
        {
            Assert.Equal(0, FindNearestSegment([(50, 0), (50, 100)], 50, 50));
        }

        [Fact]
        public void FindNearestSegment_NearEndpoint_WithTolerance()
        {
            // 8px past endpoint (108 > 100), threshold=20 → xTol=10, so 108 ≤ 110 ✓
            Assert.Equal(0, FindNearestSegment([(0, 50), (100, 50)], 108, 48, threshold: 20));
        }

        [Fact]
        public void FindNearestSegment_FarFromLine_ReturnsMinusOne()
        {
            // my=0, segment y=50, dist=50 > threshold=20
            Assert.Equal(-1, FindNearestSegment([(0, 50), (100, 50)], 50, 0, threshold: 20));
        }

        [Fact]
        public void FindNearestSegment_PicksClosestHorizontal()
        {
            // 4wp H V H: (0,50)→(40,50)→(40,200)→(300,200)
            // Click at (20, 55) — dist to H seg 0 = 5, dist to V seg 1 = 20, H wins
            List<(double X, double Y)> wps = [(0, 50), (40, 50), (40, 200), (300, 200)];
            Assert.Equal(0, FindNearestSegment(wps, 20, 55, threshold: 20));
        }

        [Fact]
        public void FindNearestSegment_PicksClosestVertical()
        {
            // Click at (42, 100) — dist to V seg 1 = 2, dist to H seg 0 = 50 → V wins
            List<(double X, double Y)> wps = [(0, 50), (40, 50), (40, 200), (300, 200)];
            Assert.Equal(1, FindNearestSegment(wps, 42, 100, threshold: 20));
        }

        [Fact]
        public void FindNearestSegment_ZeroLengthSegment_StillHittable()
        {
            // Degenerate segment: both waypoints at same position, but tolerance still registers a hit
            Assert.Equal(0, FindNearestSegment([(40, 50), (40, 50)], 40, 52, threshold: 20));
        }

        [Fact]
        public void FindNearestSegment_FourWaypoint_MiddleVertical()
        {
            Assert.Equal(1, FindNearestSegment([(0, 50), (40, 50), (40, 200), (300, 200)], 42, 100, threshold: 20));
        }

        [Fact]
        public void FindNearestSegment_FourWaypoint_FirstHorizontal()
        {
            Assert.Equal(0, FindNearestSegment([(0, 50), (40, 50), (40, 200), (300, 200)], 20, 52, threshold: 20));
        }

        [Fact]
        public void FindNearestSegment_FourWaypoint_LastHorizontal()
        {
            Assert.Equal(2, FindNearestSegment([(0, 50), (40, 50), (40, 200), (300, 200)], 150, 202, threshold: 20));
        }

        [Fact]
        public void FindNearestSegment_NearCorner_BothMatch_PicksNearest()
        {
            // Click at (42, 54): 
            //   Seg 0 (H at y=50): dist=|54-50|=4, x bounds [0,40]+tol → 42 ≤ 50 ✓
            //   Seg 1 (V at x=40): dist=|42-40|=2, y bounds [50,200]+tol → 54 ≥ 40 ✓
            // Seg 1 wins (dist=2 < 4)
            Assert.Equal(1, FindNearestSegment([(0, 50), (40, 50), (40, 200), (300, 200)], 42, 54, threshold: 20));
        }

        // =================================================================
        // DragSegment tests
        // =================================================================

        [Fact]
        public void DragSegment_InteriorVertical_TranslatesX()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (40, 50), (40, 200), (300, 200) };
            double sx = 0, sy = 0;
            Assert.True(DragSegment(wps, 1, ref sx, ref sy, 20, 0));
            Assert.Equal(60, wps[1].X); // 40 + 20
            Assert.Equal(60, wps[2].X); // 40 + 20
            Assert.Equal(20, sx);
            Assert.Equal(0, sy);
        }

        [Fact]
        public void DragSegment_InteriorHorizontalInVHV_TranslatesY()
        {
            // V-H-V path: (50,0)→(50,40)→(200,40)→(200,100)
            var wps = new List<(double X, double Y)> { (50, 0), (50, 40), (200, 40), (200, 100) };
            double sx = 0, sy = 0;
            Assert.True(DragSegment(wps, 1, ref sx, ref sy, 0, 30));
            Assert.Equal(70, wps[1].Y); // 40 + 30
            Assert.Equal(70, wps[2].Y); // 40 + 30
        }

        [Fact]
        public void DragSegment_FirstSegment_ReturnsFalse()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (40, 50), (40, 200), (300, 200) };
            double sx = 0, sy = 0;
            Assert.False(DragSegment(wps, 0, ref sx, ref sy, 10, 10));
        }

        [Fact]
        public void DragSegment_LastSegment_ReturnsFalse()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (40, 50), (40, 200), (300, 200) };
            double sx = 0, sy = 0;
            Assert.False(DragSegment(wps, 2, ref sx, ref sy, 10, 10));
        }

        [Fact]
        public void DragSegment_ThreeWaypoint_AllAdjacentToAnchor_ReturnsFalse()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (100, 50), (100, 100) };
            double sx = 0, sy = 0;
            Assert.False(DragSegment(wps, 0, ref sx, ref sy, 10, 10));
            Assert.False(DragSegment(wps, 1, ref sx, ref sy, 10, 10));
        }

        [Fact]
        public void DragSegment_OutOfRange_ReturnsFalse()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (40, 50), (40, 200), (300, 200) };
            double sx = 0, sy = 0;
            Assert.False(DragSegment(wps, -1, ref sx, ref sy, 10, 10));
            Assert.False(DragSegment(wps, 5, ref sx, ref sy, 10, 10));
        }

        // =================================================================
        // DragWaypoint tests
        // =================================================================

        [Fact]
        public void DragWaypoint_AnchorWaypoint_DoesNothing()
        {
            var orig = new List<(double X, double Y)> { (0, 50), (40, 50), (40, 200), (300, 200) };
            var wps = new List<(double X, double Y)> { (0, 50), (40, 50), (40, 200), (300, 200) };
            DragWaypoint(wps, 0, 99, 99);
            DragWaypoint(wps, 3, 99, 99);
            Assert.Equal(orig, wps);
        }

        [Fact]
        public void DragWaypoint_InteriorWaypoint_MaintainsSharedCoords()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (40, 50), (40, 200), (300, 200) };
            DragWaypoint(wps, 1, 80, 50);
            Assert.Equal(wps[0].Y, wps[1].Y, precision: 4);  // share Y
            Assert.Equal(wps[1].X, wps[2].X, precision: 4);  // share X
        }

        [Fact]
        public void DragWaypoint_DragSecondInteriorWaypoint()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (40, 50), (40, 200), (300, 200) };
            DragWaypoint(wps, 2, 40, 150); // move wp[2] up (Y: 200→150)
            // wp[2] anchored to wp[3].Y = 200, so Y constrained back
            Assert.Equal(wps[3].Y, wps[2].Y, precision: 4);
            Assert.Equal(wps[1].X, wps[2].X, precision: 4);
        }

        [Fact]
        public void DragWaypoint_ThreeWaypoint_DragCorner()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (100, 50), (100, 100) };
            DragWaypoint(wps, 1, 150, 50);
            Assert.Equal(50, wps[1].Y, precision: 4); // Y fixed by wp[0] (share Y)
            Assert.Equal(wps[1].X, wps[2].X, precision: 4); // share X
        }

        [Fact]
        public void DragWaypoint_AllConsecutivePairsShareAxisAfterDrag()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (40, 50), (40, 200), (300, 200) };
            DragWaypoint(wps, 1, 77, 88);
            for (int i = 0; i < wps.Count - 1; i++)
            {
                bool shareX = Math.Abs(wps[i].X - wps[i + 1].X) < 0.001;
                bool shareY = Math.Abs(wps[i].Y - wps[i + 1].Y) < 0.001;
                Assert.True(shareX || shareY,
                    $"Pair {i}-{i+1}: ({wps[i].X},{wps[i].Y})→({wps[i+1].X},{wps[i+1].Y})");
            }
        }

        // =================================================================
        // InsertWaypointAtMidpoint tests
        // =================================================================

        [Fact]
        public void InsertWaypoint_InsertsAtMidpoint()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (100, 50) };
            int idx = InsertWaypoint(wps, 0);
            Assert.Equal(3, wps.Count);
            Assert.Equal(50, wps[1].X);
            Assert.Equal(50, wps[1].Y);
            Assert.Equal(1, idx);
        }

        [Fact]
        public void InsertWaypoint_InFourWaypointPath()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (40, 50), (40, 200), (300, 200) };
            int idx = InsertWaypoint(wps, 2); // last horizontal segment
            Assert.Equal(5, wps.Count);
            Assert.Equal(170, wps[3].X); // (40+300)/2 = 170
            Assert.Equal(200, wps[3].Y);
            Assert.Equal(3, idx);
        }

        [Fact]
        public void InsertWaypoint_ThreeWaypointPath()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (100, 50), (100, 100) };
            int idx = InsertWaypoint(wps, 0);
            Assert.Equal(4, wps.Count);
            Assert.Equal(50, wps[1].X);
            Assert.Equal(50, wps[1].Y);
            Assert.Equal(1, idx);
        }

        [Fact]
        public void InsertWaypoint_OutOfRange_Throws()
        {
            var wps = new List<(double X, double Y)> { (0, 50), (100, 50) };
            Assert.Throws<ArgumentOutOfRangeException>(() => InsertWaypoint(wps, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => InsertWaypoint(wps, 1));
        }
    }
}
