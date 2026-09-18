using System;
using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Rules;

namespace Sentinel.Core.Models
{
    /// <summary>
    /// Root aggregate of everything Sentinel persists in a Revit project. Plain data: no Revit objects.
    /// </summary>
    public class SentinelProject
    {
        /// <summary>Stable id of this project's Sentinel data (written to placed elements).</summary>
        public string ProjectId { get; set; } = Ids.New();

        public SentinelSettings Settings { get; set; } = new SentinelSettings();
        public List<ComponentDefinition> ComponentDefinitions { get; set; } = new List<ComponentDefinition>();
        public List<DoorSetDefinition> DoorSetDefinitions { get; set; } = new List<DoorSetDefinition>();
        public List<DoorSetInstance> DoorSetInstances { get; set; } = new List<DoorSetInstance>();
        public List<AssignmentRule> AssignmentRules { get; set; } = new List<AssignmentRule>();

        /// <summary>
        /// Stored items this version could not read (corrupt, or written by a newer Sentinel). They are kept
        /// verbatim and written back unchanged so saving never destroys data it does not understand.
        /// </summary>
        public List<string> PreservedRawItems { get; set; } = new List<string>();

        public ComponentDefinition FindComponent(string id) =>
            string.IsNullOrEmpty(id) ? null : ComponentDefinitions.FirstOrDefault(c => c.Id == id);

        public DoorSetDefinition FindDoorSet(string id) =>
            string.IsNullOrEmpty(id) ? null : DoorSetDefinitions.FirstOrDefault(d => d.Id == id);

        public DoorSetInstance FindInstance(string id) =>
            string.IsNullOrEmpty(id) ? null : DoorSetInstances.FirstOrDefault(i => i.Id == id);

        public DoorSetInstance FindInstanceBySourceKey(string key) =>
            string.IsNullOrEmpty(key) ? null : DoorSetInstances.FirstOrDefault(i => i.Source != null && i.Source.Key == key);

        /// <summary>Instances still referencing the definition (used to block unsafe deletes).</summary>
        public int CountInstancesUsing(string definitionId) =>
            DoorSetInstances.Count(i => i.DefinitionId == definitionId);

        /// <summary>Definitions (and instance overrides) that still reference a component definition.</summary>
        public int CountUsagesOfComponent(string componentId)
        {
            var n = DoorSetDefinitions.Sum(d => d.Components.Count(c => c.ComponentDefinitionId == componentId));
            n += DoorSetInstances.Sum(i =>
                (i.Overrides?.AddedComponents?.Count(c => c.ComponentDefinitionId == componentId) ?? 0) +
                (i.Overrides?.RuleOverrides?.Count(o => o.ComponentDefinitionId == componentId) ?? 0));
            return n;
        }

        /// <summary>Returns a code like "DS-03" that is not used yet.</summary>
        public string NextDoorSetCode()
        {
            var used = new HashSet<string>(DoorSetDefinitions.Select(d => d.Code ?? ""), StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < 1000; i++)
            {
                var code = "DS-" + i.ToString("00");
                if (!used.Contains(code)) return code;
            }
            return "DS-" + Guid.NewGuid().ToString("N").Substring(0, 4);
        }
    }
}
