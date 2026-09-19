using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sentinel.Core.Models;

namespace Sentinel.Core.Geometry
{
    /// <summary>Result of looking for the door handle in the door's own geometry.</summary>
    public class HandleDetection
    {
        /// <summary>Hinge side implied by the handle (the handle is on the latch side); Unknown when not decided.</summary>
        public HingeSide Hinge { get; set; } = HingeSide.Unknown;

        public bool Found => Hinge != HingeSide.Unknown;

        /// <summary>Geometry pieces (or points) recognised as handle.</summary>
        public int HandleParts { get; set; }

        /// <summary>How far the handle sticks out past the leaf (mm).</summary>
        public double ProtrusionMm { get; set; }

        /// <summary>Handle position along the width from the door centre (mm, signed along the width axis).</summary>
        public double HandleOffsetMm { get; set; }

        /// <summary>Why the hinge was or was not decided (for the log and the review panel).</summary>
        public string Note { get; set; }
    }

    /// <summary>
    /// Finds the hinge side of a door from its handle. IFC doors often come without orientation data, but their
    /// geometry usually includes the handles as part of the door: at handle height they stick out past the door
    /// leaf, near the opening (latch) edge – so the hinge is on the other side.
    ///
    /// Input: the door's geometry pieces (one per solid / mesh; IFC doors usually keep leaf, lining and handles as
    /// separate items), each as points in the door's own frame (mm): X along the width axis from the door centre,
    /// Y along the facing direction, Z above the door bottom.
    ///  - The leaf is the largest thin piece that spans most of the height.
    ///  - Handle pieces are small, sit at handle height and stick out at least <see cref="MinProtrusionMm"/> past
    ///    the leaf faces. Hinges (knuckles stick out ~10 mm) and door closers (above the handle band) do not count.
    ///  - All handle pieces must be on one half of the door, away from the middle: double doors (handles in the
    ///    middle) and panic bars (across the door) stay Unknown.
    /// If the door is a single piece, handle points are looked for directly against the leaf faces measured
    /// away from handle height; when those cannot be separated the result is Unknown with a reason – never a guess.
    /// </summary>
    public static class HandleDetector
    {
        public const double HandleBandLowMm = 750;
        public const double HandleBandHighMm = 1350;
        public const double MinProtrusionMm = 25;

        private sealed class Box
        {
            public double MinX = double.MaxValue, MaxX = double.MinValue, MinY = double.MaxValue, MaxY = double.MinValue,
                MinZ = double.MaxValue, MaxZ = double.MinValue;

            // Thickness away from handle height: a leaf with a built-in handle still reads as a thin leaf.
            public double BodyMinY = double.MaxValue, BodyMaxY = double.MinValue;
            public double BodySizeY => BodyMaxY >= BodyMinY ? BodyMaxY - BodyMinY : SizeY;
            public double SizeX => MaxX - MinX;
            public double SizeY => MaxY - MinY;
            public double SizeZ => MaxZ - MinZ;
            public double CenterX => (MinX + MaxX) / 2.0;
            public double CenterZ => (MinZ + MaxZ) / 2.0;

            public void Add(Vec3 p)
            {
                if (p.X < MinX) MinX = p.X;
                if (p.X > MaxX) MaxX = p.X;
                if (p.Y < MinY) MinY = p.Y;
                if (p.Y > MaxY) MaxY = p.Y;
                if (p.Z < MinZ) MinZ = p.Z;
                if (p.Z > MaxZ) MaxZ = p.Z;
                if (p.Z < HandleBandLowMm - 100 || p.Z > HandleBandHighMm + 100)
                {
                    if (p.Y < BodyMinY) BodyMinY = p.Y;
                    if (p.Y > BodyMaxY) BodyMaxY = p.Y;
                }
            }
        }

        private static bool Valid(Vec3 p) => !double.IsNaN(p.X) && !double.IsNaN(p.Y) && !double.IsNaN(p.Z);

        /// <summary>Convenience overload: the whole door as one piece.</summary>
        public static HandleDetection Detect(IList<Vec3> localPoints) => Detect(new List<IList<Vec3>> { localPoints });

        public static HandleDetection Detect(IList<IList<Vec3>> pieces)
        {
            var result = new HandleDetection();
            var groups = (pieces ?? new List<IList<Vec3>>())
                .Where(g => g != null)
                .Select(g => g.Where(Valid).ToList())
                .Where(g => g.Count > 0)
                .ToList();
            var all = groups.SelectMany(g => g).ToList();
            if (all.Count < 8)
            {
                result.Note = "too little door geometry to look for a handle";
                return result;
            }

            var door = new Box();
            foreach (var p in all) door.Add(p);
            var width = door.SizeX;
            var height = door.MaxZ;
            if (width < 400 || height < 1500)
            {
                result.Note = "door geometry too small to look for a handle";
                return result;
            }

            var boxes = groups.Select(g => { var b = new Box(); foreach (var p in g) b.Add(p); return b; }).ToList();
            var centreX = door.CenterX;
            var half = width / 2.0;

            // ---- separate pieces: find the leaf, then small pieces sticking out of it at handle height ----
            var leaf = boxes
                .Where(b => b.SizeX >= 0.35 * width && b.SizeZ >= 0.6 * height && b.BodySizeY <= 120 && b.BodyMaxY >= b.BodyMinY)
                .OrderByDescending(b => b.SizeX * b.SizeZ)
                .FirstOrDefault();
            if (leaf != null)
            {
                var faceMin = leaf.BodyMinY;
                var faceMax = leaf.BodyMaxY;
                var handles = boxes.Where(b => b != leaf &&
                                               b.SizeZ <= 700 && b.CenterZ >= HandleBandLowMm && b.CenterZ <= HandleBandHighMm &&
                                               b.SizeX <= 0.45 * width &&
                                               (b.MaxY > faceMax + MinProtrusionMm || b.MinY < faceMin - MinProtrusionMm))
                                   .ToList();
                if (handles.Count > 0)
                {
                    result.HandleParts = handles.Count;
                    result.ProtrusionMm = handles.Max(b => Math.Max(b.MaxY - faceMax, faceMin - b.MinY));
                    return Decide(result, handles.Select(b => b.CenterX - centreX).ToList(), half);
                }

                // Handle merged into the leaf or another piece: look at the points against the leaf faces.
                var points = all.Where(p => p.Z >= HandleBandLowMm && p.Z <= HandleBandHighMm &&
                                            p.X > leaf.MinX && p.X < leaf.MaxX &&
                                            (p.Y > faceMax + MinProtrusionMm || p.Y < faceMin - MinProtrusionMm))
                                .ToList();
                if (points.Count >= 3)
                {
                    result.HandleParts = points.Count;
                    result.ProtrusionMm = points.Max(p => Math.Max(p.Y - faceMax, faceMin - p.Y));
                    return Decide(result, points.Select(p => p.X - centreX).ToList(), half);
                }
                result.Note = "no handle sticks out of the door leaf";
                return result;
            }

            // ---- one piece: leaf faces from points away from handle height, inside the frame ----
            var innerMin = door.MinX + 0.15 * width;
            var innerMax = door.MaxX - 0.15 * width;
            var upperTop = Math.Min(1900, height * 0.85); // below any door closer
            var reference = all.Where(p => p.X > innerMin && p.X < innerMax &&
                                           ((p.Z < HandleBandLowMm - 100) || (p.Z > HandleBandHighMm + 100 && p.Z <= upperTop)))
                               .ToList();
            if (reference.Count < 4)
            {
                result.Note = "door geometry is one piece and its leaf faces could not be separated from the frame";
                return result;
            }
            var leafMin = reference.Min(p => p.Y);
            var leafMax = reference.Max(p => p.Y);
            var candidates = all.Where(p => p.Z >= HandleBandLowMm && p.Z <= HandleBandHighMm &&
                                            p.X > door.MinX + 0.05 * width && p.X < door.MaxX - 0.05 * width &&
                                            (p.Y > leafMax + MinProtrusionMm || p.Y < leafMin - MinProtrusionMm))
                                .ToList();
            if (candidates.Count < 3)
            {
                result.Note = "no handle sticks out of the door leaf";
                return result;
            }
            result.HandleParts = candidates.Count;
            result.ProtrusionMm = candidates.Max(p => Math.Max(p.Y - leafMax, leafMin - p.Y));
            return Decide(result, candidates.Select(p => p.X - centreX).ToList(), half);
        }

        /// <summary>Latch side = the half with the handle; ambiguous (double door, panic bar, centred) stays Unknown.</summary>
        private static HandleDetection Decide(HandleDetection result, List<double> offsets, double half)
        {
            result.HandleOffsetMm = offsets.Average();
            var positive = offsets.Count(u => u > 0.1 * half);
            var negative = offsets.Count(u => u < -0.1 * half);
            var farEnough = Math.Abs(result.HandleOffsetMm) > 0.35 * half;
            var inv = CultureInfo.InvariantCulture;
            var protrusion = result.ProtrusionMm.ToString("0", inv);

            if (farEnough && positive >= 0.8 * offsets.Count)
            {
                result.Hinge = HingeSide.NegativeWidthAxis;
                result.Note = "handle " + result.HandleOffsetMm.ToString("0", inv) + " mm towards +width, sticks out " + protrusion + " mm";
            }
            else if (farEnough && negative >= 0.8 * offsets.Count)
            {
                result.Hinge = HingeSide.PositiveWidthAxis;
                result.Note = "handle " + (-result.HandleOffsetMm).ToString("0", inv) + " mm towards -width, sticks out " + protrusion + " mm";
            }
            else
            {
                result.Note = positive > 0 && negative > 0
                    ? "handles on both halves (double door or panic bar)"
                    : "handle too close to the door centre to tell the side";
            }
            return result;
        }
    }
}
