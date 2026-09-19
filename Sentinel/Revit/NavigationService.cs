using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Sentinel.Core.Geometry;
using Sentinel.Core.Placement;
using Sentinel.Infrastructure;
using Sentinel.Revit.Geometry;
using Sentinel.UI.Services;

namespace Sentinel.Revit
{
    /// <summary>Select / zoom helpers for doors and set components.</summary>
    internal static class NavigationService
    {
        public static OperationResult Navigate(UIDocument uidoc, NavigationRequest request, Func<BoundingBoxXYZ, string> focus)
        {
            var doc = uidoc.Document;
            var elements = (request.ElementUniqueIds ?? new List<string>())
                .Select(uid => string.IsNullOrEmpty(uid) ? null : doc.GetElement(uid))
                .Where(e => e != null)
                .ToList();

            switch (request.Kind)
            {
                case NavigationKind.SelectComponents:
                    if (elements.Count == 0) return OperationResult.Fail("No placed components exist for this set.");
                    var ids = elements.Select(e => e.Id).ToList();
                    uidoc.Selection.SetElementIds(ids);
                    try { uidoc.ShowElements(ids); } catch { }
                    return OperationResult.Ok(ids.Count + " component(s) selected.");

                case NavigationKind.SelectSourceDoor:
                    {
                        var link = string.IsNullOrEmpty(request.Source?.LinkInstanceUniqueId) ? null : doc.GetElement(request.Source.LinkInstanceUniqueId) as RevitLinkInstance;
                        var linkDoc = link?.GetLinkDocument();
                        var door = linkDoc != null && !string.IsNullOrEmpty(request.Source.DoorUniqueId) ? linkDoc.GetElement(request.Source.DoorUniqueId) : null;
                        if (door == null) return OperationResult.Fail("Source door not found in the linked model.");
                        try
                        {
                            var reference = new Reference(door).CreateLinkReference(link);
                            uidoc.Selection.SetReferences(new List<Reference> { reference });
                        }
                        catch (Exception ex)
                        {
                            SentinelLog.Warn("Linked selection failed: " + ex.Message);
                        }
                        var note = focus(DoorBox(request.Geometry, null) ?? GeometryUtils.TransformBox(GeometryUtils.GetBox(door), link.GetTotalTransform()));
                        return OperationResult.Ok("Source door selected (linked element)." + (note != null ? " " + note : ""));
                    }

                default:
                    {
                        var box = DoorBox(request.Geometry, null);
                        foreach (var e in elements) box = GeometryUtils.Union(box, GeometryUtils.GetBox(e));
                        if (box == null) return OperationResult.Fail("The door location is unknown.");
                        var zoomNote = focus(box);
                        if (elements.Count > 0)
                        {
                            try { uidoc.Selection.SetElementIds(elements.Select(e => e.Id).ToList()); } catch { }
                        }
                        return OperationResult.Ok(zoomNote);
                    }
            }
        }

        /// <summary>Box around a door (opening + wall + reach of the placed/preview components), host feet.</summary>
        public static BoundingBoxXYZ DoorBox(DoorGeometry g, IEnumerable<CalculatedPlacement> placements)
        {
            if (g == null || !g.IsValid) return null;
            var w = g.WidthAxis.Normalize();
            var n = g.Facing.Normalize();
            var pts = new List<Vec3>();
            foreach (var sw in new[] { -1.0, 1.0 })
                foreach (var sn in new[] { -1.0, 1.0 })
                    foreach (var sz in new[] { 0.0, 1.0 })
                        pts.Add(g.Origin + w * (sw * g.WidthMm / 2.0) + n * (sn * (g.WallThicknessMm / 2.0 + 300)) + Vec3.UnitZ * (sz * g.HeightMm));
            if (placements != null) pts.AddRange(placements.Select(p => p.Position));

            return new BoundingBoxXYZ
            {
                Min = RevitUnits.ToXyzFt(new Vec3(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z))),
                Max = RevitUnits.ToXyzFt(new Vec3(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z)))
            };
        }
    }
}
