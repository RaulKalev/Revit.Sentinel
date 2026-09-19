using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;

namespace Sentinel.Core.Placement
{
    public enum DiffAction
    {
        /// <summary>No element yet (new, never placed, failed or deleted) → create.</summary>
        Create,
        /// <summary>Element exists, calculated position/rotation changed → move/rotate in place.</summary>
        Move,
        /// <summary>Element exists but its family/type/hosting changed → delete and create.</summary>
        Replace,
        /// <summary>Element exists but the slot is no longer part of the set → delete element and record.</summary>
        Remove,
        /// <summary>Element was moved manually; left untouched unless the user explicitly overwrites.</summary>
        KeepManual,
        Unchanged
    }

    public class DiffItem
    {
        public DiffAction Action { get; set; }
        public string SlotKey { get; set; }
        public string Label { get; set; }
        public PlacedComponentInstance Existing { get; set; }
        public CalculatedPlacement Target { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Plans how to bring an instance's placed elements in line with the current plan. Pure data: the executor
    /// applies it, the UI shows it for confirmation first.
    /// </summary>
    public static class PlacementDiff
    {
        public static List<DiffItem> Compute(DoorSetInstance instance, PlacementPlan plan, bool overwriteManual,
            double moveToleranceMm = 1.0, double rotationToleranceDeg = 0.5)
        {
            var items = new List<DiffItem>();
            var components = instance?.Components ?? new List<PlacedComponentInstance>();
            var inv = CultureInfo.InvariantCulture;
            var used = new HashSet<string>();

            foreach (var target in plan?.Placements ?? new List<CalculatedPlacement>())
            {
                var existing = components.FirstOrDefault(c => c.SlotKey == target.SlotKey);
                if (existing != null) used.Add(existing.Id);

                var item = new DiffItem { SlotKey = target.SlotKey, Label = target.Label, Existing = existing, Target = target };
                items.Add(item);

                if (existing == null || string.IsNullOrEmpty(existing.ElementUniqueId) ||
                    existing.State == ComponentState.Missing || existing.State == ComponentState.Failed ||
                    existing.State == ComponentState.Planned)
                {
                    item.Action = DiffAction.Create;
                    item.Reason = existing == null ? "new component" :
                        existing.State == ComponentState.Missing ? "element was deleted" :
                        existing.State == ComponentState.Failed ? "previous attempt failed" : "not placed yet";
                    continue;
                }

                // Switching between "own family" and "built into another component" replaces the component.
                var typeChanged = existing.ComponentDefinitionId != target.ComponentDefinitionId ||
                                  existing.IsBuiltIn != target.IsBuiltIn ||
                                  (!string.IsNullOrEmpty(existing.PlacedFamilyName) &&
                                   (existing.PlacedFamilyName != target.FamilyName || existing.PlacedTypeName != target.TypeName));

                var moved = !existing.CalculatedPosition.HasValue ||
                            existing.CalculatedPosition.Value.DistanceTo(target.Position) > moveToleranceMm ||
                            Angles.Difference(existing.CalculatedRotationDeg, target.InstanceRotationDeg) > rotationToleranceDeg ||
                            // Built in: another parameter must be switched on (side swapped, parameters renamed).
                            (target.IsBuiltIn && existing.PlacedHosting != BuiltInHosting.For(target.CarrierParameter));

                var isManual = existing.State == ComponentState.ManuallyModified || existing.ManualPositionAccepted;

                if (!typeChanged && !moved)
                {
                    item.Action = DiffAction.Unchanged;
                    item.Reason = isManual ? "unchanged (manual position kept)" : "unchanged";
                }
                else if (isManual && !overwriteManual)
                {
                    item.Action = DiffAction.KeepManual;
                    item.Reason = "manually modified – kept";
                }
                else if (typeChanged)
                {
                    item.Action = DiffAction.Replace;
                    item.Reason = "component type/family changed";
                }
                else
                {
                    var d = existing.CalculatedPosition.HasValue ? existing.CalculatedPosition.Value.DistanceTo(target.Position) : 0.0;
                    item.Action = DiffAction.Move;
                    item.Reason = "position changed " + d.ToString("0", inv) + " mm";
                }
            }

            foreach (var c in components.Where(c => !used.Contains(c.Id)))
            {
                items.Add(new DiffItem
                {
                    Action = DiffAction.Remove,
                    SlotKey = c.SlotKey,
                    Label = c.Label,
                    Existing = c,
                    Reason = "no longer part of the set"
                });
            }

            return items;
        }

        public static string Summarize(IEnumerable<DiffItem> items)
        {
            var list = items.ToList();
            var parts = new List<string>();
            void Add(DiffAction a, string text)
            {
                var n = list.Count(i => i.Action == a);
                if (n > 0) parts.Add(n + " " + text);
            }
            Add(DiffAction.Create, "to create");
            Add(DiffAction.Move, "to move");
            Add(DiffAction.Replace, "to replace");
            Add(DiffAction.Remove, "to delete");
            Add(DiffAction.KeepManual, "manual kept");
            return parts.Count == 0 ? "No changes." : string.Join(", ", parts);
        }
    }
}
