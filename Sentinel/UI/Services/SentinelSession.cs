using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.Core.Status;

namespace Sentinel.UI.Services
{
    /// <summary>
    /// UI-side state that is not persisted: the last discovery result, live source checks from Refresh and the
    /// instances currently shown in preview. Also the single place where plans and statuses are computed.
    /// </summary>
    public class SentinelSession
    {
        public SentinelSession(ISentinelHost host)
        {
            Host = host;
        }

        public ISentinelHost Host { get; }
        public SentinelProject Project => Host.Project;

        public List<DiscoveredDoor> DiscoveredDoors { get; set; } = new List<DiscoveredDoor>();
        public string DiscoveryLinkName { get; set; }

        /// <summary>Captured-parameter names the current discovery read (Settings may have more by now).</summary>
        public List<string> ParametersRead { get; set; } = new List<string>();
        public Dictionary<string, SourceCheck> SourceChecks { get; set; } = new Dictionary<string, SourceCheck>();
        public HashSet<string> PreviewInstanceIds { get; } = new HashSet<string>();
        public bool HasRefreshed { get; set; }

        /// <summary>Live door state for an instance (refresh check first, then the last discovery).</summary>
        public DiscoveredDoor LiveDoor(DoorSetInstance inst)
        {
            if (inst == null) return null;
            SourceCheck sc;
            if (SourceChecks.TryGetValue(inst.Id, out sc) && sc.Current != null) return sc.Current;
            var key = inst.Source?.Key;
            return DiscoveredDoors.FirstOrDefault(d => d.Key == key);
        }

        public DoorGeometry GeometryFor(DoorSetInstance inst) =>
            LiveDoor(inst)?.Geometry ?? inst?.Source?.LastKnownGeometry;

        public PlacementPlan Plan(DoorSetInstance inst)
        {
            if (inst == null || string.IsNullOrEmpty(inst.DefinitionId)) return null;
            return DoorSetPlacementCalculator.Calculate(inst, Project.FindDoorSet(inst.DefinitionId), GeometryFor(inst), Project);
        }

        public SourceCheck Check(DoorSetInstance inst)
        {
            SourceCheck sc;
            return inst != null && SourceChecks.TryGetValue(inst.Id, out sc) ? sc : null;
        }

        public SetStatusResult Evaluate(DoorSetInstance inst)
        {
            var sc = Check(inst);
            var comparison = sc?.Comparison;
            if (sc == null && inst != null)
            {
                // Not refreshed yet: compare against the last discovery if the door was found there.
                var live = DiscoveredDoors.FirstOrDefault(d => d.Key == inst.Source?.Key);
                if (live != null)
                    comparison = SourceChangeDetector.Compare(inst.Source, live.Geometry, live.Current.LastKnownParameters, Project.Settings);
            }

            return DoorSetStatusEvaluator.Evaluate(new DoorSetStatusContext
            {
                Instance = inst,
                Definition = inst != null ? Project.FindDoorSet(inst.DefinitionId) : null,
                SourceState = sc?.State ?? SourceState.NotChecked,
                SourceComparison = comparison,
                Plan = Plan(inst),
                IsPreviewing = inst != null && PreviewInstanceIds.Contains(inst.Id)
            });
        }

        /// <summary>"Corridor → Server Room" style text for the controlled direction.</summary>
        public static string AccessText(SourceDoorReference src, AccessDirection dir)
        {
            var a = SideName(src, true);
            var b = SideName(src, false);
            switch (dir)
            {
                case AccessDirection.SideBToSideA: return b + " → " + a;
                case AccessDirection.Both: return a + " ↔ " + b;
                default: return a + " → " + b;
            }
        }

        public static string SideName(SourceDoorReference src, bool sideA)
        {
            var room = sideA ? src?.SideARoom : src?.SideBRoom;
            return string.IsNullOrWhiteSpace(room) ? (sideA ? "Side A" : "Side B") : room;
        }
    }
}
