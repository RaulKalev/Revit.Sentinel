using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Xunit;

namespace Sentinel.Core.Tests
{
    /// <summary>
    /// Synthetic IFC-style doors in the door frame (mm): X across the width from the centre, Y through the wall,
    /// Z up. 1000 mm wide, 2100 mm high, 200 mm lining, 40 mm leaf. Pieces are boxes given by their corners only
    /// (like straight solid edges read from Revit).
    /// </summary>
    public class HandleDetectorTests
    {
        private static IList<Vec3> BoxPts(double x0, double x1, double y0, double y1, double z0, double z1)
        {
            var pts = new List<Vec3>();
            foreach (var x in new[] { x0, x1 })
                foreach (var y in new[] { y0, y1 })
                    foreach (var z in new[] { z0, z1 })
                        pts.Add(new Vec3(x, y, z));
            return pts;
        }

        /// <summary>Linings, leaf and optional extra pieces.</summary>
        private static List<IList<Vec3>> Door(params IList<Vec3>[] extra)
        {
            var pieces = new List<IList<Vec3>>
            {
                BoxPts(-500, -460, -100, 100, 0, 2100), // lining left
                BoxPts(460, 500, -100, 100, 0, 2100),   // lining right
                BoxPts(-500, 500, -100, 100, 2060, 2100),// head
                BoxPts(-458, 458, -20, 20, 0, 2055)     // leaf
            };
            pieces.AddRange(extra);
            return pieces;
        }

        /// <summary>Lever handles on both faces, near the given edge (+1 = +X).</summary>
        private static IList<Vec3>[] Levers(int side) => new[]
        {
            BoxPts(side * 300, side * 430, 20, 85, 1000, 1040),
            BoxPts(side * 300, side * 430, -85, -20, 1000, 1040),
            BoxPts(side * 370, side * 420, 20, 30, 950, 1150),   // rosette / escutcheon (within 25 mm → not a handle by itself)
        };

        [Fact]
        public void HandleOnPlusSide_MeansHingeOnMinusSide()
        {
            var r = HandleDetector.Detect(Door(Levers(+1)));
            Assert.Equal(HingeSide.NegativeWidthAxis, r.Hinge);
            Assert.True(r.ProtrusionMm >= 60);
            Assert.Contains("+width", r.Note);
        }

        [Fact]
        public void HandleOnMinusSide_MeansHingeOnPlusSide()
        {
            Assert.Equal(HingeSide.PositiveWidthAxis, HandleDetector.Detect(Door(Levers(-1))).Hinge);
        }

        [Fact]
        public void MiddleHingeAndDoorCloser_OnTheHingeSide_AreNotMistakenForAHandle()
        {
            var pieces = Door(Levers(+1));
            pieces.Add(BoxPts(-470, -452, 20, 34, 1000, 1100));   // middle hinge knuckle (14 mm)
            pieces.Add(BoxPts(-440, -100, 20, 130, 1950, 2050));  // overhead door closer
            Assert.Equal(HingeSide.NegativeWidthAxis, HandleDetector.Detect(pieces).Hinge);
        }

        [Fact]
        public void DoubleDoor_WithHandlesInTheMiddle_StaysUnknown()
        {
            var pieces = new List<IList<Vec3>>
            {
                BoxPts(-500, -460, -100, 100, 0, 2100),
                BoxPts(460, 500, -100, 100, 0, 2100),
                BoxPts(-458, -2, -20, 20, 0, 2055),
                BoxPts(2, 458, -20, 20, 0, 2055),
                BoxPts(-120, -20, 20, 85, 1000, 1040),
                BoxPts(20, 120, 20, 85, 1000, 1040)
            };
            var r = HandleDetector.Detect(pieces);
            Assert.Equal(HingeSide.Unknown, r.Hinge);
            Assert.Contains("both halves", r.Note);
        }

        [Fact]
        public void PanicBarAcrossTheDoor_StaysUnknown()
        {
            var r = HandleDetector.Detect(Door(BoxPts(-400, 400, 20, 90, 980, 1060)));
            Assert.Equal(HingeSide.Unknown, r.Hinge);
        }

        [Fact]
        public void DoorWithoutHandle_StaysUnknown()
        {
            var r = HandleDetector.Detect(Door());
            Assert.Equal(HingeSide.Unknown, r.Hinge);
            Assert.Contains("no handle", r.Note);
        }

        [Fact]
        public void HandleMergedIntoTheLeafPiece_IsFoundFromItsPoints()
        {
            var leafWithHandle = BoxPts(-458, 458, -20, 20, 0, 2055).Concat(BoxPts(300, 430, -85, 85, 1000, 1040)).ToList();
            var pieces = new List<IList<Vec3>>
            {
                BoxPts(-500, -460, -100, 100, 0, 2100),
                BoxPts(460, 500, -100, 100, 0, 2100),
                leafWithHandle
            };
            Assert.Equal(HingeSide.NegativeWidthAxis, HandleDetector.Detect(pieces).Hinge);
        }

        [Fact]
        public void WholeDoorAsOnePiece_NeverGuesses()
        {
            var one = Door(Levers(+1)).SelectMany(p => p).ToList();
            var r = HandleDetector.Detect(one);
            // Either decided correctly or left unknown with a reason – never the wrong side.
            Assert.NotEqual(HingeSide.PositiveWidthAxis, r.Hinge);
            Assert.False(string.IsNullOrEmpty(r.Note));
        }

        [Fact]
        public void TooLittleGeometry_StaysUnknown()
        {
            Assert.Equal(HingeSide.Unknown, HandleDetector.Detect(new List<Vec3> { new Vec3(0, 0, 0) }).Hinge);
        }
    }
}
