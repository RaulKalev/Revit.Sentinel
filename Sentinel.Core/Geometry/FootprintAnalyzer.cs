using System;
using System.Collections.Generic;
using System.Linq;

namespace Sentinel.Core.Geometry
{
    /// <summary>Oriented rectangle in plan.</summary>
    public class FootprintRectangle
    {
        public double CenterX { get; set; }
        public double CenterY { get; set; }

        /// <summary>Unit direction of the long side (door width direction).</summary>
        public Vec3 LongAxis { get; set; }

        /// <summary>Unit direction of the short side (door normal / wall thickness direction).</summary>
        public Vec3 ShortAxis { get; set; }

        public double LongLength { get; set; }
        public double ShortLength { get; set; }
    }

    /// <summary>
    /// Estimates a door frame from plan points of its geometry (used for IFC doors that come in as DirectShapes
    /// without FacingOrientation/HandOrientation): minimum-area bounding rectangle of the convex hull.
    /// The short axis sign is canonicalised so the result is deterministic for the same geometry.
    /// </summary>
    public static class FootprintAnalyzer
    {
        public static FootprintRectangle MinimumAreaRectangle(IEnumerable<Vec3> points)
        {
            var pts = (points ?? Enumerable.Empty<Vec3>())
                .Where(p => !double.IsNaN(p.X) && !double.IsNaN(p.Y))
                .Select(p => new Vec3(p.X, p.Y, 0))
                .ToList();
            if (pts.Count < 3) return null;

            var hull = ConvexHull(pts);
            if (hull.Count < 3) return null;

            FootprintRectangle best = null;
            var bestArea = double.MaxValue;

            for (int i = 0; i < hull.Count; i++)
            {
                var a = hull[i];
                var b = hull[(i + 1) % hull.Count];
                var edge = (b - a).Normalize();
                if (edge.IsZero) continue;
                var perp = new Vec3(-edge.Y, edge.X, 0);

                double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
                foreach (var p in hull)
                {
                    var u = p.Dot(edge);
                    var v = p.Dot(perp);
                    if (u < minU) minU = u;
                    if (u > maxU) maxU = u;
                    if (v < minV) minV = v;
                    if (v > maxV) maxV = v;
                }

                var area = (maxU - minU) * (maxV - minV);
                if (area < bestArea - 1e-6)
                {
                    bestArea = area;
                    var cu = (minU + maxU) / 2.0;
                    var cv = (minV + maxV) / 2.0;
                    var center = edge * cu + perp * cv;
                    var lu = maxU - minU;
                    var lv = maxV - minV;
                    best = lu >= lv
                        ? new FootprintRectangle { CenterX = center.X, CenterY = center.Y, LongAxis = edge, ShortAxis = perp, LongLength = lu, ShortLength = lv }
                        : new FootprintRectangle { CenterX = center.X, CenterY = center.Y, LongAxis = perp, ShortAxis = edge, LongLength = lv, ShortLength = lu };
                }
            }

            if (best == null) return null;
            best.ShortAxis = Canonical(best.ShortAxis);
            // Right-handed frame: long axis = Z × short axis (keeps hinge/latch naming stable).
            best.LongAxis = Vec3.UnitZ.Cross(best.ShortAxis).Normalize();
            return best;
        }

        /// <summary>Chooses a deterministic sign: plan angle in [-90°, 90°).</summary>
        private static Vec3 Canonical(Vec3 v)
        {
            v = v.Normalize();
            if (v.X < -1e-9 || (Math.Abs(v.X) <= 1e-9 && v.Y < 0)) v = -v;
            return v;
        }

        /// <summary>Andrew's monotone chain; returns hull in counter-clockwise order.</summary>
        public static List<Vec3> ConvexHull(List<Vec3> points)
        {
            var pts = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (pts.Count < 3) return pts;

            var lower = new List<Vec3>();
            foreach (var p in pts)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 1e-9) lower.RemoveAt(lower.Count - 1);
                lower.Add(p);
            }
            var upper = new List<Vec3>();
            for (int i = pts.Count - 1; i >= 0; i--)
            {
                var p = pts[i];
                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 1e-9) upper.RemoveAt(upper.Count - 1);
                upper.Add(p);
            }
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        private static double Cross(Vec3 o, Vec3 a, Vec3 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
    }
}
