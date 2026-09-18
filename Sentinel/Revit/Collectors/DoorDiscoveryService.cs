using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Sentinel.Core.Models;
using Sentinel.Core.Status;
using Sentinel.Infrastructure;
using Sentinel.UI.Services;

namespace Sentinel.Revit.Collectors
{
    /// <summary>Link listing, scoped door discovery and source-door verification for stored instances.</summary>
    internal static class DoorDiscoveryService
    {
        public static List<LinkInfo> GetLinks(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Select(li =>
                {
                    Document ld = null;
                    try { ld = li.GetLinkDocument(); } catch { }
                    var title = ld != null ? ld.Title : li.Name;
                    var path = "";
                    try { path = ld != null ? ld.PathName : ""; } catch { }
                    return new LinkInfo
                    {
                        UniqueId = li.UniqueId,
                        Name = li.Name,
                        DocumentTitle = title,
                        IsLoaded = ld != null,
                        IsIfc = (title ?? "").IndexOf(".ifc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (path ?? "").IndexOf(".ifc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (li.Name ?? "").IndexOf(".ifc", StringComparison.OrdinalIgnoreCase) >= 0
                    };
                })
                .OrderByDescending(l => l.IsLoaded)
                .ThenByDescending(l => l.IsIfc)
                .ThenBy(l => l.Name)
                .ToList();
        }

        public static List<string> GetLevels(Document doc, string linkUniqueId)
        {
            var link = doc.GetElement(linkUniqueId) as RevitLinkInstance;
            var ld = link?.GetLinkDocument();
            if (ld == null) return new List<string>();
            return new FilteredElementCollector(ld).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).Select(l => l.Name).ToList();
        }

        public static DiscoveryResult Discover(Document doc, View activeView, DiscoveryRequest req, SentinelSettings settings)
        {
            var result = new DiscoveryResult();
            var link = doc.GetElement(req.LinkUniqueId ?? "") as RevitLinkInstance;
            if (link == null)
            {
                result.Error = "Select a linked model first.";
                return result;
            }
            var linkDoc = link.GetLinkDocument();
            if (linkDoc == null)
            {
                result.Error = "The linked model \"" + link.Name + "\" is not loaded.";
                return result;
            }
            result.LinkName = link.Name;

            IList<Element> doors;
            if (req.Scope == DiscoveryScope.ActiveView && activeView != null)
            {
                try
                {
                    // Linked elements visible in the host view (Revit 2024+, see RevitCompat).
                    doors = new FilteredElementCollector(doc, activeView.Id, link.Id)
                        .OfCategory(BuiltInCategory.OST_Doors)
                        .WhereElementIsNotElementType()
                        .ToElements();
                }
                catch (Exception ex)
                {
                    result.Warnings.Add("Current-view scope is not available for view \"" + activeView.Name + "\" (" + ex.Message + "); all doors were collected.");
                    doors = AllDoors(linkDoc);
                }
            }
            else
            {
                doors = AllDoors(linkDoc);
            }

            if (req.Scope == DiscoveryScope.SelectedLevels && req.LevelNames != null && req.LevelNames.Count > 0)
            {
                var levelIds = new HashSet<long>(new FilteredElementCollector(linkDoc).OfClass(typeof(Level)).Cast<Level>()
                    .Where(l => req.LevelNames.Contains(l.Name)).Select(l => RevitCompat.IdValue(l.Id)));
                var reader0 = new DoorReader(doc, link, settings);
                doors = doors.Where(d =>
                {
                    if (RevitCompat.IsValid(d.LevelId)) return levelIds.Contains(RevitCompat.IdValue(d.LevelId));
                    // IFC elements without LevelId: fall back to the computed level name.
                    var dd = reader0.Read(d);
                    return dd.Current.LevelName != null && req.LevelNames.Contains(dd.Current.LevelName);
                }).ToList();
            }

            var reader = new DoorReader(doc, link, settings);
            var failed = 0;
            foreach (var d in doors)
            {
                try
                {
                    var dd = reader.Read(d);
                    result.Doors.Add(dd);
                }
                catch (Exception ex)
                {
                    failed++;
                    SentinelLog.Error("Reading door " + d.UniqueId + " failed", ex);
                }
            }
            if (failed > 0) result.Warnings.Add(failed + " door(s) could not be read (see log).");

            var noGeometry = result.Doors.Count(x => x.Geometry == null || !x.Geometry.IsValid);
            if (noGeometry > 0) result.Warnings.Add(noGeometry + " door(s) have unsupported geometry.");
            var estimated = result.Doors.Count(x => x.Geometry != null && x.Geometry.Source == DoorGeometrySource.EstimatedFromGeometry);
            if (estimated > 0) result.Warnings.Add(estimated + " door(s) use estimated IFC geometry (hinge side unknown).");

            result.Success = true;
            SentinelLog.Info("Discovered " + result.Doors.Count + " door(s) in " + link.Name + " (" + req.Scope + ").");
            return result;
        }

        private static IList<Element> AllDoors(Document linkDoc) =>
            new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().ToElements();

        // ------------------------------------------------------------------ source verification

        /// <summary>
        /// Resolves the stored source door of every instance (UniqueId → IFC GlobalId, link UniqueId → link title),
        /// reads its current state and compares it with the snapshot. Updates identity fields in place when a door
        /// or link was re-identified.
        /// </summary>
        public static Dictionary<string, SourceCheck> VerifySources(Document doc, IEnumerable<DoorSetInstance> instances, SentinelSettings settings)
        {
            var checks = new Dictionary<string, SourceCheck>();
            var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList();
            var readers = new Dictionary<string, DoorReader>();
            var guidMaps = new Dictionary<string, Dictionary<string, Element>>();

            foreach (var inst in instances)
            {
                var check = new SourceCheck { InstanceId = inst.Id };
                checks[inst.Id] = check;
                try
                {
                    var src = inst.Source;
                    var link = links.FirstOrDefault(l => l.UniqueId == src.LinkInstanceUniqueId);
                    if (link == null || link.GetLinkDocument() == null)
                    {
                        // Link removed and re-added: match by linked document title.
                        var byTitle = links.FirstOrDefault(l =>
                        {
                            var ld = l.GetLinkDocument();
                            return ld != null && !string.IsNullOrWhiteSpace(src.LinkDocumentTitle) &&
                                   string.Equals(ld.Title, src.LinkDocumentTitle, StringComparison.OrdinalIgnoreCase);
                        });
                        if (byTitle != null && byTitle != link)
                        {
                            link = byTitle;
                            src.LinkInstanceUniqueId = byTitle.UniqueId;
                            src.LinkName = byTitle.Name;
                            check.Reidentified = true;
                        }
                    }

                    var linkDoc = link?.GetLinkDocument();
                    if (linkDoc == null)
                    {
                        check.State = SourceState.LinkUnavailable;
                        continue;
                    }

                    var door = string.IsNullOrEmpty(src.DoorUniqueId) ? null : linkDoc.GetElement(src.DoorUniqueId);
                    if (door == null && !string.IsNullOrWhiteSpace(src.IfcGlobalId))
                    {
                        Dictionary<string, Element> map;
                        if (!guidMaps.TryGetValue(link.UniqueId, out map))
                        {
                            map = new Dictionary<string, Element>(StringComparer.OrdinalIgnoreCase);
                            foreach (var d in AllDoors(linkDoc))
                            {
                                var g = DoorReader.ReadIfcGuid(d);
                                if (!string.IsNullOrEmpty(g) && !map.ContainsKey(g)) map[g] = d;
                            }
                            guidMaps[link.UniqueId] = map;
                        }
                        map.TryGetValue(src.IfcGlobalId, out door);
                        if (door != null)
                        {
                            src.DoorUniqueId = door.UniqueId;
                            src.DoorElementId = RevitCompat.IdValue(door.Id);
                            check.Reidentified = true;
                        }
                    }

                    if (door == null)
                    {
                        check.State = SourceState.Missing;
                        continue;
                    }

                    DoorReader reader;
                    if (!readers.TryGetValue(link.UniqueId, out reader))
                    {
                        reader = new DoorReader(doc, link, settings);
                        readers[link.UniqueId] = reader;
                    }
                    check.Current = reader.Read(door);
                    check.Comparison = SourceChangeDetector.Compare(src, check.Current.Geometry, check.Current.Current.LastKnownParameters, settings);
                    check.State = check.Comparison.HasChanges ? SourceState.Changed : SourceState.Ok;
                }
                catch (Exception ex)
                {
                    SentinelLog.Error("Source verification failed for instance " + inst.Id, ex);
                    check.State = SourceState.NotChecked;
                }
            }
            return checks;
        }
    }
}
