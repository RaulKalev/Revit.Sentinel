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
