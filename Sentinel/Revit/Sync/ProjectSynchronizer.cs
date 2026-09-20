using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.Core.Status;
using Sentinel.Infrastructure;
using Sentinel.Revit.Collectors;
using Sentinel.Revit.Placement;
using Sentinel.Revit.Storage;
using Sentinel.UI.Services;

namespace Sentinel.Revit.Sync
{
    /// <summary>
    /// Brings the in-memory project in line with the model without modifying any Revit element:
    ///  - verifies source doors (moved / missing / re-identified),
    ///  - checks placed components (missing / manually modified),
    ///  - rebuilds the relationship graph from element tags (relink, duplicates, recovery).
    /// Returns whether persisted data changed (identity updates, recovered sets) so the caller can save.
    /// </summary>
    internal static class ProjectSynchronizer
    {
        public static RefreshResult Refresh(Document doc, SentinelProject project, out bool persistedDataChanged)
        {
            persistedDataChanged = false;
            var settings = project.Settings ?? new SentinelSettings();
            var result = new RefreshResult();

            // ---- element tags ----
            var allTags = ElementTagStorage.ReadAll(doc);
            var ownTags = allTags.Where(kv => string.IsNullOrEmpty(kv.Value.ProjectId) || kv.Value.ProjectId == project.ProjectId).ToList();
            result.UntrackedElements = allTags.Count - ownTags.Count;
            var byComponent = ownTags.GroupBy(kv => kv.Value.ComponentId ?? "")
                .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());

            // ---- recover sets that exist only as tagged elements ----
            var knownSets = new HashSet<string>(project.DoorSetInstances.Select(i => i.Id));
            var recovered = new List<DoorSetInstance>();
            foreach (var group in ownTags.Where(kv => !string.IsNullOrEmpty(kv.Value.SetId) && !knownSets.Contains(kv.Value.SetId))
                                         .GroupBy(kv => kv.Value.SetId))
            {
                var first = group.First().Value;
                var inst = new DoorSetInstance
                {
                    Id = first.SetId,
                    // A definition deleted since then shows up as an Error status on the recovered set.
                    DefinitionId = first.DefinitionId,
                    Source = new SourceDoorReference
                    {
                        LinkInstanceUniqueId = first.SourceLinkUniqueId,
                        DoorUniqueId = first.SourceDoorUniqueId,
                        IfcGlobalId = first.SourceIfcGlobalId
                    },
                    Notes = "Recovered from element tags on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ".",
                    ReviewState = ReviewState.NeedsReview
                };
                foreach (var kv in group.GroupBy(x => x.Value.ComponentId).Select(g => g.First()))
                {
                    Vec3 pos;
                    double rot;
                    ElementPose.TryRead(kv.Key, out pos, out rot);
                    inst.Components.Add(new PlacedComponentInstance
                    {
                        Id = kv.Value.ComponentId,
                        SlotKey = kv.Value.SlotKey,
                        ComponentDefinitionId = kv.Value.ComponentDefinitionId,
                        Label = project.FindComponent(kv.Value.ComponentDefinitionId)?.Name ?? "Component",
                        ElementUniqueId = kv.Key.UniqueId,
                        ElementId = RevitCompat.IdValue(kv.Key.Id),
                        State = ComponentState.Placed,
                        PlacedPosition = pos,
                        PlacedRotationDeg = rot,
                        CalculatedPosition = pos,
                        CalculatedRotationDeg = rot
                    });
                }
                recovered.Add(inst);
            }
            if (recovered.Count > 0)
            {
                project.DoorSetInstances.AddRange(recovered);
                result.RecoveredSets = recovered.Count;
                persistedDataChanged = true;
                result.Messages.Add(recovered.Count + " set(s) recovered from element tags – review them.");
                SentinelLog.Warn("Recovered " + recovered.Count + " set(s) from element tags.");
            }

            // ---- sources ----
            result.Sources = DoorDiscoveryService.VerifySources(doc, project.DoorSetInstances, settings);
            if (result.Sources.Values.Any(s => s.Reidentified))
            {
                persistedDataChanged = true;
                result.Messages.Add(result.Sources.Values.Count(s => s.Reidentified) + " source door(s) re-identified (IFC GlobalId / link).");
            }
            foreach (var inst in recovered)
            {
                SourceCheck sc;
                if (result.Sources.TryGetValue(inst.Id, out sc) && sc.Current != null)
                {
                    DoorSetInstanceOperations.AcceptSourceChanges(inst, sc.Current.Current);
                    sc.Comparison = new SourceComparison();
                    sc.State = SourceState.Ok;
                }
            }

            // ---- components ----
            foreach (var inst in project.DoorSetInstances)
            {
                foreach (var c in inst.Components)
                {
                    Element el = string.IsNullOrEmpty(c.ElementUniqueId) ? null : doc.GetElement(c.ElementUniqueId);

                    List<Element> tagged;
                    byComponent.TryGetValue(c.Id ?? "", out tagged);
                    if (el == null && tagged != null && tagged.Count > 0 && c.State != ComponentState.Failed)
                    {
                        el = tagged[0];
                        c.ElementUniqueId = el.UniqueId;
                        c.ElementId = RevitCompat.IdValue(el.Id);
                        persistedDataChanged = true;
                        result.RelinkedComponents++;
                    }
                    if (tagged != null && el != null)
                        result.DuplicateElements += tagged.Count(t => t.UniqueId != el.UniqueId);

                    if (el == null)
                    {
                        if (!string.IsNullOrEmpty(c.ElementUniqueId))
                        {
                            c.State = ComponentState.Missing;
                            result.MissingComponents++;
                        }
                        else if (c.State == ComponentState.Placed || c.State == ComponentState.ManuallyModified || c.State == ComponentState.Missing)
                        {
                            c.State = ComponentState.Planned;
                        }
                        continue;
                    }

                    c.ElementId = RevitCompat.IdValue(el.Id);
                    if (c.IsBuiltIn) continue; // shares the carrier's element: takes over its state below
                    Vec3 pos;
                    double rot;
                    if (!ElementPose.TryRead(el, out pos, out rot))
                    {
                        c.State = ComponentState.Placed;
                        continue;
                    }
                    c.ActualPosition = pos;
                    c.ActualRotationDeg = rot;

                    string detail;
                    if (ComponentDriftDetector.IsDrifted(c, pos, rot, settings, out detail))
                    {
                        c.State = ComponentState.ManuallyModified;
                        c.PlacementNote = SplitNote(c.PlacementNote) + " | Manually modified: " + detail;
                        result.ModifiedComponents++;
                    }
                    else
                    {
                        c.State = ComponentState.Placed;
                        c.PlacementNote = SplitNote(c.PlacementNote);
                    }
                }
                DoorSetInstanceOperations.FollowCarriers(inst);
            }

            if (result.RelinkedComponents > 0) result.Messages.Add(result.RelinkedComponents + " component(s) relinked from element tags.");
            if (result.DuplicateElements > 0) result.Messages.Add(result.DuplicateElements + " copied element(s) carry an existing Sentinel identity.");
            if (result.UntrackedElements > 0) result.Messages.Add(result.UntrackedElements + " element(s) are tagged by another Sentinel project.");

            result.Success = true;
            return result;
        }

        private static string SplitNote(string note)
        {
            if (string.IsNullOrEmpty(note)) return note;
            var i = note.IndexOf(" | Manually modified:", StringComparison.Ordinal);
            return i >= 0 ? note.Substring(0, i) : note;
        }
    }
}
