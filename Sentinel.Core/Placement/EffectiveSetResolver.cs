using System;
using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Models;

namespace Sentinel.Core.Placement
{
    /// <summary>A component after applying the instance overrides to the base definition.</summary>
    public class EffectiveComponent
    {
        public string RuleId { get; set; }
        public string Label { get; set; }

        /// <summary>Null when the referenced component definition no longer exists.</summary>
        public ComponentDefinition Component { get; set; }
        public string ComponentDefinitionId { get; set; }

        /// <summary>Fully resolved rule (overrides applied, mounting height defaulted).</summary>
        public PlacementRule Rule { get; set; }

        /// <summary>True when the component was added on this instance only.</summary>
        public bool IsAddedByOverride { get; set; }

        /// <summary>True when any rule value or the component type was overridden on this instance.</summary>
        public bool IsOverridden { get; set; }

        public string DisplayLabel =>
            !string.IsNullOrWhiteSpace(Label) ? Label :
            Component != null ? Component.Name : "(missing component)";
    }

    /// <summary>Applies per-instance overrides to a set definition without modifying either.</summary>
    public static class EffectiveSetResolver
    {
        public static List<EffectiveComponent> Resolve(
            SecuritySetDefinition definition,
            SetOverrides overrides,
            Func<string, ComponentDefinition> findComponent)
        {
            var result = new List<EffectiveComponent>();
            if (findComponent == null) findComponent = id => null;
            overrides = overrides ?? new SetOverrides();
            var removed = new HashSet<string>(overrides.RemovedRuleIds ?? new List<string>());

            if (definition != null)
            {
                foreach (var slot in definition.Components ?? new List<SetComponentRule>())
                {
                    if (slot == null || removed.Contains(slot.Id)) continue;
                    result.Add(Build(slot, overrides.Find(slot.Id), false, findComponent));
                }
            }

            foreach (var added in overrides.AddedComponents ?? new List<SetComponentRule>())
            {
                if (added == null || removed.Contains(added.Id)) continue;
                result.Add(Build(added, overrides.Find(added.Id), true, findComponent));
            }

            return result;
        }

        private static EffectiveComponent Build(SetComponentRule slot, ComponentRuleOverride ov, bool added,
            Func<string, ComponentDefinition> findComponent)
        {
            var componentId = ov != null && !string.IsNullOrEmpty(ov.ComponentDefinitionId)
                ? ov.ComponentDefinitionId
                : slot.ComponentDefinitionId;
            var component = findComponent(componentId);

            var rule = (slot.Rule ?? component?.DefaultPlacement ?? new PlacementRule()).Clone();
            var overridden = false;
            if (ov != null)
            {
                if (ov.Reference.HasValue) { rule.Reference = ov.Reference.Value; overridden = true; }
                if (ov.Side.HasValue) { rule.Side = ov.Side.Value; overridden = true; }
                if (ov.AlongWallOffsetMm.HasValue) { rule.AlongWallOffsetMm = ov.AlongWallOffsetMm.Value; overridden = true; }
                if (ov.FromWallOffsetMm.HasValue) { rule.FromWallOffsetMm = ov.FromWallOffsetMm.Value; overridden = true; }
                if (ov.MountingHeightMm.HasValue) { rule.MountingHeightMm = ov.MountingHeightMm.Value; overridden = true; }
                if (ov.HeightReference.HasValue) { rule.HeightReference = ov.HeightReference.Value; overridden = true; }
                if (ov.Orientation.HasValue) { rule.Orientation = ov.Orientation.Value; overridden = true; }
                if (ov.RotationDeg.HasValue) { rule.RotationDeg = ov.RotationDeg.Value; overridden = true; }
                if (!string.IsNullOrEmpty(ov.ComponentDefinitionId)) overridden = true;
            }

            if (!rule.MountingHeightMm.HasValue)
                rule.MountingHeightMm = component != null ? component.DefaultMountingHeightMm : 0.0;

            return new EffectiveComponent
            {
                RuleId = slot.Id,
                Label = slot.Label,
                Component = component,
                ComponentDefinitionId = componentId,
                Rule = rule,
                IsAddedByOverride = added,
                IsOverridden = overridden
            };
        }
    }
}
