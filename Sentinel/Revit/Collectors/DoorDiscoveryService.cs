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

        public static List<string> GetLevels(Document doc, IEnumerable<string> linkUniqueIds)
        {
            // Union across links, ordered by elevation (the first link that has a level decides its position).
            var byName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var uid in linkUniqueIds ?? Enumerable.Empty<string>())
            {
                var link = doc.GetElement(uid ?? "") as RevitLinkInstance;
                var ld = link?.GetLinkDocument();
                if (ld == null) continue;
                foreach (var l in new FilteredElementCollector(ld).OfClass(typeof(Level)).Cast<Level>())
                    if (!byName.ContainsKey(l.Name)) byName[l.Name] = l.Elevation;
            }
            return byName.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
        }

        /// <summary>
        /// Collects doors from every requested link. Unloaded or removed links are reported as warnings; the call
        /// only fails when none of the links can be read. Doors that appear in more than one link are marked.
        /// </summary>
        public static DiscoveryResult Discover(Document doc, View activeView, DiscoveryRequest req, SentinelSettings settings)
        {
            var result = new DiscoveryResult();
            var linkIds = (req.LinkUniqueIds ?? new List<string>()).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            if (linkIds.Count == 0)
            {
                result.Error = "Select at least one linked model first.";
                return result;
            }

            var names = new List<string>();
            var failed = 0;
            foreach (var uid in linkIds)
            {
                var link = doc.GetElement(uid) as RevitLinkInstance;
                if (link == null)
                {
                    result.Warnings.Add("A selected linked model is no longer in the project.");
                    continue;
                }
                var linkDoc = link.GetLinkDocument();
                if (linkDoc == null)
                {
                    result.Warnings.Add("\"" + link.Name + "\" is not loaded.");
                    continue;
                }
                names.Add(LinkInfo.Short(link.Name));

                var doors = CollectDoors(doc, activeView, link, linkDoc, req, result.Warnings);
                if (req.SkipWindowTypes)
                {
                    var before = doors.Count;
                    doors = doors.Where(d => !IsWindowType(linkDoc, d)).ToList();
                    result.SkippedWindowTypes += before - doors.Count;
                }

                var reader = new DoorReader(doc, link, settings);
                if (req.Scope == DiscoveryScope.SelectedLevels && req.LevelNames != null && req.LevelNames.Count > 0)
                {
                    var levelIds = new HashSet<long>(new FilteredElementCollector(linkDoc).OfClass(typeof(Level)).Cast<Level>()
                        .Where(l => req.LevelNames.Contains(l.Name)).Select(l => RevitCompat.IdValue(l.Id)));
                    doors = doors.Where(d =>
                    {
                        if (RevitCompat.IsValid(d.LevelId)) return levelIds.Contains(RevitCompat.IdValue(d.LevelId));
                        // IFC elements without LevelId: fall back to the computed level name.
                        var dd = reader.Read(d);
                        return dd.Current.LevelName != null && req.LevelNames.Contains(dd.Current.LevelName);
                    }).ToList();
                }

                var count = 0;
                foreach (var d in doors)
                {
                    try
                    {
                        result.Doors.Add(reader.Read(d));
                        count++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        SentinelLog.Error("Reading door " + d.UniqueId + " failed", ex);
                    }
                }
                result.CountsByLink.Add(LinkInfo.Short(link.Name) + ": " + count);
                SentinelLog.Info("Discovered " + count + " door(s) in " + link.Name + " (" + req.Scope + ").");
            }

            if (names.Count == 0)
            {
                result.Error = result.Warnings.Count > 0 ? string.Join(" ", result.Warnings) : "The selected linked models could not be read.";
                return result;
            }
            result.LinkName = string.Join(" + ", names);

            result.PossibleDuplicates = CrossLinkDuplicates.Mark(result.Doors);
            foreach (var d in result.Doors.Where(x => x.PossibleDuplicateOf != null))
                d.ReadWarnings.Add("Probably the same door as " + d.PossibleDuplicateOf + ".");

            if (failed > 0) result.Warnings.Add(failed + " door(s) could not be read (see log).");
            if (result.SkippedWindowTypes > 0) result.Warnings.Add(result.SkippedWindowTypes + " window type(s) in the Doors category were left out.");
            var noGeometry = result.Doors.Count(x => x.Geometry == null || !x.Geometry.IsValid);
            if (noGeometry > 0) result.Warnings.Add(noGeometry + " door(s) have unsupported geometry.");
            var estimated = result.Doors.Count(x => x.Geometry != null && x.Geometry.Source == DoorGeometrySource.EstimatedFromGeometry);
            var fromHandle = result.Doors.Count(x => x.Geometry != null && x.Geometry.HingeFromHandle);
            if (fromHandle > 0) result.Warnings.Add(fromHandle + " IFC door(s): hinge side read from the door handle.");
            if (estimated - fromHandle > 0) result.Warnings.Add((estimated - fromHandle) + " IFC door(s): hinge side unknown, assumed (check the preview).");

            result.Success = true;
            return result;
        }

        private static List<Element> CollectDoors(Document doc, View activeView, RevitLinkInstance link, Document linkDoc,
            DiscoveryRequest req, List<string> warnings)
        {
            if (req.Scope == DiscoveryScope.ActiveView && activeView != null)
            {
                try
                {
                    // Linked elements visible in the host view (Revit 2024+, see RevitCompat).
                    return new FilteredElementCollector(doc, activeView.Id, link.Id)
                        .OfCategory(BuiltInCategory.OST_Doors)
                        .WhereElementIsNotElementType()
                        .ToElements()
                        .ToList();
                }
                catch (Exception ex)
                {
                    warnings.Add("Current-view scope is not available for view \"" + activeView.Name + "\" (" + ex.Message + "); all doors of " + link.Name + " were collected.");
                }
            }
            return AllDoors(linkDoc).ToList();
        }

        /// <summary>IFC exports sometimes put windows in the Doors category ("Window 27", Estonian "Aken").</summary>
        private static bool IsWindowType(Document linkDoc, Element door)
        {
            var typeName = (linkDoc.GetElement(door.GetTypeId())?.Name ?? "").TrimStart();
            return typeName.StartsWith("Window", StringComparison.OrdinalIgnoreCase) ||
                   typeName.StartsWith("Aken", StringComparison.OrdinalIgnoreCase);
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
