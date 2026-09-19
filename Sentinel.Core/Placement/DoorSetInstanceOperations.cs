using System;
using System.Linq;
using Sentinel.Core.Models;

namespace Sentinel.Core.Placement
{
    /// <summary>
    /// Edits on door set instances. All edits keep the reference to the base definition and record differences as
    /// overrides; nothing here touches Revit — placed elements are reconciled later through <see cref="PlacementDiff"/>.
    /// </summary>
    public static class DoorSetInstanceOperations
    {
        public static DoorSetInstance Create(DiscoveredDoor door, DoorSetDefinition definition)
        {
            if (door?.Current == null) throw new ArgumentNullException(nameof(door));
            var inst = new DoorSetInstance
            {
                Source = door.Current.Clone(),
                DefinitionId = definition?.Id,
                AccessDirection = definition != null ? definition.DefaultAccessDirection : AccessDirection.SideAToSideB
            };
            inst.Source.LastSyncedUtc = DateTime.UtcNow.ToString("o");
            return inst;
        }

        /// <summary>
        /// Assigns or changes the set type. Door-specific added components are kept; overrides that pointed at rules
        /// of the previous template are dropped because they no longer apply.
        /// </summary>
        public static void AssignDefinition(DoorSetInstance inst, DoorSetDefinition definition)
        {
            if (inst == null) return;
            var changed = inst.DefinitionId != definition?.Id;
            inst.DefinitionId = definition?.Id;
            inst.IsIgnored = false;
            inst.NoAccessControl = false;
            if (changed)
            {
                var ov = inst.Overrides ?? (inst.Overrides = new SetOverrides());
                var addedIds = ov.AddedComponents.Select(a => a.Id).ToList();
                var templateIds = definition?.Components.Select(c => c.Id).ToList() ?? new System.Collections.Generic.List<string>();
                ov.RemovedRuleIds.RemoveAll(id => !templateIds.Contains(id) && !addedIds.Contains(id));
                ov.RuleOverrides.RemoveAll(o => !templateIds.Contains(o.RuleId) && !addedIds.Contains(o.RuleId));
                if (!inst.HasPlacedElements && definition != null)
                    inst.AccessDirection = definition.DefaultAccessDirection;
            }
            inst.Touch();
        }

        /// <summary>
        /// Marks the door as needing no access control: drops the set type and its door-specific overrides. Placed
        /// components must be deleted by the caller first (their records are kept until then, so nothing is lost).
        /// </summary>
        public static void MarkNoAccessControl(DoorSetInstance inst)
        {
            if (inst == null) return;
            inst.DefinitionId = null;
            inst.IsIgnored = false;
            inst.IgnoreReason = null;
            inst.NoAccessControl = true;
            inst.LastError = null;
            if (!inst.HasPlacedElements)
            {
                inst.Overrides = new SetOverrides();
                inst.Components.Clear();
                inst.PlacedConfigurationHash = null;
            }
            inst.Touch();
        }

        /// <summary>Swaps SideA→B and SideB→A. Returns false for Both (nothing to flip).</summary>
        public static bool Flip(DoorSetInstance inst)
        {
            if (inst == null) return false;
            switch (inst.AccessDirection)
            {
                case AccessDirection.SideAToSideB: inst.AccessDirection = AccessDirection.SideBToSideA; break;
                case AccessDirection.SideBToSideA: inst.AccessDirection = AccessDirection.SideAToSideB; break;
                default: return false;
            }
            inst.Touch();
            return true;
        }

        /// <summary>Toggles the hinge side relative to the currently resolved one.</summary>
        public static void SwapHinge(DoorSetInstance inst, HingeSide resolvedHinge)
        {
            if (inst == null) return;
            inst.HingeSideOverride = resolvedHinge == HingeSide.PositiveWidthAxis
                ? HingeSide.NegativeWidthAxis
                : HingeSide.PositiveWidthAxis;
            inst.Touch();
        }

        public static SetComponentRule AddComponent(DoorSetInstance inst, ComponentDefinition component, PlacementRule rule = null, string label = null)
        {
            if (inst == null || component == null) return null;
            var ov = inst.Overrides ?? (inst.Overrides = new SetOverrides());
            var slot = new SetComponentRule
            {
                ComponentDefinitionId = component.Id,
                Label = string.IsNullOrWhiteSpace(label) ? component.Name : label,
                Rule = (rule ?? component.DefaultPlacement ?? new PlacementRule()).Clone()
            };
            ov.AddedComponents.Add(slot);
            inst.Touch();
            return slot;
        }

        /// <summary>Removes a component from this door only (template rules are excluded, added ones deleted).</summary>
        public static void RemoveComponent(DoorSetInstance inst, string ruleId)
        {
            if (inst == null || string.IsNullOrEmpty(ruleId)) return;
            var ov = inst.Overrides ?? (inst.Overrides = new SetOverrides());
            var added = ov.AddedComponents.FirstOrDefault(a => a.Id == ruleId);
            if (added != null)
            {
                ov.AddedComponents.Remove(added);
                ov.RuleOverrides.RemoveAll(o => o.RuleId == ruleId);
            }
            else if (!ov.RemovedRuleIds.Contains(ruleId))
            {
                ov.RemovedRuleIds.Add(ruleId);
            }
            inst.Touch();
        }

        public static void RestoreComponent(DoorSetInstance inst, string ruleId)
        {
            if (inst?.Overrides == null) return;
            inst.Overrides.RemovedRuleIds.Remove(ruleId);
            inst.Touch();
        }

        public static void ChangeComponentType(DoorSetInstance inst, string ruleId, string componentDefinitionId)
        {
            if (inst == null || string.IsNullOrEmpty(ruleId)) return;
            var ov = inst.Overrides ?? (inst.Overrides = new SetOverrides());
            var added = ov.AddedComponents.FirstOrDefault(a => a.Id == ruleId);
            if (added != null) added.ComponentDefinitionId = componentDefinitionId;
            else ov.GetOrCreate(ruleId).ComponentDefinitionId = componentDefinitionId;
            inst.Touch();
        }

        /// <summary>Applies a rule override (only the non-null values of <paramref name="values"/>).</summary>
        public static void OverrideRule(DoorSetInstance inst, ComponentRuleOverride values)
        {
            if (inst == null || values == null || string.IsNullOrEmpty(values.RuleId)) return;
            var ov = inst.Overrides ?? (inst.Overrides = new SetOverrides());
            var target = ov.GetOrCreate(values.RuleId);
            if (values.Reference.HasValue) target.Reference = values.Reference;
            if (values.Side.HasValue) target.Side = values.Side;
            if (values.AlongWallOffsetMm.HasValue) target.AlongWallOffsetMm = values.AlongWallOffsetMm;
            if (values.FromWallOffsetMm.HasValue) target.FromWallOffsetMm = values.FromWallOffsetMm;
            if (values.MountingHeightMm.HasValue) target.MountingHeightMm = values.MountingHeightMm;
            if (values.HeightReference.HasValue) target.HeightReference = values.HeightReference;
            if (values.Orientation.HasValue) target.Orientation = values.Orientation;
            if (values.RotationDeg.HasValue) target.RotationDeg = values.RotationDeg;
            if (!string.IsNullOrEmpty(values.ComponentDefinitionId)) target.ComponentDefinitionId = values.ComponentDefinitionId;
            inst.Touch();
        }

        public static void ClearRuleOverride(DoorSetInstance inst, string ruleId)
        {
            if (inst?.Overrides == null) return;
            inst.Overrides.RuleOverrides.RemoveAll(o => o.RuleId == ruleId);
            inst.Touch();
        }

        /// <summary>Accepts the current source state as the new snapshot (after a source change was reviewed).</summary>
        public static void AcceptSourceChanges(DoorSetInstance inst, SourceDoorReference current)
        {
            if (inst == null || current == null) return;
            var keepLinkUid = current.LinkInstanceUniqueId;
            inst.Source = current.Clone();
            inst.Source.LinkInstanceUniqueId = keepLinkUid;
            inst.Source.LastSyncedUtc = DateTime.UtcNow.ToString("o");
            inst.Touch();
        }

        /// <summary>Marks a manually moved component as intentional; it will no longer be reported.</summary>
        public static void AcceptManualPosition(PlacedComponentInstance c)
        {
            if (c == null || !c.ActualPosition.HasValue) return;
            c.ManualPositionAccepted = true;
            c.ManualPositionAcceptedAt = c.ActualPosition;
            c.ManualRotationAcceptedDeg = c.ActualRotationDeg;
            if (c.State == ComponentState.ManuallyModified) c.State = ComponentState.Placed;
        }
    }
}
