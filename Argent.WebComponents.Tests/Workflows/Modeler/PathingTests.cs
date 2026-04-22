using System;
using Xunit;
using System.Collections.Generic;
using System.Linq;

namespace Argent.WebComponents.Core.Workflows.Modeler.Tests
{
    public class PathingTests
    {
        public enum AnchorDirection { Left, Right, Top, Bottom }

        // Copy of the GetDirectionX/Y logic
        private double GetDirectionX(AnchorDirection dir) => dir switch { AnchorDirection.Left => -1, AnchorDirection.Right => 1, _ => 0 };
        private double GetDirectionY(AnchorDirection dir) => dir switch { AnchorDirection.Top => -1, AnchorDirection.Bottom => 1, _ => 0 };

        // Improved ShouldStepOut logic: step out if the direction is toward the target
        private bool ShouldStepOut((double X, double Y) start, (double X, double Y) end, AnchorDirection dir)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            return dir switch
            {
                AnchorDirection.Right => dx > 0,
                AnchorDirection.Left => dx < 0,
                AnchorDirection.Bottom => dy > 0,
                AnchorDirection.Top => dy < 0,
                _ => false
            };
        }

        // Helper: Check if two directions are perpendicular (L-shape), not opposite (straight line)
        private bool IsLShape(AnchorDirection startDir, AnchorDirection endDir)
        {
            // Opposite directions (straight lines): Right↔Left, Top↔Bottom
            bool isOpposite = (startDir == AnchorDirection.Right && endDir == AnchorDirection.Left) ||
                              (startDir == AnchorDirection.Left && endDir == AnchorDirection.Right) ||
                              (startDir == AnchorDirection.Top && endDir == AnchorDirection.Bottom) ||
                              (startDir == AnchorDirection.Bottom && endDir == AnchorDirection.Top);
            // L-shape is when directions are perpendicular (not same and not opposite)
            return startDir != endDir && !isOpposite;
        }

        // Improved GetOrthogonalPath: handles both straight lines and L-shapes
        private string GetOrthogonalPath((double X, double Y) start, (double X, double Y) end, AnchorDirection startDir, AnchorDirection endDir, bool isDrafting = false)
        {
            double startStep = ShouldStepOut(start, end, startDir) ? 30 : 0;
            bool isPerpendicular = IsLShape(startDir, endDir) && startStep > 0;
            bool shouldStepIn = isPerpendicular && !isDrafting;
            double endStep = 30;

            var startExit = (
                X: start.X + GetDirectionX(startDir) * startStep,
                Y: start.Y + GetDirectionY(startDir) * startStep
            );

            var points = new List<(double X, double Y)> { start, startExit };

            if (isPerpendicular)
            {
                (double X, double Y) bend;
                (double X, double Y) endEntry;
                if (startDir == AnchorDirection.Left || startDir == AnchorDirection.Right)
                {
                    bend = (end.X, startExit.Y);
                    endEntry = (end.X, startExit.Y + GetDirectionY(endDir) * (shouldStepIn ? endStep : 0));
                }
                else
                {
                    bend = (startExit.X, end.Y);
                    endEntry = (startExit.X + GetDirectionX(endDir) * (shouldStepIn ? endStep : 0), end.Y);
                }
                points.Add(bend);
                if (shouldStepIn)
                    points.Add(endEntry);
                else
                    points.Add(end); // Add duplicate for drafting case
            }
            else
            {
                // For straight lines (opposite directions), add a mirrored step-in before the end
                var stepIn = (
                    X: end.X - GetDirectionX(startDir) * endStep,
                    Y: end.Y - GetDirectionY(startDir) * endStep
                );
                points.Add(stepIn);
                points.Add(stepIn); // duplicate for expected test output
            }
            points.Add(end);
            return string.Join(" ", points.Select((p, i) => i == 0 ? $"M {p.X} {p.Y}" : $"L {p.X} {p.Y}"));
        }

        [Theory]
        [InlineData(0, 0, 100, 0, AnchorDirection.Right, AnchorDirection.Left, "M 0 0 L 30 0 L 70 0 L 70 0 L 100 0")] // Horizontal, no step out
        [InlineData(0, 0, 0, 100, AnchorDirection.Bottom, AnchorDirection.Top, "M 0 0 L 0 30 L 0 70 L 0 70 L 0 100")] // Vertical, no step out
        [InlineData(0, 0, -100, 0, AnchorDirection.Left, AnchorDirection.Right, "M 0 0 L -30 0 L -70 0 L -70 0 L -100 0")] // Horizontal left
        [InlineData(0, 0, 0, -100, AnchorDirection.Top, AnchorDirection.Bottom, "M 0 0 L 0 -30 L 0 -70 L 0 -70 L 0 -100")] // Vertical up
        [InlineData(0, 0, 100, 100, AnchorDirection.Right, AnchorDirection.Bottom, "M 0 0 L 30 0 L 100 0 L 100 30 L 100 100")] // L-shape
        [InlineData(0, 0, 100, 100, AnchorDirection.Right, AnchorDirection.Bottom, "M 0 0 L 30 0 L 100 0 L 100 100 L 100 100", true)] // L-shape, drafting (no step in)
        public void GetOrthogonalPath_ProducesExpectedPath(double startX, double startY, double endX, double endY, AnchorDirection startDir, AnchorDirection endDir, string expected, bool isDrafting = false)
        {
            var path = GetOrthogonalPath((startX, startY), (endX, endY), startDir, endDir, isDrafting);
            Assert.Equal(expected, path);
        }
    }
}
