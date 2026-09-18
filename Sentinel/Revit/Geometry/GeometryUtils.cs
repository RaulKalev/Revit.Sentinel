using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Sentinel.Revit.Geometry
{
    /// <summary>Revit geometry helpers (all in the element's own document coordinates, feet).</summary>
    internal static class GeometryUtils
    {
        public static IEnumerable<Solid> GetSolids(Element e)
        {
            var result = new List<Solid>();
            if (e == null) return result;
            GeometryElement geo = null;
            try { geo = e.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine }); }
            catch { }
            if (geo == null) return result;
            Collect(geo, result, null);
            return result;
        }

        private static void Collect(GeometryElement geo, List<Solid> solids, List<XYZ> points)
        {
            foreach (var obj in geo)
            {
                var s = obj as Solid;
                if (s != null && s.Volume > 1e-9)
                {
                    solids?.Add(s);
                    if (points != null) AddSolidPoints(s, points);
                    continue;
                }
                var m = obj as Mesh;
                if (m != null && points != null)
                {
                    for (int i = 0; i < m.Vertices.Count; i++) points.Add(m.Vertices[i]);
                    continue;
                }
                var gi = obj as GeometryInstance;
                if (gi != null)
                {
                    GeometryElement inst = null;
                    try { inst = gi.GetInstanceGeometry(); } catch { }
                    if (inst != null) Collect(inst, solids, points);
                }
            }
        }

        private static void AddSolidPoints(Solid s, List<XYZ> points)
        {
            foreach (Edge edge in s.Edges)
            {
                IList<XYZ> tess = null;
                try { tess = edge.Tessellate(); } catch { }
                if (tess != null) points.AddRange(tess);
            }
        }

        /// <summary>Vertices of all solids/meshes of an element (for footprint estimation).</summary>
        public static List<XYZ> GetPoints(Element e)
        {
            var points = new List<XYZ>();
            if (e == null) return points;
            GeometryElement geo = null;
            try { geo = e.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine }); }
            catch { }
            if (geo != null) Collect(geo, null, points);
            return points;
        }

        /// <summary>
        /// Measures the wall around a point along a horizontal normal using the wall's planar side faces.
        /// Returns the offset from the point to the wall centre plane and the thickness (feet).
        /// </summary>
        public static bool MeasureWall(Element wall, XYZ point, XYZ normal, out double centerOffset, out double thickness)
        {
            centerOffset = 0;
            thickness = 0;
            if (wall == null || point == null || normal == null) return false;

            double pos = double.MaxValue, neg = double.MinValue;
            const double maxReach = 5.0; // ft (~1.5 m)

            foreach (var solid in GetSolids(wall))
            {
                foreach (Face f in solid.Faces)
                {
                    var pf = f as PlanarFace;
                    if (pf == null) continue;
                    if (Math.Abs(pf.FaceNormal.DotProduct(normal)) < 0.99) continue;

                    var d = (pf.Origin - point).DotProduct(normal);
                    if (Math.Abs(d) > maxReach) continue;
                    if (d > 1e-4 && d < pos) pos = d;
                    if (d < -1e-4 && d > neg) neg = d;
                }
            }

            if (pos == double.MaxValue || neg == double.MinValue) return false;
            thickness = pos - neg;
            centerOffset = (pos + neg) / 2.0;
            return thickness > 1e-3;
        }

        public static BoundingBoxXYZ GetBox(Element e)
        {
            try { return e?.get_BoundingBox(null); } catch { return null; }
        }

        /// <summary>Axis-aligned box of a (possibly linked) element in host coordinates.</summary>
        public static BoundingBoxXYZ TransformBox(BoundingBoxXYZ box, Transform t)
        {
            if (box == null) return null;
            if (t == null || t.IsIdentity) return box;
            var corners = new[]
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Min.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Min.Z),
                new XYZ(box.Min.X, box.Min.Y, box.Max.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Max.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            }.Select(t.OfPoint).ToList();
            return new BoundingBoxXYZ
            {
                Min = new XYZ(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z)),
                Max = new XYZ(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z))
            };
        }

        public static BoundingBoxXYZ Union(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return new BoundingBoxXYZ
            {
                Min = new XYZ(Math.Min(a.Min.X, b.Min.X), Math.Min(a.Min.Y, b.Min.Y), Math.Min(a.Min.Z, b.Min.Z)),
                Max = new XYZ(Math.Max(a.Max.X, b.Max.X), Math.Max(a.Max.Y, b.Max.Y), Math.Max(a.Max.Z, b.Max.Z))
            };
        }

        public static BoundingBoxXYZ Inflate(BoundingBoxXYZ b, double ft) => b == null ? null : new BoundingBoxXYZ
        {
            Min = new XYZ(b.Min.X - ft, b.Min.Y - ft, b.Min.Z - ft),
            Max = new XYZ(b.Max.X + ft, b.Max.Y + ft, b.Max.Z + ft)
        };
    }
}
