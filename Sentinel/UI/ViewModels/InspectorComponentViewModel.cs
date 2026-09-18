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
        }

        public string RuleId { get; }
        public string Label { get; }
        public string ComponentName { get; }
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
        public PlacementRuleEditor Editor { get; }
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
            else if (manual > 0) { StateKind = "manual"; StateGlyph = "✎"; StateText = "Manually modified"; }
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

            var added = AddedSlot;
            if (added != null)
            {
                added.Rule = edited;
                if (EditComponent != null) added.ComponentDefinitionId = EditComponent.Id;
                inst.Touch();
            }
            else
            {
                var template = TemplateSlot;
                if (template == null) return;
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
                if (EditComponent != null && EditComponent.Id != template.ComponentDefinitionId) ov.ComponentDefinitionId = EditComponent.Id;

                DoorSetInstanceOperations.ClearRuleOverride(inst, RuleId);
                if (!ov.IsEmpty) DoorSetInstanceOperations.OverrideRule(inst, ov);
            }

            _owner.SetEditing(null);
            _owner.Changed("override " + Label);
            _owner.Main.SetStatus(Label + " updated for this door" + (inst.HasPlacedElements ? " – use Update placement to apply it in the model." : "."));
        }

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
