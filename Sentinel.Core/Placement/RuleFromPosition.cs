using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;

namespace Sentinel.Core.Placement
{
    public class RuleFromPositionResult
    {
        /// <summary>The rule that puts the component exactly where it is in the model; null when not possible.</summary>
        public PlacementRule Rule { get; set; }

        /// <summary>Why the position cannot be turned into a rule.</summary>
        public string Error { get; set; }

        /// <summary>What changes, e.g. "along the wall 150 → 230 mm, height 1000 → 1100 mm".</summary>
        public string ChangeText { get; set; }

        public bool Success => Rule != null;
    }

    /// <summary>
    /// The reverse of <see cref="DoorSetPlacementCalculator"/>: turns a component's position in the model (moved by
    /// hand in Revit) back into placement rule values relative to its door, so the position can be used for that
    /// door or for every door of the set type. Reference jamb, side, height reference and orientation mode of the
    /// current rule are kept; offsets, mounting height and extra rotation are measured.
    /// </summary>
    public static class RuleFromPosition
    {
        /// <param name="rule">The rule currently used for the component (template with this door's overrides).</param>
        /// <param name="slot">The component's calculated placement (side, wall clearance, rotation it was calculated with).</param>
        /// <param name="door">Door geometry the plan was calculated from.</param>
        /// <param name="resolvedHinge">Hinge side the plan used.</param>
        /// <param name="actual">Where the element is now (mm, host coordinates).</param>
        /// <param name="actualRotationDeg">Its rotation now.</param>
        /// <param name="defaultMountingHeightMm">Component default height (kept as "default" when unchanged).</param>
        public static RuleFromPositionResult Derive(PlacementRule rule, CalculatedPlacement slot, DoorGeometry door, HingeSide resolvedHinge,
            Vec3 actual, double actualRotationDeg, double? defaultMountingHeightMm)
        {
            var result = new RuleFromPositionResult();
            if (rule == null || slot == null || door == null || !door.IsValid)
            {
                result.Error = "The door geometry is not available.";
                return result;
            }

            var n = door.Facing.Flatten().Normalize();
            var w = (door.WidthAxis.Flatten() - n * door.WidthAxis.Flatten().Dot(n)).Normalize();
            var halfWidth = door.WidthMm / 2.0;
            var halfWall = Math.Max(0.0, door.WallThicknessMm) / 2.0;
            var hingeSign = resolvedHinge == HingeSide.PositiveWidthAxis ? 1.0 : -1.0;
            var d = actual - door.Origin;

            // --- along the wall (same reference jamb) ---
            double refAlong, outward;
            switch (rule.Reference)
            {
                case PlacementReference.HingeJamb: refAlong = hingeSign * halfWidth; outward = hingeSign; break;
                case PlacementReference.LatchJamb: refAlong = -hingeSign * halfWidth; outward = -hingeSign; break;
                default: refAlong = 0.0; outward = hingeSign; break;
            }
            var along = (d.Dot(w) - refAlong) / outward;

            // --- from the wall face (this door's measured wall push-out is not part of the rule) ---
            var perp = d.Dot(n);
            double fromWall;
            switch (slot.Side)
            {
                case ResolvedSide.SideA:
                    if (perp < -1) return Fail(result, "It was moved to the other side of the wall. Use Flip sides for that instead.");
                    fromWall = perp - halfWall - slot.WallClearanceMm;
                    break;
                case ResolvedSide.SideB:
                    if (perp > 1) return Fail(result, "It was moved to the other side of the wall. Use Flip sides for that instead.");
                    fromWall = -perp - halfWall - slot.WallClearanceMm;
                    break;
                default:
                    fromWall = perp;
                    break;
            }

            // --- height ---
            var z = actual.Z - door.Origin.Z;
            var mount = rule.HeightReference == HeightReference.DoorTop ? z - door.HeightMm : z;

            // --- extra rotation compared with what the rule gives ---
            var turn = Angles.Normalize360(actualRotationDeg - slot.InstanceRotationDeg);
            if (turn > 180) turn -= 360;

            var derived = rule.Clone();
            derived.AlongWallOffsetMm = Math.Round(along);
            derived.FromWallOffsetMm = Math.Round(fromWall);
            var height = Math.Round(mount);
            var unchangedDefault = !rule.MountingHeightMm.HasValue && defaultMountingHeightMm.HasValue &&
                                   Math.Abs(height - defaultMountingHeightMm.Value) < 1;
            derived.MountingHeightMm = unchangedDefault ? (double?)null : height;
            if (Math.Abs(turn) >= 0.5)
            {
                var r = Angles.Normalize360(rule.RotationDeg + turn);
                derived.RotationDeg = Math.Round(r > 180 ? r - 360 : r, 1);
            }

            var oldHeight = rule.MountingHeightMm ?? defaultMountingHeightMm ?? 0;
            var parts = new List<string>();
            var inv = CultureInfo.InvariantCulture;
            if (Math.Abs(derived.AlongWallOffsetMm - rule.AlongWallOffsetMm) >= 1)
                parts.Add("along the wall " + rule.AlongWallOffsetMm.ToString("0", inv) + " → " + derived.AlongWallOffsetMm.ToString("0", inv) + " mm");
            if (Math.Abs(derived.FromWallOffsetMm - rule.FromWallOffsetMm) >= 1)
                parts.Add("from the wall " + rule.FromWallOffsetMm.ToString("0", inv) + " → " + derived.FromWallOffsetMm.ToString("0", inv) + " mm");
            if (Math.Abs(height - oldHeight) >= 1)
                parts.Add("height " + oldHeight.ToString("0", inv) + " → " + height.ToString("0", inv) + " mm");
            if (Math.Abs(derived.RotationDeg - rule.RotationDeg) >= 0.5)
                parts.Add("rotation " + rule.RotationDeg.ToString("0.#", inv) + "° → " + derived.RotationDeg.ToString("0.#", inv) + "°");
            result.ChangeText = parts.Count > 0 ? string.Join(", ", parts) : "no change";
            result.Rule = derived;
            return result;
        }

        private static RuleFromPositionResult Fail(RuleFromPositionResult r, string why)
        {
            r.Error = why;
            return r;
        }

        /// <summary>
        /// After the rule was changed to the element's own position: the element is where the plan puts it now, so its
        /// record becomes Placed again (no manual move, nothing to update). The placed-configuration hash follows only
        /// when every other component is unaffected, so real pending changes (e.g. a mirrored copy) still show.
        /// </summary>
        public static void AdoptPlacedPosition(DoorSetInstance inst, PlacementPlan before, PlacementPlan after, string ruleId)
        {
            if (inst == null || after == null) return;
            foreach (var rec in inst.Components.Where(c => c.ActualPosition.HasValue && SlotRule(c.SlotKey) == ruleId))
            {
                var target = after.Placements.FirstOrDefault(p => p.SlotKey == rec.SlotKey);
                if (target == null || target.Position.DistanceTo(rec.ActualPosition.Value) > 2) continue;
                rec.CalculatedPosition = target.Position;
                rec.CalculatedRotationDeg = target.InstanceRotationDeg;
                rec.PlacedPosition = rec.ActualPosition;
                rec.PlacedRotationDeg = rec.ActualRotationDeg;
                rec.State = ComponentState.Placed;
                rec.ManualPositionAccepted = false;
                rec.ManualPositionAcceptedAt = null;
            }
            DoorSetInstanceOperations.FollowCarriers(inst); // components built into it (a lock in the magnet) come along

            if (before == null || inst.PlacedConfigurationHash != before.ConfigurationHash) return;
            var adopted = new HashSet<string>(inst.Components.Where(c => SlotRule(c.SlotKey) == ruleId && c.State == ComponentState.Placed &&
                                                                         c.CalculatedPosition.HasValue &&
                                                                         after.Placements.Any(p => p.SlotKey == c.SlotKey && p.Position.DistanceTo(c.CalculatedPosition.Value) < 0.5))
                                                        .Select(c => c.SlotKey));
            var othersUnchanged = after.Placements.All(p =>
            {
                if (adopted.Contains(p.SlotKey)) return true;
                var old = before.Placements.FirstOrDefault(o => o.SlotKey == p.SlotKey);
                return old != null && old.Position.DistanceTo(p.Position) < 0.5 && Math.Abs(old.InstanceRotationDeg - p.InstanceRotationDeg) < 0.05;
            }) && before.Placements.Count == after.Placements.Count;
            if (othersUnchanged) inst.PlacedConfigurationHash = after.ConfigurationHash;
        }

        private static string SlotRule(string slotKey) =>
            slotKey != null && slotKey.EndsWith(DoorSetPlacementCalculator.MirrorSuffix, StringComparison.Ordinal)
                ? slotKey.Substring(0, slotKey.Length - DoorSetPlacementCalculator.MirrorSuffix.Length)
                : slotKey;
    }
}
