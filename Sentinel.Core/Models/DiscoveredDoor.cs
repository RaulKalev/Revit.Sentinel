using System;
using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Geometry;

namespace Sentinel.Core.Models
{
    /// <summary>
    /// A door found in a linked model during discovery (transient, not persisted on its own). Its
    /// <see cref="Current"/> reference carries the live values; a clone becomes the instance snapshot.
    /// </summary>
    public class DiscoveredDoor
    {
        public SourceDoorReference Current { get; set; }

        /// <summary>Warnings produced while reading the door (missing width, estimated geometry...).</summary>
        public List<string> ReadWarnings { get; set; } = new List<string>();

        public DoorGeometry Geometry => Current?.LastKnownGeometry;
        public string Key => Current?.Key;

        /// <summary>
        /// Set when another source link has a door in the same opening ("SKU13 in ITM_TP_SA.ifc"). Architecture is
        /// often split across models (shell + interior) and some doors are modelled in both.
        /// </summary>
        public string PossibleDuplicateOf { get; set; }
    }

    /// <summary>Finds doors from different links that occupy the same opening.</summary>
    public static class CrossLinkDuplicates
    {
        public const double PlanToleranceMm = 300;
        public const double HeightToleranceMm = 500;

        /// <summary>
        /// Marks every door that has a door from another link within the tolerances (plan distance and height of
        /// the door origin) and returns how many doors were marked. Doors of the same link are never compared.
        /// </summary>
        public static int Mark(IList<DiscoveredDoor> doors, double planToleranceMm = PlanToleranceMm, double heightToleranceMm = HeightToleranceMm)
        {
            if (doors == null || doors.Count < 2) return 0;
            var located = doors.Where(d => d?.Current != null && d.Geometry != null).ToList();
            if (located.Select(d => d.Current.LinkInstanceUniqueId).Distinct().Count() < 2) return 0;

            // Plan grid with cell = tolerance: candidates are in the same or a neighbouring cell.
            var cell = Math.Max(1.0, planToleranceMm);
            var grid = new Dictionary<long, List<DiscoveredDoor>>();
            Func<double, double, long> key = (x, y) => ((long)Math.Floor(x / cell) << 32) ^ (long)(uint)(int)Math.Floor(y / cell);
            foreach (var d in located)
            {
                var k = key(d.Geometry.Origin.X, d.Geometry.Origin.Y);
                List<DiscoveredDoor> list;
                if (!grid.TryGetValue(k, out list)) grid[k] = list = new List<DiscoveredDoor>();
                list.Add(d);
            }

            var marked = 0;
            foreach (var d in located)
            {
                var o = d.Geometry.Origin;
                DiscoveredDoor best = null;
                var bestDist = double.MaxValue;
                var cx = (long)Math.Floor(o.X / cell);
                var cy = (long)Math.Floor(o.Y / cell);
                for (var ix = cx - 1; ix <= cx + 1; ix++)
                for (var iy = cy - 1; iy <= cy + 1; iy++)
                {
                    List<DiscoveredDoor> list;
                    if (!grid.TryGetValue((ix << 32) ^ (long)(uint)(int)iy, out list)) continue;
                    foreach (var other in list)
                    {
                        if (other == d || other.Current.LinkInstanceUniqueId == d.Current.LinkInstanceUniqueId) continue;
                        var p = other.Geometry.Origin;
                        var plan = Math.Sqrt((p.X - o.X) * (p.X - o.X) + (p.Y - o.Y) * (p.Y - o.Y));
                        if (plan > planToleranceMm || Math.Abs(p.Z - o.Z) > heightToleranceMm || plan >= bestDist) continue;
                        best = other;
                        bestDist = plan;
                    }
                }
                if (best == null) continue;
                d.PossibleDuplicateOf = best.Current.DisplayName + " in " + ShortLinkName(best.Current.LinkName);
                marked++;
            }
            return marked;
        }

        /// <summary>"SA.ifc : 12" → "SA.ifc" (Revit appends the link instance number).</summary>
        private static string ShortLinkName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "another link";
            var i = name.LastIndexOf(" : ", StringComparison.Ordinal);
            return i > 0 ? name.Substring(0, i) : name;
        }
    }

    /// <summary>Matches live doors to stored instances: UniqueId first, then IFC GlobalId.</summary>
    public static class DoorMatcher
    {
        public class MatchResult
        {
            public DiscoveredDoor Door { get; set; }
            public DoorSetInstance Instance { get; set; }

            /// <summary>True when the instance was matched through the IFC GlobalId (door UniqueId changed).</summary>
            public bool MatchedByIfcGlobalId { get; set; }
        }

        public static List<MatchResult> Match(IEnumerable<DiscoveredDoor> doors, IEnumerable<DoorSetInstance> instances)
        {
            var result = new List<MatchResult>();
            var pending = (instances ?? Enumerable.Empty<DoorSetInstance>()).Where(i => i != null && i.Source != null).ToList();
            var byKey = new Dictionary<string, DoorSetInstance>();
            foreach (var i in pending)
                if (!byKey.ContainsKey(i.Source.Key)) byKey[i.Source.Key] = i;

            var used = new HashSet<string>();
            var unmatchedDoors = new List<DiscoveredDoor>();

            foreach (var d in doors ?? Enumerable.Empty<DiscoveredDoor>())
            {
                if (d?.Current == null) continue;
                DoorSetInstance inst;
                if (byKey.TryGetValue(d.Key, out inst) && !used.Contains(inst.Id))
                {
                    used.Add(inst.Id);
                    result.Add(new MatchResult { Door = d, Instance = inst });
                }
                else
                {
                    unmatchedDoors.Add(d);
                }
            }

            foreach (var d in unmatchedDoors)
            {
                DoorSetInstance inst = null;
                var guid = d.Current.IfcGlobalId;
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    inst = pending.FirstOrDefault(i => !used.Contains(i.Id) &&
                        string.Equals(i.Source.IfcGlobalId, guid, System.StringComparison.OrdinalIgnoreCase) &&
                        SameLink(i.Source, d.Current));
                }
                if (inst != null) used.Add(inst.Id);
                result.Add(new MatchResult { Door = d, Instance = inst, MatchedByIfcGlobalId = inst != null });
            }

            foreach (var i in pending.Where(i => !used.Contains(i.Id)))
                result.Add(new MatchResult { Instance = i });

            return result;
        }

        /// <summary>Same link instance, or the same linked document title (link removed and re-added).</summary>
        public static bool SameLink(SourceDoorReference a, SourceDoorReference b)
        {
            if (a == null || b == null) return false;
            if (!string.IsNullOrEmpty(a.LinkInstanceUniqueId) && a.LinkInstanceUniqueId == b.LinkInstanceUniqueId) return true;
            return !string.IsNullOrWhiteSpace(a.LinkDocumentTitle) &&
                   string.Equals(a.LinkDocumentTitle, b.LinkDocumentTitle, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
