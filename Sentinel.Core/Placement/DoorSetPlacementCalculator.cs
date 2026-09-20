using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;

namespace Sentinel.Core.Placement
{
    /// <summary>Input for <see cref="DoorSetPlacementCalculator"/>.</summary>
    public class DoorPlacementInput
    {
        public DoorGeometry Door { get; set; }
        public AccessDirection AccessDirection { get; set; }
        public HingeSide? HingeOverride { get; set; }
        public List<EffectiveComponent> Components { get; set; } = new List<EffectiveComponent>();
        public SentinelSettings Settings { get; set; } = new SentinelSettings();

        /// <summary>Library lookup (names of carrier components in messages). Optional.</summary>
        public Func<string, ComponentDefinition> FindComponent { get; set; }

        /// <summary>Measured push-out from the wall face per slot (mm, see <see cref="DoorSetInstance.WallClearances"/>). Optional.</summary>
        public IDictionary<string, double> WallClearances { get; set; }
    }

    /// <summary>
    /// Computes component placements for a door set. Pure and deterministic: no Revit API, no side effects.
    /// The same result feeds the transient preview and the placement executor, so what the user previews is
    /// exactly what gets placed.
    /// </summary>
    public static class DoorSetPlacementCalculator
    {
        public const string MirrorSuffix = "#B";

        /// <summary>Rotation that turns a family's local -Y front towards plan angle 0 (+X): -270° ≡ +90°.</summary>
        public const double FamilyFrontToFacingDeg = 90.0;

        /// <summary>Convenience overload: resolves overrides and calculates in one call.</summary>
        /// <param name="applyWallClearances">False gives the plan without measured push-outs (used to measure them).</param>
        public static PlacementPlan Calculate(DoorSetInstance instance, DoorSetDefinition definition,
            DoorGeometry geometry, SentinelProject project, bool applyWallClearances = true)
        {
            var components = EffectiveSetResolver.Resolve(definition, instance?.Overrides, project.FindComponent);
            var plan = Calculate(new DoorPlacementInput
            {
                Door = geometry,
                AccessDirection = instance != null ? instance.AccessDirection : AccessDirection.SideAToSideB,
                HingeOverride = instance?.HingeSideOverride,
                Components = components,
                Settings = project.Settings ?? new SentinelSettings(),
                FindComponent = project.FindComponent,
                WallClearances = applyWallClearances && project.Settings != null && project.Settings.CheckWallClearance ? instance?.WallClearances : null
            });
            if (definition == null && instance != null && !string.IsNullOrEmpty(instance.DefinitionId))
            {
                plan.Issues.Insert(0, new StatusIssue(IssueSeverity.Error, IssueCodes.DefinitionMissing,
                    "The door set type assigned to this door no longer exists."));
            }
            return plan;
        }

        public static PlacementPlan Calculate(DoorPlacementInput input)
        {
            var plan = new PlacementPlan();
            var settings = input.Settings ?? new SentinelSettings();
            var door = input.Door;

            if (door == null || !door.IsValid)
            {
                plan.Issues.Add(new StatusIssue(IssueSeverity.Error, IssueCodes.UnsupportedGeometry,
                    door == null
                        ? "Source door geometry is not available."
                        : "Unsupported door geometry (width/height/orientation could not be determined)."));
                plan.ConfigurationHash = ComputeHash(plan.Placements);
                return plan;
            }

            // Orthonormal door frame (horizontal).
            var n = door.Facing.Flatten().Normalize();
            var w = (door.WidthAxis.Flatten() - n * door.WidthAxis.Flatten().Dot(n)).Normalize();
            var halfWidth = door.WidthMm / 2.0;
            var halfWall = Math.Max(0.0, door.WallThicknessMm) / 2.0;

            // Hinge side resolution: per-door override > source geometry > settings default (flagged).
            var hinge = input.HingeOverride.HasValue && input.HingeOverride.Value != HingeSide.Unknown
                ? input.HingeOverride.Value
                : door.Hinge;
            var hingeAssumed = false;
            if (hinge == HingeSide.Unknown)
            {
                hinge = settings.DefaultHingeSide == HingeSide.Unknown ? HingeSide.NegativeWidthAxis : settings.DefaultHingeSide;
                hingeAssumed = true;
            }
            plan.ResolvedHinge = hinge;
            var hingeSign = hinge == HingeSide.PositiveWidthAxis ? 1.0 : -1.0;

            var usesHinge = (input.Components ?? new List<EffectiveComponent>()).Any(c =>
                c.Rule != null && (c.Rule.Reference != PlacementReference.DoorCenter || Math.Abs(c.Rule.AlongWallOffsetMm) > 0.01));
            // No warning: every placement is confirmed in the review, where a wrong hinge side is seen and swapped.
            // (A warning would also mark each such placement "needs review" and stop the review from moving on.)
            if (hingeAssumed && usesHinge) plan.HingeAssumed = true;

            if (door.Source == DoorGeometrySource.EstimatedFromGeometry)
            {
                plan.Issues.Add(new StatusIssue(IssueSeverity.Info, IssueCodes.GeometryEstimated,
                    "Door frame estimated from the element geometry (no family orientation data)."));
            }

            if (door.WallThicknessMm <= 0.0)
            {
                plan.Issues.Add(new StatusIssue(IssueSeverity.Warning, IssueCodes.UnsupportedGeometry,
                    "Wall thickness is unknown; wall-face offsets are measured from the wall centre line."));
            }

            foreach (var comp in input.Components ?? new List<EffectiveComponent>())
            {
                if (comp == null) continue;
                var rule = comp.Rule ?? new PlacementRule();

                var sides = ResolveSides(rule, input.AccessDirection);
                for (int i = 0; i < sides.Count; i++)
                {
                    var side = sides[i];
                    var isMirror = i > 0;
                    plan.Placements.Add(Place(comp, rule, side, isMirror, door, n, w, halfWidth, halfWall, hingeSign, input.WallClearances));
                }
            }

            ResolveBuiltIn(plan, input, door, w, hingeSign);

            plan.ConfigurationHash = ComputeHash(plan.Placements);
            return plan;
        }

        /// <summary>Maps the rule side to concrete door sides for the given access direction.</summary>
        public static List<ResolvedSide> ResolveSides(PlacementRule rule, AccessDirection direction)
        {
            var list = new List<ResolvedSide>();
            switch (rule.Side)
            {
                case PlacementSide.SideA:
                    list.Add(ResolvedSide.SideA);
                    break;
                case PlacementSide.SideB:
                    list.Add(ResolvedSide.SideB);
                    break;
                case PlacementSide.InWall:
                    list.Add(ResolvedSide.InWall);
                    break;
                case PlacementSide.UnsecuredSide:
                    list.Add(direction == AccessDirection.SideBToSideA ? ResolvedSide.SideB : ResolvedSide.SideA);
                    if (direction == AccessDirection.Both && rule.DuplicateWhenBothDirections) list.Add(ResolvedSide.SideB);
                    break;
                case PlacementSide.SecuredSide:
                    list.Add(direction == AccessDirection.SideBToSideA ? ResolvedSide.SideA : ResolvedSide.SideB);
                    if (direction == AccessDirection.Both && rule.DuplicateWhenBothDirections) list.Add(ResolvedSide.SideA);
                    break;
            }
            return list;
        }

        private static CalculatedPlacement Place(EffectiveComponent comp, PlacementRule rule, ResolvedSide side, bool isMirror,
            DoorGeometry door, Vec3 n, Vec3 w, double halfWidth, double halfWall, double hingeSign,
            IDictionary<string, double> clearances)
        {
            var def = comp.Component;
            var p = new CalculatedPlacement
            {
                SlotKey = isMirror ? comp.RuleId + MirrorSuffix : comp.RuleId,
                RuleId = comp.RuleId,
                IsMirrorCopy = isMirror,
                ComponentDefinitionId = comp.ComponentDefinitionId,
                ComponentName = def != null ? def.Name : "(missing component)",
                Category = def != null ? def.Category : ComponentCategory.Custom,
                Label = comp.DisplayLabel,
                FamilyName = def?.FamilyName,
                TypeName = def?.TypeName,
                HostBehavior = def != null ? def.HostBehavior : HostBehavior.Auto,
                PreviewSizeMm = def != null && def.PreviewSizeMm > 1 ? def.PreviewSizeMm : 120,
                Side = side
            };

            // --- along the wall ---
            double refAlong, outward;
            switch (rule.Reference)
            {
                case PlacementReference.HingeJamb:
                    refAlong = hingeSign * halfWidth;
                    outward = hingeSign;
                    break;
                case PlacementReference.LatchJamb:
                    refAlong = -hingeSign * halfWidth;
                    outward = -hingeSign;
                    break;
                default:
                    refAlong = 0.0;
                    outward = hingeSign; // positive offset from the centre moves towards the hinge jamb
                    break;
            }
            var along = refAlong + outward * rule.AlongWallOffsetMm;

            // --- perpendicular to the wall ---
            double sideSign;
            double perp;
            switch (side)
            {
                case ResolvedSide.SideA:
                    sideSign = 1.0;
                    perp = halfWall + rule.FromWallOffsetMm;
                    break;
                case ResolvedSide.SideB:
                    sideSign = -1.0;
                    perp = -(halfWall + rule.FromWallOffsetMm);
                    break;
                default:
                    sideSign = 0.0;
                    perp = rule.FromWallOffsetMm;
                    break;
            }

            // --- wall material in front of the door's wall face (measured in Revit, e.g. a lining in another link) ---
            double clearance;
            if (sideSign != 0.0 && clearances != null && clearances.TryGetValue(WallClearance.Key(p.SlotKey, side.ToString()), out clearance) && clearance > 0.5)
            {
                perp += sideSign * clearance;
                p.WallClearanceMm = clearance;
            }

            // --- height ---
            var mount = rule.MountingHeightMm ?? (def != null ? def.DefaultMountingHeightMm : 0.0);
            var z = rule.HeightReference == HeightReference.DoorTop ? door.HeightMm + mount : mount;

            p.Position = door.Origin + w * along + n * perp + Vec3.UnitZ * z;
            p.WallNormal = sideSign == 0.0 ? Vec3.Zero : n * sideSign;

            // --- orientation ---
            Vec3 facing;
            switch (rule.Orientation)
            {
                case OrientationMode.FaceTowardDoor:
                    if (along > 0.5) facing = -w;
                    else if (along < -0.5) facing = w;
                    else facing = sideSign == 0.0 ? n : n * sideSign;
                    facing = facing.RotateAboutZ(rule.RotationDeg);
                    break;
                case OrientationMode.FollowWall:
                    facing = n.RotateAboutZ(rule.RotationDeg);
                    break;
                case OrientationMode.Fixed:
                    facing = n.RotateAboutZ(rule.RotationDeg);
                    break;
                default: // FaceAwayFromWall
                    facing = (sideSign == 0.0 ? n : n * sideSign).RotateAboutZ(rule.RotationDeg);
                    break;
            }
            p.Facing = facing.Normalize();
            p.FacingAngleDeg = p.Facing.PlanAngleDeg();
            // Family convention: the device front is the family's local -Y axis (what Revit's "Front" view looks at),
            // so an unrotated family faces 270°. Rotation needed = facing - 270 = facing + 90, plus calibration.
            p.InstanceRotationDeg = Angles.Normalize360(p.FacingAngleDeg + FamilyFrontToFacingDeg + (def != null ? def.FamilyRotationOffsetDeg : 0.0));

            p.Explanation = Explain(p, rule, mount);

            // --- validation (traceable reasons, never silent) ---
            if (def == null)
            {
                p.Issues.Add(new StatusIssue(IssueSeverity.Error, IssueCodes.ComponentDefinitionMissing,
                    p.Label + ": component definition no longer exists in the library.", p.SlotKey));
            }
            else if (!def.IsBuiltIn && !def.IsFamilyConfigured)
            {
                p.Issues.Add(new StatusIssue(IssueSeverity.Error, IssueCodes.FamilyNotConfigured,
                    def.Name + " family not configured (Components page).", p.SlotKey));
            }

            if (double.IsNaN(p.Position.X) || double.IsNaN(p.Position.Y) || double.IsNaN(p.Position.Z) ||
                double.IsInfinity(p.Position.X) || double.IsInfinity(p.Position.Y) || double.IsInfinity(p.Position.Z))
            {
                p.Issues.Add(new StatusIssue(IssueSeverity.Error, IssueCodes.InvalidPlacementPoint,
                    p.Label + ": placement point invalid.", p.SlotKey));
            }

            return p;
        }

        /// <summary>
        /// Components modelled inside another component's family (a lock inside a magnet contact) get no element of
        /// their own: they are bound to the carrier's placement and choose the carrier parameter for their side.
        /// Left/right is seen from the front of the carrier (the side Revit's Front view looks at). Without a carrier
        /// in the set, the component falls back to its own family when allowed, otherwise it is reported.
        /// </summary>
        private static void ResolveBuiltIn(PlacementPlan plan, DoorPlacementInput input, DoorGeometry door, Vec3 w, double hingeSign)
        {
            var defs = (input.Components ?? new List<EffectiveComponent>())
                .Where(c => c?.Component != null)
                .GroupBy(c => c.ComponentDefinitionId)
                .ToDictionary(g => g.Key, g => g.First().Component);
            Func<string, ComponentDefinition> find = id =>
            {
                ComponentDefinition d;
                if (!string.IsNullOrEmpty(id) && defs.TryGetValue(id, out d)) return d;
                return input.FindComponent != null && !string.IsNullOrEmpty(id) ? input.FindComponent(id) : null;
            };

            foreach (var p in plan.Placements)
            {
                var def = find(p.ComponentDefinitionId);
                if (def == null || !def.IsBuiltIn) continue;

                // Any of the carrier components can hold it (they share the left/right parameters).
                var carrierDefs = def.Carriers.Select(find).Where(c => c != null && !c.IsBuiltIn).ToList();
                var carrierName = carrierDefs.Count == 0 ? "the carrier component" : string.Join(" or ", carrierDefs.Select(c => c.Name));
                var candidates = plan.Placements.Where(c => c != p && carrierDefs.Any(cd => cd.Id == c.ComponentDefinitionId)).ToList();
                // With several carriers (e.g. two magnets) the rule can name the one that holds this component.
                var wanted = (input.Components ?? new List<EffectiveComponent>()).FirstOrDefault(c => c?.RuleId == p.RuleId)?.Rule?.CarrierRuleId;
                var chosen = string.IsNullOrEmpty(wanted) ? candidates : candidates.Where(c => c.RuleId == wanted).ToList();
                var chosenMissing = !string.IsNullOrEmpty(wanted) && chosen.Count == 0 && candidates.Count > 0;
                if (chosenMissing) chosen = candidates; // the chosen one was removed from this door: fall back to the first
                var carrier = chosen
                    .OrderBy(c => c.IsMirrorCopy == p.IsMirrorCopy ? 0 : 1)
                    .FirstOrDefault();

                string why = null;
                if (def.Carriers.Count == 0) why ="no carrier component is chosen (Components page)";
                else if (string.IsNullOrWhiteSpace(def.CarrierParameterLeft) || string.IsNullOrWhiteSpace(def.CarrierParameterRight))
                    why = "the left/right parameters of " + carrierName + " are not set (Components page)";
                else if (carrier == null) why = "this door set has no " + carrierName;

                if (why == null)
                {
                    // Which side of the door the component is on (along the wall), falling back to the latch side.
                    var along = (p.Position - door.Origin).Dot(w);
                    var towards = Math.Abs(along) > 1.0 ? w * Math.Sign(along) : w * -hingeSign;
                    // Right, for a viewer facing the carrier's front: up × facing.
                    var right = Vec3.UnitZ.Cross(carrier.Facing.Flatten().Normalize());
                    var isRight = towards.Dot(right) > 0;
                    if (def.SwapCarrierSides) isRight = !isRight;

                    p.CarrierSlotKey = carrier.SlotKey;
                    p.CarrierLabel = carrier.Label;
                    p.CarrierParameter = isRight ? def.CarrierParameterRight.Trim() : def.CarrierParameterLeft.Trim();
                    p.CarrierOtherParameter = isRight ? def.CarrierParameterLeft.Trim() : def.CarrierParameterRight.Trim();
                    p.FamilyName = null;
                    p.TypeName = null;
                    p.Explanation += " → built into " + carrier.Label + ": “" + p.CarrierParameter + "” on (" +
                                     (isRight ? "right" : "left") + " of the " + carrier.Label + " seen from its front)";
                    if (chosenMissing)
                        p.Issues.Add(new StatusIssue(IssueSeverity.Info, IssueCodes.BuiltInFallback,
                            p.Label + ": the " + carrierName + " chosen to carry it is not on this door; built into " + carrier.Label + " instead.", p.SlotKey));
                    continue;
                }

                if (def.UseOwnFamilyAsBackup && def.IsFamilyConfigured)
                {
                    p.Issues.Add(new StatusIssue(IssueSeverity.Info, IssueCodes.BuiltInFallback,
                        p.Label + ": placed as its own family because " + why + ".", p.SlotKey));
                }
                else
                {
                    p.Issues.Add(new StatusIssue(IssueSeverity.Error, IssueCodes.BuiltInUnavailable,
                        p.Label + ": cannot be built into " + carrierName + " because " + why +
                        (def.IsFamilyConfigured ? "" : ". Map its own family as a backup") + ".", p.SlotKey));
                }
            }
        }

        private static string Explain(CalculatedPlacement p, PlacementRule rule, double mount)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append(p.Label).Append(": ");
            switch (rule.Reference)
            {
                case PlacementReference.HingeJamb: sb.Append("hinge jamb"); break;
                case PlacementReference.LatchJamb: sb.Append("latch jamb"); break;
                default: sb.Append("door centre"); break;
            }
            if (Math.Abs(rule.AlongWallOffsetMm) > 0.01)
                sb.Append(rule.AlongWallOffsetMm > 0 ? " +" : " ").Append(rule.AlongWallOffsetMm.ToString("0", inv)).Append(" mm");

            sb.Append(", ");
            switch (p.Side)
            {
                case ResolvedSide.SideA: sb.Append("side A"); break;
                case ResolvedSide.SideB: sb.Append("side B"); break;
                default: sb.Append("in wall"); break;
            }
            if (rule.Side == PlacementSide.UnsecuredSide) sb.Append(" (unsecured)");
            else if (rule.Side == PlacementSide.SecuredSide) sb.Append(" (secured)");
            if (p.IsMirrorCopy) sb.Append(" – mirrored copy for two-way access");

            if (Math.Abs(rule.FromWallOffsetMm) > 0.01)
                sb.Append(", ").Append(rule.FromWallOffsetMm.ToString("0", inv)).Append(p.Side == ResolvedSide.InWall ? " mm from wall centre" : " mm from wall face");

            if (p.WallClearanceMm > 0.5)
                sb.Append(", moved ").Append(p.WallClearanceMm.ToString("0", inv)).Append(" mm out of a wall in front of the door's wall face");

            sb.Append(", h=").Append(mount.ToString("0", inv)).Append(" mm ")
              .Append(rule.HeightReference == HeightReference.DoorTop ? "above door head" : "above door bottom");

            switch (rule.Orientation)
            {
                case OrientationMode.FaceTowardDoor: sb.Append(", faces the door"); break;
                case OrientationMode.FollowWall: sb.Append(", follows wall"); break;
                case OrientationMode.Fixed: sb.Append(", fixed ").Append(rule.RotationDeg.ToString("0.#", inv)).Append("°"); break;
                default: sb.Append(", faces away from wall"); break;
            }
            return sb.ToString();
        }

        public static string HingeText(HingeSide h) =>
            h == HingeSide.PositiveWidthAxis ? "hinge on the +width side" :
            h == HingeSide.NegativeWidthAxis ? "hinge on the -width side" : "unknown";

        /// <summary>
        /// Deterministic hash of the placements (rounded to 1 mm / 0.1°) — detects when the calculated result
        /// differs from what was placed (template edit, override, flip, source change).
        /// </summary>
        public static string ComputeHash(IEnumerable<CalculatedPlacement> placements)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            foreach (var p in placements.OrderBy(x => x.SlotKey, StringComparer.Ordinal))
            {
                sb.Append(p.SlotKey).Append('|')
                  .Append(p.ComponentDefinitionId).Append('|')
                  .Append(p.FamilyName).Append('|').Append(p.TypeName).Append('|')
                  .Append(p.HostBehavior).Append('|')
                  .Append(Math.Round(p.Position.X).ToString("0", inv)).Append(',')
                  .Append(Math.Round(p.Position.Y).ToString("0", inv)).Append(',')
                  .Append(Math.Round(p.Position.Z).ToString("0", inv)).Append('|')
                  .Append(Math.Round(p.InstanceRotationDeg, 1).ToString("0.0", inv));
                // Only built-in components add fields, so hashes of existing placements stay the same.
                if (p.IsBuiltIn) sb.Append("|in:").Append(p.CarrierSlotKey).Append('|').Append(p.CarrierParameter);
                sb.Append(';');
            }
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(bytes).Replace("-", "").Substring(0, 16);
            }
        }
    }
}
