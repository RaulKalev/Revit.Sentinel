using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;

namespace Sentinel.Core.Status
{
    public class DoorSetStatusContext
    {
        public DoorSetInstance Instance { get; set; }
        public DoorSetDefinition Definition { get; set; }
        public SourceState SourceState { get; set; } = SourceState.NotChecked;
        public SourceComparison SourceComparison { get; set; }

        /// <summary>Current calculated plan (null when it could not be calculated).</summary>
        public PlacementPlan Plan { get; set; }

        public bool IsPreviewing { get; set; }
    }

    public class SetStatusResult
    {
        public SetStatus Status { get; set; }
        public List<StatusIssue> Issues { get; set; } = new List<StatusIssue>();

        public string PrimaryReason =>
            Issues.OrderByDescending(i => i.Severity).Select(i => i.Message).FirstOrDefault() ?? "";
    }

    /// <summary>
    /// Derives the grid status and the list of visible reasons from stored facts. Statuses are never stored as
    /// the source of truth; they are recomputed from components, source state and the current plan.
    /// </summary>
    public static class DoorSetStatusEvaluator
    {
        public static SetStatusResult Evaluate(DoorSetStatusContext ctx)
        {
            var r = new SetStatusResult();
            var inst = ctx.Instance;

            if (inst == null)
            {
                r.Status = SetStatus.Unassigned;
                return r;
            }
            if (inst.IsIgnored)
            {
                r.Status = SetStatus.Ignored;
                if (!string.IsNullOrWhiteSpace(inst.IgnoreReason))
                    r.Issues.Add(new StatusIssue(IssueSeverity.Info, "Ignored", "Ignored: " + inst.IgnoreReason));
                return r;
            }
            if (string.IsNullOrEmpty(inst.DefinitionId))
            {
                r.Status = SetStatus.Unassigned;
                return r;
            }

            var components = inst.Components ?? new List<PlacedComponentInstance>();
            var anyPlaced = components.Any(c => !string.IsNullOrEmpty(c.ElementUniqueId));
            var missing = components.Where(c => c.State == ComponentState.Missing).ToList();
            var failed = components.Where(c => c.State == ComponentState.Failed).ToList();
            var manual = components.Where(c => c.State == ComponentState.ManuallyModified && !c.ManualPositionAccepted).ToList();

            // ---- collect reasons ----
            if (ctx.Definition == null)
                r.Issues.Add(new StatusIssue(IssueSeverity.Error, IssueCodes.DefinitionMissing, "Assigned door set type no longer exists."));

            if (ctx.SourceState == SourceState.Missing)
                r.Issues.Add(new StatusIssue(IssueSeverity.Error, IssueCodes.SourceMissing, "Source door no longer available in the linked model."));
            else if (ctx.SourceState == SourceState.LinkUnavailable)
                r.Issues.Add(new StatusIssue(IssueSeverity.Error, IssueCodes.LinkUnavailable, "Source link is not loaded or was removed."));

            if (ctx.SourceComparison != null && ctx.SourceComparison.HasChanges)
                r.Issues.Add(new StatusIssue(IssueSeverity.Warning, IssueCodes.SourceChanged,
                    "Source door changed: " + string.Join("; ", ctx.SourceComparison.Details)));

            foreach (var c in missing)
                r.Issues.Add(new StatusIssue(IssueSeverity.Error, IssueCodes.ComponentMissing, Label(c) + " missing (element deleted).", c.SlotKey));
            foreach (var c in failed)
                r.Issues.Add(new StatusIssue(IssueSeverity.Error, IssueCodes.CreationFailed,
                    Label(c) + ": " + (string.IsNullOrWhiteSpace(c.LastError) ? "component creation failed." : c.LastError), c.SlotKey));
            foreach (var c in manual)
                r.Issues.Add(new StatusIssue(IssueSeverity.Info, IssueCodes.ManuallyModified, Label(c) + " manually modified.", c.SlotKey));

            var plan = ctx.Plan;
            var planErrors = new List<StatusIssue>();
            if (plan != null)
            {
                foreach (var i in plan.AllIssues)
                {
                    // Family / geometry problems only block when something still needs to be placed.
                    if (i.Severity == IssueSeverity.Error) planErrors.Add(i);
                    if (!r.Issues.Any(x => x.Code == i.Code && x.Message == i.Message)) r.Issues.Add(i);
                }
            }

            var configChanged = false;
            if (anyPlaced && plan != null && !plan.HasBlockingErrors)
            {
                var slots = new HashSet<string>(plan.Placements.Select(p => p.SlotKey));
                var haveSlots = new HashSet<string>(components.Select(c => c.SlotKey));
                var added = slots.Count(s => !haveSlots.Contains(s));
                var removed = haveSlots.Count(s => !slots.Contains(s));
                var hashDiffers = !string.IsNullOrEmpty(inst.PlacedConfigurationHash) && inst.PlacedConfigurationHash != plan.ConfigurationHash;
                configChanged = added > 0 || removed > 0 || hashDiffers;
                if (configChanged)
                {
                    var what = new List<string>();
                    if (added > 0) what.Add(added + " component(s) to add");
                    if (removed > 0) what.Add(removed + " component(s) to remove");
                    if (hashDiffers && added == 0 && removed == 0) what.Add("positions changed");
                    r.Issues.Add(new StatusIssue(IssueSeverity.Warning, IssueCodes.ConfigurationChanged,
                        "Configuration differs from placed components (" + string.Join(", ", what) + "). Use Update placement."));
                }
            }

            // ---- pick status (highest priority first) ----
            if (ctx.SourceState == SourceState.Missing) r.Status = SetStatus.Orphaned;
            else if (ctx.Definition == null || ctx.SourceState == SourceState.LinkUnavailable) r.Status = SetStatus.Error;
            else if (ctx.IsPreviewing) r.Status = SetStatus.Preview;
            else if (!anyPlaced)
                r.Status = failed.Count > 0 || planErrors.Count > 0 ? SetStatus.Error : SetStatus.Ready;
            else if (missing.Count > 0) r.Status = SetStatus.MissingComponent;
            else if (failed.Count > 0) r.Status = SetStatus.Error;
            else if (ctx.SourceComparison != null && ctx.SourceComparison.HasChanges) r.Status = SetStatus.SourceChanged;
            else if (manual.Count > 0 || configChanged) r.Status = SetStatus.Modified;
            else r.Status = SetStatus.Placed;

            return r;
        }

        private static string Label(PlacedComponentInstance c) =>
            string.IsNullOrWhiteSpace(c.Label) ? "Component" : c.Label;

        public static string StatusText(SetStatus s)
        {
            switch (s)
            {
                case SetStatus.MissingComponent: return "Missing Component";
                case SetStatus.SourceChanged: return "Source Changed";
                default: return s.ToString();
            }
        }

        /// <summary>Severity used for colouring the status in the UI.</summary>
        public static IssueSeverity Severity(SetStatus s)
        {
            switch (s)
            {
                case SetStatus.Error:
                case SetStatus.Orphaned:
                case SetStatus.MissingComponent:
                    return IssueSeverity.Error;
                case SetStatus.Modified:
                case SetStatus.SourceChanged:
                    return IssueSeverity.Warning;
                default:
                    return IssueSeverity.Info;
            }
        }
    }
}
