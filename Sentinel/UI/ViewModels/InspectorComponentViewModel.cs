using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.UI.Mvvm;

namespace Sentinel.UI.ViewModels
{
    /// <summary>
    /// One component of the inspected door: state (placed / missing / manual / failed / planned), why it is where it
    /// is, and inline editing of per-door overrides.
    /// </summary>
    public class InspectorComponentViewModel : ObservableObject
    {
        private readonly DoorInspectorViewModel _owner;
        private readonly EffectiveComponent _effective;
        private readonly List<PlacedComponentInstance> _records;
        private bool _isEditing;
        private ComponentDefinition _editComponent;

        public InspectorComponentViewModel(DoorInspectorViewModel owner, EffectiveComponent effective,
            List<CalculatedPlacement> slots, List<PlacedComponentInstance> records, bool removed)
        {
            _owner = owner;
            _effective = effective;
            _records = records;
            IsRemoved = removed;

            RuleId = effective.RuleId;
            Label = effective.DisplayLabel;
            ComponentName = effective.Component != null ? effective.Component.Name + " – " + effective.Component.FamilyDisplay : "(missing component definition)";
            var builtIn = slots.FirstOrDefault(s => s.IsBuiltIn);
            IsBuiltIn = builtIn != null;
            IsFamilyMissing = effective.Component == null || !effective.Component.IsModelled;
            FamilyText = effective.Component == null ? "Component definition missing" :
                         builtIn != null ? "Built into " + builtIn.CarrierLabel + " · “" + builtIn.CarrierParameter + "” switched on" :
                         effective.Component.IsFamilyConfigured ? effective.Component.Name + " · " + effective.Component.FamilyName + " : " + effective.Component.TypeName :
                         effective.Component.Name + " · no family mapped";
            Summary = UiChoices.RuleSummary(effective.Rule, effective.Component?.DefaultMountingHeightMm);
            IsAdded = effective.IsAddedByOverride;
            IsOverridden = effective.IsOverridden;
            Explanation = string.Join("\n", slots.Select(s => s.Explanation));
            IssuesText = string.Join("\n", slots.SelectMany(s => s.Issues).Select(i => i.Message).Distinct());
            PlacementNotes = string.Join("\n", records.Select(r => r.PlacementNote).Where(n => !string.IsNullOrEmpty(n)));
            ComputeState(slots);

            Editor = new PlacementRuleEditor();
            EditCommand = new RelayCommand(() => { if (IsEditing) CancelEdit(); else BeginEdit(); }, () => _owner.IsEditable && !IsRemoved);
            ApplyCommand = new RelayCommand(Apply, () => Editor.IsValid && _editComponent != null);
            ResetCommand = new RelayCommand(Reset, () => IsOverridden && !IsAdded);
            RemoveCommand = new RelayCommand(Remove, () => _owner.IsEditable && !IsRemoved);
            RestoreCommand = new RelayCommand(Restore, () => _owner.IsEditable && IsRemoved);
            AcceptManualCommand = new RelayCommand(AcceptManual, () => _owner.IsEditable && _records.Any(r => r.State == ComponentState.ManuallyModified));
            UseForDoorCommand = new RelayCommand(UseForDoor, () => _owner.IsEditable && IsMovedInModel);
            UseForSetCommand = new RelayCommand(UseForSet, () => _owner.IsEditable && IsMovedInModel && CanUseForSet);
        }

        public RelayCommand UseForDoorCommand { get; }
        public RelayCommand UseForSetCommand { get; }

        public string RuleId { get; }
        public string Label { get; }
        public string ComponentName { get; }

        /// <summary>"Card Reader · Family : Type" or "… · no family mapped".</summary>
        public string FamilyText { get; }
        public bool IsFamilyMissing { get; }

        /// <summary>Part of another component's family (switched on by a parameter), no element of its own.</summary>
        public bool IsBuiltIn { get; }

        /// <summary>One-line placement summary for the collapsed row.</summary>
        public string Summary { get; }
        public bool IsAdded { get; }
        public bool IsRemoved { get; }
        public bool IsOverridden { get; }
        public string Explanation { get; }
        public string IssuesText { get; }
        public bool HasIssues => !string.IsNullOrEmpty(IssuesText);
        public string PlacementNotes { get; }

        /// <summary>ok / manual / warn / error / planned / removed – drives the glyph colour.</summary>
        public string StateKind { get; private set; }
        public string StateGlyph { get; private set; }
        public string StateText { get; private set; }

        public string Tags
        {
            get
            {
                var t = new List<string>();
                if (IsAdded) t.Add("added on this door");
                if (IsOverridden && !IsAdded) t.Add("overridden");
                if (IsRemoved) t.Add("removed on this door");
                return string.Join(" • ", t);
            }
        }

        public RelayCommand EditCommand { get; }
        public RelayCommand ApplyCommand { get; }
        public RelayCommand ResetCommand { get; }
        public RelayCommand RemoveCommand { get; }
        public RelayCommand RestoreCommand { get; }
        public RelayCommand AcceptManualCommand { get; }

        public bool IsEditing { get => _isEditing; private set => Set(ref _isEditing, value); }
        public PlacementRuleEditor Editor { get; private set; }

        /// <summary>
        /// Continues an edit that was open before the inspector refreshed: the same editor object keeps the typed
        /// (possibly invalid, not yet applied) values, validation state and advanced-section state.
        /// </summary>
        internal void ResumeEdit(InspectorComponentViewModel previous)
        {
            Editor = previous.Editor;
            EditComponent = previous.EditComponent;
            IsEditing = true;
            OnPropertyChanged(nameof(Editor));
        }
        public List<ComponentDefinition> ComponentChoices => _owner.ProjectData.ComponentDefinitions.OrderBy(c => c.Name).ToList();
        public ComponentDefinition EditComponent { get => _editComponent; set => Set(ref _editComponent, value); }

        private void ComputeState(List<CalculatedPlacement> slots)
        {
            if (IsRemoved)
            {
                var stillPlaced = _records.Any(r => !string.IsNullOrEmpty(r.ElementUniqueId) && r.State != ComponentState.Missing);
                StateKind = "removed";
                StateGlyph = "–";
                StateText = stillPlaced ? "Removed – element still in model (Update placement deletes it)" : "Removed on this door";
                return;
            }

            var expected = Math.Max(1, slots.Count);
            var missing = _records.Count(r => r.State == ComponentState.Missing);
            var failed = _records.Count(r => r.State == ComponentState.Failed);
            var manual = _records.Count(r => r.State == ComponentState.ManuallyModified);
            var placed = _records.Count(r => (r.State == ComponentState.Placed || r.State == ComponentState.ManuallyModified) && !string.IsNullOrEmpty(r.ElementUniqueId));

            if (missing > 0) { StateKind = "error"; StateGlyph = "⚠"; StateText = "Missing (element deleted)"; }
            else if (failed > 0) { StateKind = "error"; StateGlyph = "✗"; StateText = "Failed: " + (_records.First(r => r.State == ComponentState.Failed).LastError ?? ""); }
            else if (manual > 0)
            {
                StateKind = "manual";
                StateGlyph = "✎";
                // Built in: it has no element of its own and simply went where its carrier was moved.
                StateText = IsBuiltIn ? "Moved with " + (slots.FirstOrDefault(s => s.IsBuiltIn)?.CarrierLabel ?? "its carrier") : "Manually modified";
            }
            else if (placed >= expected) { StateKind = "ok"; StateGlyph = "✓"; StateText = "Placed"; }
            else if (placed > 0) { StateKind = "warn"; StateGlyph = "◐"; StateText = placed + " of " + expected + " placed"; }
            else if (slots.Any(s => s.HasErrors)) { StateKind = "error"; StateGlyph = "✗"; StateText = "Cannot be placed"; }
            else { StateKind = "planned"; StateGlyph = "○"; StateText = "Not placed yet"; }
        }

        // ------------------------------------------------------------------ editing

        private SetComponentRule TemplateSlot => _owner.Definition?.Components.FirstOrDefault(c => c.Id == RuleId);
        private SetComponentRule AddedSlot => _owner.Instance?.Overrides?.AddedComponents.FirstOrDefault(c => c.Id == RuleId);

        public void BeginEdit()
        {
            // Show the template rule with this door's overrides applied (height blank = component default).
            var baseSlot = AddedSlot ?? TemplateSlot;
            var rule = (baseSlot?.Rule ?? new PlacementRule()).Clone();
            var ov = AddedSlot == null ? _owner.Instance?.Overrides?.Find(RuleId) : null;
            if (ov != null) Apply(ov, rule);
            Editor.Load(rule);
            EditComponent = _owner.ProjectData.FindComponent(_effective.ComponentDefinitionId);
            IsEditing = true;
            _owner.SetEditing(RuleId);
        }

        private void CancelEdit()
        {
            IsEditing = false;
            _owner.SetEditing(null);
        }

        private void Apply()
        {
            var edited = Editor.ToRule();
            if (edited == null || _owner.Instance == null) return;
            var inst = _owner.Instance;
            if (!SaveForDoor(edited, EditComponent)) return;

            _owner.SetEditing(null);
            _owner.Changed("override " + Label);
            _owner.Main.SetStatus(Label + " updated for this door" + (inst.HasPlacedElements ? " – use Update placement to apply it in the model." : "."));
        }

        /// <summary>Stores <paramref name="edited"/> for this door only: the added component's rule, or an override of the template.</summary>
        private bool SaveForDoor(PlacementRule edited, ComponentDefinition component)
        {
            var inst = _owner.Instance;
            var added = AddedSlot;
            if (added != null)
            {
                added.Rule = edited;
                if (component != null) added.ComponentDefinitionId = component.Id;
                inst.Touch();
            }
            else
            {
                var template = TemplateSlot;
                if (template == null) return false;
                var b = template.Rule ?? new PlacementRule();
                var ov = new ComponentRuleOverride { RuleId = RuleId };
                if (edited.Reference != b.Reference) ov.Reference = edited.Reference;
                if (edited.Side != b.Side) ov.Side = edited.Side;
                if (Math.Abs(edited.AlongWallOffsetMm - b.AlongWallOffsetMm) > 1e-6) ov.AlongWallOffsetMm = edited.AlongWallOffsetMm;
                if (Math.Abs(edited.FromWallOffsetMm - b.FromWallOffsetMm) > 1e-6) ov.FromWallOffsetMm = edited.FromWallOffsetMm;
                if (edited.MountingHeightMm.HasValue && (!b.MountingHeightMm.HasValue || Math.Abs(edited.MountingHeightMm.Value - b.MountingHeightMm.Value) > 1e-6))
                    ov.MountingHeightMm = edited.MountingHeightMm;
                if (edited.HeightReference != b.HeightReference) ov.HeightReference = edited.HeightReference;
                if (edited.Orientation != b.Orientation) ov.Orientation = edited.Orientation;
                if (Math.Abs(edited.RotationDeg - b.RotationDeg) > 1e-6) ov.RotationDeg = edited.RotationDeg;
                if (!string.IsNullOrEmpty(edited.CarrierRuleId) && edited.CarrierRuleId != b.CarrierRuleId) ov.CarrierRuleId = edited.CarrierRuleId; // keep this door's carrier choice
                if (component != null && component.Id != template.ComponentDefinitionId) ov.ComponentDefinitionId = component.Id;
                else if (component == null) ov.ComponentDefinitionId = inst.Overrides?.Find(RuleId)?.ComponentDefinitionId; // keep a swapped component

                DoorSetInstanceOperations.ClearRuleOverride(inst, RuleId);
                if (!ov.IsEmpty) DoorSetInstanceOperations.OverrideRule(inst, ov);
            }
            return true;
        }

        // ------------------------------------------------------------------ built in: which carrier on this door

        private List<Option<string>> _carrierChoices = new List<Option<string>>();

        /// <summary>This door's components that can carry it (e.g. both magnets); set by the panel after building rows.</summary>
        public List<Option<string>> CarrierChoices => _carrierChoices;

        /// <summary>Only when there is a real choice: built into another component and the door has 2+ of those.</summary>
        public bool ShowsCarrierChoice => _carrierChoices.Count > 1 && !IsRemoved;

        public Option<string> CarrierChoice
        {
            get => _carrierChoices.FirstOrDefault(o => o.Value == _effective.Rule?.CarrierRuleId) ?? _carrierChoices.FirstOrDefault();
            set
            {
                if (value == null || _owner.Instance == null || value.Value == CarrierChoice?.Value) return;
                var inst = _owner.Instance;
                var added = AddedSlot;
                if (added != null)
                {
                    added.Rule = (added.Rule ?? new PlacementRule()).Clone();
                    added.Rule.CarrierRuleId = value.Value;
                    inst.Touch();
                }
                else
                {
                    var template = TemplateSlot;
                    if (template == null) return;
                    var ov = inst.Overrides?.Find(RuleId)?.Clone() ?? new ComponentRuleOverride { RuleId = RuleId };
                    // Same as the set type (or its automatic first choice): no override needed.
                    var setChoice = template.Rule?.CarrierRuleId ?? _carrierChoices.FirstOrDefault()?.Value;
                    ov.CarrierRuleId = value.Value == setChoice ? null : value.Value;
                    DoorSetInstanceOperations.ClearRuleOverride(inst, RuleId);
                    if (!ov.IsEmpty) DoorSetInstanceOperations.OverrideRule(inst, ov);
                }
                _owner.Changed("choose carrier " + Label);
                _owner.Main.SetStatus(Label + " on this door is now built into " + value.Text.Split('·')[0].Trim() +
                                      (inst.HasPlacedElements ? " – use Update placement (or Place) to switch it in the model." : "."));
            }
        }

        internal void SetCarrierChoices(List<Option<string>> choices)
        {
            _carrierChoices = choices ?? new List<Option<string>>();
            OnPropertiesChanged(nameof(CarrierChoices), nameof(ShowsCarrierChoice), nameof(CarrierChoice));
        }

        // ------------------------------------------------------------------ position moved in the model

        /// <summary>A placed element of this component that was moved in Revit (kept or not yet decided).</summary>
        private PlacedComponentInstance MovedRecord =>
            IsBuiltIn ? null : _records.FirstOrDefault(r => r.ActualPosition.HasValue && !string.IsNullOrEmpty(r.ElementUniqueId) &&
                                                          (r.State == ComponentState.ManuallyModified || r.ManualPositionAccepted));

        public bool IsMovedInModel => MovedRecord != null && !IsRemoved;

        /// <summary>"Use for all DS-02 doors…" (only for components of the set type, not ones added on this door).</summary>
        public string UseForSetText => _owner.Definition == null ? "" : "Use for all " + _owner.Definition.Code + " doors…";
        public bool CanUseForSet => !IsAdded && TemplateSlot != null;

        private RuleFromPositionResult DeriveFromModel(out PlacementPlan before)
        {
            before = null;
            var inst = _owner.Instance;
            var rec = MovedRecord;
            if (inst == null || rec == null) return new RuleFromPositionResult { Error = "This component was not moved in the model." };
            before = _owner.Main.Session.Plan(inst);
            var slot = before?.Placements.FirstOrDefault(p => p.SlotKey == rec.SlotKey);
            if (slot == null) return new RuleFromPositionResult { Error = "The component is no longer part of this door's plan." };
            return RuleFromPosition.Derive(_effective.Rule ?? new PlacementRule(), slot, _owner.Main.Session.GeometryFor(inst), before.ResolvedHinge,
                rec.ActualPosition.Value, rec.ActualRotationDeg, _effective.Component?.DefaultMountingHeightMm);
        }

        private void UseForDoor()
        {
            PlacementPlan before;
            var r = DeriveFromModel(out before);
            if (!r.Success)
            {
                _owner.Main.SetStatus(Label + ": " + r.Error, true);
                return;
            }
            var inst = _owner.Instance;
            if (!SaveForDoor(r.Rule, null)) return;
            RuleFromPosition.AdoptPlacedPosition(inst, before, _owner.Main.Session.Plan(inst), RuleId);
            _owner.SetEditing(null);
            _owner.Changed("use model position " + Label);
            _owner.Main.SetStatus(Label + " on this door now follows its position in the model (" + r.ChangeText + ").");
        }

        private void UseForSet()
        {
            var def = _owner.Definition;
            var template = TemplateSlot;
            var inst = _owner.Instance;
            if (def == null || template == null || inst == null) return;
            PlacementPlan before;
            var r = DeriveFromModel(out before);
            if (!r.Success)
            {
                _owner.Main.SetStatus(Label + ": " + r.Error, true);
                return;
            }

            var project = _owner.ProjectData;
            var others = project.DoorSetInstances.Where(i => i != inst && i.DefinitionId == def.Id).ToList();
            var placedOthers = others.Count(i => i.HasPlacedElements);
            var withOwnOverride = others.Count(i => i.Overrides?.Find(RuleId) != null);
            var message = "Use this " + Label + " position on every " + def.DisplayName + " door?\n\n" + Capitalize(r.ChangeText) + "." +
                          (placedOthers > 0 ? "\n\n" + placedOthers + " other placed door(s) will show Modified. Use Update placement to move their " + Label + "." : "") +
                          (withOwnOverride > 0 ? "\n" + withOwnOverride + " door(s) with their own " + Label + " adjustment keep it." : "");
            _owner.Main.Dialogs.Confirm("Use position for " + def.Code, message, null, "Use for " + (others.Count + 1) + " door(s)", "Cancel", ok =>
            {
                if (!ok) return;
                template.Rule = r.Rule;
                def.Touch();
                DoorSetInstanceOperations.ClearRuleOverride(inst, RuleId); // this door now simply follows the set type
                RuleFromPosition.AdoptPlacedPosition(inst, before, _owner.Main.Session.Plan(inst), RuleId);
                _owner.SetEditing(null);
                _owner.Changed("use model position for " + def.Code);
                _owner.Main.Doors.UpdateRows();
                _owner.Main.SetStatus(def.Code + " " + Label + " updated from this door (" + r.ChangeText + ")" +
                                      (placedOthers > 0 ? " – use Update placement on the other " + def.Code + " doors." : "."));
            });
        }

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private void Reset()
        {
            DoorSetInstanceOperations.ClearRuleOverride(_owner.Instance, RuleId);
            _owner.SetEditing(null);
            _owner.Changed("reset override " + Label);
        }

        private void Remove()
        {
            DoorSetInstanceOperations.RemoveComponent(_owner.Instance, RuleId);
            _owner.SetEditing(null);
            _owner.Changed("remove " + Label);
            if (_records.Any(r => !string.IsNullOrEmpty(r.ElementUniqueId)))
                _owner.Main.SetStatus(Label + " removed from this door – use Update placement to delete its element.");
        }

        private void Restore()
        {
            DoorSetInstanceOperations.RestoreComponent(_owner.Instance, RuleId);
            _owner.Changed("restore " + Label);
        }

        private void AcceptManual()
        {
            foreach (var r in _records.Where(r => r.State == ComponentState.ManuallyModified))
                DoorSetInstanceOperations.AcceptManualPosition(r);
            DoorSetInstanceOperations.FollowCarriers(_owner.Instance); // a lock built into this component is accepted with it
            _owner.Changed("accept manual position");
        }

        private static void Apply(ComponentRuleOverride ov, PlacementRule rule)
        {
            if (ov.Reference.HasValue) rule.Reference = ov.Reference.Value;
            if (ov.Side.HasValue) rule.Side = ov.Side.Value;
            if (ov.AlongWallOffsetMm.HasValue) rule.AlongWallOffsetMm = ov.AlongWallOffsetMm.Value;
            if (ov.FromWallOffsetMm.HasValue) rule.FromWallOffsetMm = ov.FromWallOffsetMm.Value;
            if (ov.MountingHeightMm.HasValue) rule.MountingHeightMm = ov.MountingHeightMm.Value;
            if (ov.HeightReference.HasValue) rule.HeightReference = ov.HeightReference.Value;
            if (ov.Orientation.HasValue) rule.Orientation = ov.Orientation.Value;
            if (ov.RotationDeg.HasValue) rule.RotationDeg = ov.RotationDeg.Value;
        }
    }
}
