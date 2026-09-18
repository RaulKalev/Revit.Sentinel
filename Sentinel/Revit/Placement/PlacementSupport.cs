using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Sentinel.Core.Geometry;
using Sentinel.Infrastructure;

namespace Sentinel.Revit.Placement
{
    /// <summary>Finds family types by (family name, type name) with a per-operation cache.</summary>
    internal sealed class FamilySymbolResolver
    {
        private readonly Document _doc;
        private static readonly string Sep = ((char)31).ToString(); // unit separator: cannot occur in names
        private Dictionary<string, FamilySymbol> _map;

        public FamilySymbolResolver(Document doc) { _doc = doc; }

        public FamilySymbol Find(string family, string type)
        {
            if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(type)) return null;
            if (_map == null)
            {
                _map = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                {
                    if (s.Family == null) continue;
                    var key = s.Family.Name + Sep + s.Name;
                    if (!_map.ContainsKey(key)) _map[key] = s;
                }
            }
            FamilySymbol sym;
            return _map.TryGetValue(family + Sep + type, out sym) ? sym : null;
        }
    }

    internal static class LevelFinder
    {
        /// <summary>Highest level at or below z (feet); lowest level when z is below all levels.</summary>
        public static Level BelowOrLowest(Document doc, double zFt)
        {
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
            if (levels.Count == 0) return null;
            return levels.LastOrDefault(l => l.Elevation <= zFt + 1e-3) ?? levels.First();
        }
    }

    /// <summary>Position/plan rotation of a placed instance, read the same way at placement and at refresh.</summary>
    internal static class ElementPose
    {
        public static bool TryRead(Element e, out Vec3 positionMm, out double rotationDeg)
        {
            positionMm = Vec3.Zero;
            rotationDeg = 0;
            var lp = e?.Location as LocationPoint;
            if (lp == null) return false;
            positionMm = RevitUnits.ToVec3Mm(lp.Point);

            var fi = e as FamilyInstance;
            var hand = fi?.HandOrientation;
            if (hand != null && Math.Abs(hand.Z) < 0.99 && hand.GetLength() > 1e-9)
                rotationDeg = RevitUnits.ToVec3Dir(hand).PlanAngleDeg();
            else
            {
                try { rotationDeg = Angles.Normalize360(lp.Rotation * 180.0 / Math.PI); } catch { rotationDeg = 0; }
            }
            return true;
        }
    }

    /// <summary>
    /// Deletes warnings (collected for reporting) and rolls back on errors, so a failing door never leaves
    /// half-created elements or modal Revit dialogs behind.
    /// </summary>
    internal sealed class CollectingFailuresPreprocessor : IFailuresPreprocessor
    {
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var hasError = false;
            foreach (var f in accessor.GetFailureMessages())
            {
                var text = f.GetDescriptionText();
                if (f.GetSeverity() == FailureSeverity.Warning)
                {
                    Warnings.Add(text);
                    accessor.DeleteWarning(f);
                }
                else
                {
                    Errors.Add(text);
                    hasError = true;
                }
            }
            return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }

        public static void Attach(Transaction t, CollectingFailuresPreprocessor pre)
        {
            var opts = t.GetFailureHandlingOptions();
            opts.SetFailuresPreprocessor(pre);
            opts.SetClearAfterRollback(true);
            opts.SetForcedModalHandling(false);
            t.SetFailureHandlingOptions(opts);
        }
    }

    /// <summary>Helper 3D views used for navigation and wall-face raycasting (created once per project).</summary>
    internal static class HelperViews
    {
        public const string FocusViewName = "Sentinel Focus";
        public const string RaycastViewName = "Sentinel Placement (do not edit)";

        public static View3D Find(Document doc, string name) =>
            new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Returns the view, creating it in its own transaction if needed. Must not be called inside an open transaction.</summary>
        public static View3D Ensure(Document doc, string name, bool sectionBox)
        {
            var existing = Find(doc, name);
            if (existing != null) return existing;

            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);
            if (vft == null) throw new InvalidOperationException("No 3D view type exists in the project.");

            using (var t = new Transaction(doc, "Sentinel – create helper view"))
            {
                t.Start();
                var v = View3D.CreateIsometric(doc, vft.Id);
                v.Name = name;
                try { v.ViewTemplateId = ElementId.InvalidElementId; } catch { }
                v.DetailLevel = ViewDetailLevel.Medium;
                try { v.DisplayStyle = DisplayStyle.ShadingWithEdges; } catch { }
                v.IsSectionBoxActive = sectionBox;
                t.Commit();
                SentinelLog.Info("Created helper view '" + name + "'.");
                return v;
            }
        }
    }

    /// <summary>Finds the linked (or host) wall face behind a calculated point.</summary>
    internal sealed class WallFaceFinder
    {
        private readonly ReferenceIntersector _intersector;

        public WallFaceFinder(View3D view)
        {
            _intersector = new ReferenceIntersector(new ElementCategoryFilter(BuiltInCategory.OST_Walls), FindReferenceTarget.Face, view)
            {
                FindReferencesInRevitLinks = true
            };
        }

        /// <summary>
        /// Casts a horizontal ray from outside the wall (point + normal * search) towards the wall and returns the
        /// first wall face within reach. <paramref name="outwardNormal"/> points from the wall to the device side.
        /// </summary>
        public bool TryFind(XYZ point, XYZ outwardNormal, double searchFt, double extraFt, out Reference face, out XYZ pointOnFace)
        {
            face = null;
            pointOnFace = null;
            var n = new XYZ(outwardNormal.X, outwardNormal.Y, 0).Normalize();
            var origin = point + n * searchFt;
            IList<ReferenceWithContext> hits;
            try { hits = _intersector.Find(origin, -n); }
            catch (Exception ex)
            {
                SentinelLog.Error("Wall raycast failed", ex);
                return false;
            }
            if (hits == null) return false;

            var maxReach = searchFt * 2 + Math.Max(0, extraFt);
            var best = hits.Where(h => h != null && h.Proximity > 1e-4 && h.Proximity <= maxReach)
                .OrderBy(h => h.Proximity).FirstOrDefault();
            if (best == null) return false;

            face = best.GetReference();
            pointOnFace = origin - n * best.Proximity;
            return face != null;
        }
    }
}
