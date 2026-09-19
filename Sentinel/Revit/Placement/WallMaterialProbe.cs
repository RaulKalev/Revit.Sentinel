using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Sentinel.Core.Geometry;
using Sentinel.Infrastructure;
using Sentinel.Revit.Geometry;

namespace Sentinel.Revit.Placement
{
    /// <summary>
    /// Measures wall material along a horizontal line through a point, in the host model and every loaded link (walls
    /// are often split across models: core wall in one, lining in another). Results are spans in mm along the given
    /// direction, relative to the point (see <see cref="WallClearance.ExitDistance"/>).
    /// </summary>
    internal sealed class WallMaterialProbe
    {
        private sealed class Source
        {
            public Document Doc;
            public Transform ToHost;
            public Transform ToLocal;
            public string Name;
        }

        private readonly List<Source> _sources = new List<Source>();
        private readonly Dictionary<string, List<Solid>> _solids = new Dictionary<string, List<Solid>>();

        public WallMaterialProbe(Document doc)
        {
            _sources.Add(new Source { Doc = doc, ToHost = Transform.Identity, ToLocal = Transform.Identity, Name = doc.Title });
            foreach (var li in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document ld = null;
                try { ld = li.GetLinkDocument(); } catch { }
                if (ld == null) continue;
                var t = li.GetTotalTransform();
                _sources.Add(new Source { Doc = ld, ToHost = t, ToLocal = t.Inverse, Name = li.Name });
            }
        }

        /// <summary>Wall material crossed by the line point ± direction × reach (mm along the direction, point = 0).</summary>
        public List<MaterialSpan> Spans(XYZ point, XYZ direction, double reachFt, List<string> hitSources = null)
        {
            var spans = new List<MaterialSpan>();
            var dirH = new XYZ(direction.X, direction.Y, 0);
            if (dirH.GetLength() < 1e-9) return spans;
            dirH = dirH.Normalize();

            foreach (var s in _sources)
            {
                var p = s.ToLocal.OfPoint(point);
                var d = s.ToLocal.OfVector(dirH).Normalize();
                var a = p - d * reachFt;
                var b = p + d * reachFt;
                const double pad = 0.05; // ft
                var outline = new Outline(
                    new XYZ(Math.Min(a.X, b.X) - pad, Math.Min(a.Y, b.Y) - pad, p.Z - pad),
                    new XYZ(Math.Max(a.X, b.X) + pad, Math.Max(a.Y, b.Y) + pad, p.Z + pad));

                IList<Element> walls;
                try
                {
                    walls = new FilteredElementCollector(s.Doc).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType()
                        .WherePasses(new BoundingBoxIntersectsFilter(outline)).ToElements();
                }
                catch (Exception ex)
                {
                    SentinelLog.Warn("Wall probe skipped " + s.Name + ": " + ex.Message);
                    continue;
                }
                if (walls.Count == 0) continue;

                var line = Line.CreateBound(a, b);
                var options = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
                foreach (var wall in walls)
                {
                    foreach (var solid in SolidsOf(s, wall))
                    {
                        SolidCurveIntersection hit;
                        try { hit = solid.IntersectWithCurve(line, options); }
                        catch { continue; }
                        if (hit == null) continue;
                        for (var i = 0; i < hit.SegmentCount; i++)
                        {
                            var seg = hit.GetCurveSegment(i);
                            var t0 = (seg.GetEndPoint(0) - p).DotProduct(d);
                            var t1 = (seg.GetEndPoint(1) - p).DotProduct(d);
                            spans.Add(new MaterialSpan(RevitUnits.FtToMm(t0), RevitUnits.FtToMm(t1)));
                            if (hitSources != null && !hitSources.Contains(s.Name)) hitSources.Add(s.Name);
                        }
                    }
                }
            }
            return spans;
        }

        private List<Solid> SolidsOf(Source s, Element e)
        {
            var key = s.Doc.GetHashCode() + ":" + e.Id;
            List<Solid> list;
            if (!_solids.TryGetValue(key, out list)) _solids[key] = list = GeometryUtils.GetSolids(e).ToList();
            return list;
        }
    }
}
