using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.Core.Status;
using Sentinel.UI.Mvvm;
using Sentinel.UI.Services;

namespace Sentinel.UI.ViewModels
{
    public class IssueItem
    {
        public IssueSeverity Severity { get; set; }
        public string Message { get; set; }
        public string Glyph => Severity == IssueSeverity.Error ? "✗" : Severity == IssueSeverity.Warning ? "⚠" : "ℹ";
    }

    public class SetChoice
    {
        public DoorSetDefinition Definition { get; set; }
        public string Text => Definition == null ? "(no set)" : Definition.DisplayName;
    }

    /// <summary>Detail panel for one door: set, status + reasons, direction, components, overrides and source data.</summary>
    public class DoorInspectorViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly DoorsViewModel _doors;
        private DoorRowViewModel _row;
        private bool _loading;
        private SetChoice _selectedSet;
        private Option<AccessDirection> _selectedDirection;
        private ComponentDefinition _selectedAdd;
        private string _multiText;
        private string _editingRuleId;

        public DoorInspectorViewModel(MainViewModel main, DoorsViewModel doors)
        {
            _main = main;
            _doors = doors;

            FlipCommand = new RelayCommand(Flip, () => CanEditInstance && Instance.AccessDirection != AccessDirection.Both);
            SwapHingeCommand = new RelayCommand(SwapHinge, () => CanEditInstance);
            AddComponentCommand = new RelayCommand(AddComponent, () => CanEditInstance && SelectedAdd != null);
            AcceptSourceCommand = new RelayCommand(AcceptSource, () => CanEditInstance && SourceCheck?.Current != null && SourceCheck.Comparison != null && SourceCheck.Comparison.HasChanges);
            ZoomCommand = new RelayCommand(() => _doors.Zoom(_row), () => _row != null);
            SelectComponentsCommand = new RelayCommand(() => _doors.SelectComponents(_row), () => _row != null && _row.HasPlacedElements);
            SelectSourceCommand = new RelayCommand(() => _doors.SelectSourceDoor(_row), () => _row != null);
            PreviewCommand = new RelayCommand(() => _doors.Preview.Start(new List<DoorRowViewModel> { _row }), () => CanEditInstance && _row.IsAssigned);
            PlaceCommand = new RelayCommand(Place, () => CanEditInstance && _row.IsAssigned && !_row.HasPlacedElements);
            UpdateCommand = new RelayCommand(() => _doors.UpdatePlacement(new[] { _row }, true), () => CanEditInstance && _row.IsAssigned && _row.HasPlacedElements);
            DeleteComponentsCommand = new RelayCommand(DeleteComponents, () => CanEditInstance && _row.HasPlacedElements);
            MarkReviewedCommand = new RelayCommand(MarkReviewed, () => CanEditInstance && _row.IsAssigned && Instance.ReviewState != ReviewState.Reviewed);
        }

        private SentinelProject Project => _main.Session.Project;
        public DoorSetInstance Instance => _row?.Instance;
        private bool CanEditInstance => _main.IsEditable && !_main.IsBusy && Instance != null && !_doors.Preview.IsActive;
        private SourceCheck SourceCheck => _main.Session.Check(Instance);

        public bool HasRow => _row != null;
        public bool HasInstance => Instance != null && !string.IsNullOrEmpty(Instance.DefinitionId);
        public string MultiSelectText { get => _multiText; private set => Set(ref _multiText, value); }
        public bool IsEditable => _main.IsEditable;

        public RelayCommand FlipCommand { get; }
        public RelayCommand SwapHingeCommand { get; }
        public RelayCommand AddComponentCommand { get; }
        public RelayCommand AcceptSourceCommand { get; }
        public RelayCommand ZoomCommand { get; }
        public RelayCommand SelectComponentsCommand { get; }
        public RelayCommand SelectSourceCommand { get; }
        public RelayCommand PreviewCommand { get; }
        public RelayCommand PlaceCommand { get; }
        public RelayCommand UpdateCommand { get; }
        public RelayCommand DeleteComponentsCommand { get; }
        public RelayCommand MarkReviewedCommand { get; }

        // ---- header ----
        public string Title { get; private set; }
        public string Subtitle { get; private set; }
        public string RoomsText { get; private set; }
        public SetStatus Status { get; private set; }
        public string StatusText { get; private set; }
        public string ReviewText { get; private set; }
        public ObservableCollection<IssueItem> Issues { get; } = new ObservableCollection<IssueItem>();
        public bool HasIssues => Issues.Count > 0;

        // ---- set ----
        public ObservableCollection<SetChoice> SetChoices { get; } = new ObservableCollection<SetChoice>();

        public SetChoice SelectedSet
        {
            get => _selectedSet;
            set
            {
                if (!Set(ref _selectedSet, value) || _loading || _row == null || value == null) return;
                ChangeSet(value.Definition);
            }
        }

        // ---- direction / hinge ----
        public ObservableCollection<Option<AccessDirection>> DirectionChoices { get; } = new ObservableCollection<Option<AccessDirection>>();

        public Option<AccessDirection> SelectedDirection
        {
            get => _selectedDirection;
            set
            {
                if (!Set(ref _selectedDirection, value) || _loading || Instance == null || value == null) return;
                if (Instance.AccessDirection == value.Value) return;
                Instance.AccessDirection = value.Value;
                Instance.Touch();
                Changed("access direction");
            }
        }

        public string AccessText { get; private set; }
        public string HingeText { get; private set; }

        // ---- components ----
        public ObservableCollection<InspectorComponentViewModel> Components { get; } = new ObservableCollection<InspectorComponentViewModel>();
        public ObservableCollection<ComponentDefinition> AddChoices { get; } = new ObservableCollection<ComponentDefinition>();
        public ComponentDefinition SelectedAdd { get => _selectedAdd; set => Set(ref _selectedAdd, value); }
        public string OverridesText { get; private set; }

        // ---- source ----
        public string LinkText { get; private set; }
        public string TypeText { get; private set; }
        public string LevelText { get; private set; }
        public string SizeText { get; private set; }
        public string WallText { get; private set; }
        public string GuidText { get; private set; }
        public string GeometryText { get; private set; }
        public string ParametersText { get; private set; }
        public string SourceChangeText { get; private set; }
        public bool HasSourceChange => !string.IsNullOrEmpty(SourceChangeText);
        public string ReadWarningsText { get; private set; }

        public string Notes
        {
            get => Instance?.Notes ?? "";
            set
            {
                if (Instance == null || _loading || Instance.Notes == value) return;
                Instance.Notes = value;
                Instance.Touch();
                _main.MarkDirty("notes");
                OnPropertyChanged();
            }
        }

        // ------------------------------------------------------------------ load

        public void Load(DoorRowViewModel row, int selectionCount = 1)
        {
            _loading = true;
            try
            {
                if (_row != row) _editingRuleId = null;
                _row = row;
                MultiSelectText = row == null
                    ? (selectionCount > 1 ? selectionCount + " doors selected – use the toolbar to assign, preview or place them together." : "Select a door to see its details.")
                    : null;

                Issues.Clear();
                Components.Clear();
                SetChoices.Clear();
                DirectionChoices.Clear();
                AddChoices.Clear();

                if (row == null)
                {
                    RefreshAll();
                    return;
                }

                var src = row.Source;
                var inst = row.Instance;
                var def = inst != null ? Project.FindDoorSet(inst.DefinitionId) : null;

                Title = row.Mark;
                Subtitle = string.Join(" • ", new[] { src?.TypeName, src?.LevelName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                RoomsText = "Side A: " + SentinelSession.SideName(src, true) + "     Side B: " + SentinelSession.SideName(src, false);
                Status = row.Status;
                StatusText = row.StatusText;
                ReviewText = inst == null || string.IsNullOrEmpty(inst.DefinitionId) ? "" :
                    inst.ReviewState == ReviewState.Reviewed ? "Reviewed" + (string.IsNullOrEmpty(inst.ReviewedBy) ? "" : " by " + inst.ReviewedBy) :
                    inst.ReviewState == ReviewState.NeedsReview ? "Needs review" : "Not reviewed";
                foreach (var i in row.Evaluation.Issues.OrderByDescending(x => x.Severity))
                    Issues.Add(new IssueItem { Severity = i.Severity, Message = i.Message });
                if (!string.IsNullOrWhiteSpace(inst?.LastError) && !Issues.Any(i => i.Message.Contains(inst.LastError)))
                    Issues.Add(new IssueItem { Severity = IssueSeverity.Error, Message = "Last placement: " + inst.LastError });

                SetChoices.Add(new SetChoice());
                foreach (var d in Project.DoorSetDefinitions.OrderBy(d => d.Code, StringComparer.OrdinalIgnoreCase)) SetChoices.Add(new SetChoice { Definition = d });
                _selectedSet = SetChoices.FirstOrDefault(c => c.Definition != null && c.Definition == def) ?? SetChoices[0];

                var a = SentinelSession.SideName(src, true);
                var b = SentinelSession.SideName(src, false);
                DirectionChoices.Add(new Option<AccessDirection>(AccessDirection.SideAToSideB, a + " → " + b));
                DirectionChoices.Add(new Option<AccessDirection>(AccessDirection.SideBToSideA, b + " → " + a));
                DirectionChoices.Add(new Option<AccessDirection>(AccessDirection.Both, "Both ways (" + a + " ↔ " + b + ")"));
                _selectedDirection = inst != null ? DirectionChoices.First(o => o.Value == inst.AccessDirection) : DirectionChoices[0];
                AccessText = inst != null ? SentinelSession.AccessText(src, inst.AccessDirection) : "";

                var plan = _main.Session.Plan(inst);
                var geometry = _main.Session.GeometryFor(inst) ?? row.Door?.Geometry;
                var hingeSource = inst?.HingeSideOverride != null ? "per-door override" :
                    plan != null && plan.HingeAssumed ? "assumed – check the preview" :
                    geometry?.Source == DoorGeometrySource.FamilyInstance ? "from door family" : "unknown";
                HingeText = plan != null
                    ? DoorSetPlacementCalculator.HingeText(plan.ResolvedHinge) + " (" + hingeSource + ")"
                    : geometry != null ? DoorSetPlacementCalculator.HingeText(geometry.Hinge) : "";

                if (inst != null && def != null) BuildComponents(inst, def, plan);
                foreach (var c in Project.ComponentDefinitions.OrderBy(c => c.Name)) AddChoices.Add(c);
                if (SelectedAdd == null || !AddChoices.Contains(SelectedAdd)) SelectedAdd = AddChoices.FirstOrDefault();
                OverridesText = inst?.Overrides != null && !inst.Overrides.IsEmpty
                    ? inst.Overrides.Count + " per-door override(s) – base set " + (def?.Code ?? "?")
                    : "No per-door overrides";

                // Source
                LinkText = src?.LinkName ?? src?.LinkDocumentTitle ?? "";
                TypeText = string.Join(" : ", new[] { src?.FamilyName, src?.TypeName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                LevelText = src?.LevelName ?? "";
                SizeText = geometry != null ? Mm(geometry.WidthMm) + " × " + Mm(geometry.HeightMm) + " mm" : "unknown";
                WallText = geometry != null && geometry.WallThicknessMm > 0 ? Mm(geometry.WallThicknessMm) + " mm" : "unknown";
                GuidText = string.IsNullOrWhiteSpace(src?.IfcGlobalId) ? "—" : src.IfcGlobalId;
                GeometryText = geometry == null ? "not available" :
                    geometry.Source == DoorGeometrySource.EstimatedFromGeometry ? "estimated from IFC geometry" : "from door family";
                ParametersText = src?.LastKnownParameters != null && src.LastKnownParameters.Count > 0
                    ? string.Join("\n", src.LastKnownParameters.Select(kv => kv.Key + ": " + kv.Value))
                    : "—";
                var sc = SourceCheck;
                SourceChangeText = sc?.Comparison != null && sc.Comparison.HasChanges ? string.Join("; ", sc.Comparison.Details) : null;
                ReadWarningsText = row.Door != null && row.Door.ReadWarnings.Count > 0 ? string.Join(" ", row.Door.ReadWarnings) : null;

                RefreshAll();
            }
            finally
            {
                _loading = false;
            }
            RelayCommand.Requery();
        }

        private void BuildComponents(DoorSetInstance inst, DoorSetDefinition def, PlacementPlan plan)
        {
            var effective = EffectiveSetResolver.Resolve(def, inst.Overrides, Project.FindComponent);
            foreach (var e in effective)
            {
                var slots = plan?.Placements.Where(p => p.RuleId == e.RuleId).ToList() ?? new List<CalculatedPlacement>();
                var records = inst.Components.Where(c => c.SlotKey == e.RuleId || c.SlotKey == e.RuleId + DoorSetPlacementCalculator.MirrorSuffix).ToList();
                var vm = new InspectorComponentViewModel(this, e, slots, records, removed: false);
                if (vm.RuleId == _editingRuleId) vm.BeginEdit();
                Components.Add(vm);
            }

            // Template components removed on this door (can be restored).
            foreach (var removedId in inst.Overrides?.RemovedRuleIds ?? new List<string>())
            {
                var slot = def.Components.FirstOrDefault(c => c.Id == removedId);
                if (slot == null) continue;
                var e = new EffectiveComponent
                {
                    RuleId = slot.Id,
                    Label = slot.Label,
                    Component = Project.FindComponent(slot.ComponentDefinitionId),
                    ComponentDefinitionId = slot.ComponentDefinitionId,
                    Rule = slot.Rule
                };
                var records = inst.Components.Where(c => c.SlotKey == e.RuleId || c.SlotKey == e.RuleId + DoorSetPlacementCalculator.MirrorSuffix).ToList();
                Components.Add(new InspectorComponentViewModel(this, e, new List<CalculatedPlacement>(), records, removed: true));
            }
        }

        private static string Mm(double v) => v.ToString("0", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ edits

        /// <summary>Common tail of every edit: mark dirty and refresh row + panel (statuses are recomputed).</summary>
        internal void Changed(string reason)
        {
            _main.MarkDirty(reason);
            _doors.UpdateRow(_row);
            _doors.Preview.OnInstanceEdited(Instance);
        }

        internal void SetEditing(string ruleId) => _editingRuleId = ruleId;

        private void ChangeSet(DoorSetDefinition def)
        {
            if (def == null)
            {
                if (Instance == null) return;
                if (Instance.HasPlacedElements)
                {
                    _main.SetStatus("This set has placed components – use 'Remove set' in the toolbar to remove it and delete the components.", true);
                    Load(_row);
                    return;
                }
                Project.DoorSetInstances.Remove(Instance);
                _row.Instance = null;
                _main.MarkDirty("unassign");
                _doors.UpdateRows();
                return;
            }

            var inst = _doors.EnsureInstance(_row, def);
            if (inst == null) return;
            var wasPlaced = inst.HasPlacedElements && inst.DefinitionId != def.Id;
            DoorSetInstanceOperations.AssignDefinition(inst, def);
            Changed("change set");
            if (wasPlaced) _main.SetStatus("Set changed to " + def.DisplayName + " – use Update placement to apply it to the placed components.");
        }

        private void Flip()
        {
            if (DoorSetInstanceOperations.Flip(Instance))
            {
                Changed("flip set");
                _main.SetStatus("Set flipped: " + SentinelSession.AccessText(_row.Source, Instance.AccessDirection) +
                                (Instance.HasPlacedElements ? " – use Update placement to move the placed components." : "."));
            }
        }

        private void SwapHinge()
        {
            var plan = _main.Session.Plan(Instance);
            var resolved = plan?.ResolvedHinge ?? HingeSide.NegativeWidthAxis;
            DoorSetInstanceOperations.SwapHinge(Instance, resolved);
            Changed("swap hinge");
        }

        private void AddComponent()
        {
            var slot = DoorSetInstanceOperations.AddComponent(Instance, SelectedAdd);
            if (slot == null) return;
            _editingRuleId = null;
            Changed("add component");
            _main.SetStatus(SelectedAdd.Name + " added to " + _row.Mark + (Instance.HasPlacedElements ? " – use Update placement to place it." : "."));
        }

        private void AcceptSource()
        {
            var sc = SourceCheck;
            if (sc?.Current == null) return;
            DoorSetInstanceOperations.AcceptSourceChanges(Instance, sc.Current.Current);
            sc.Comparison = new SourceComparison();
            sc.State = SourceState.Ok;
            Changed("accept source changes");
            if (Instance.HasPlacedElements) _main.SetStatus("Source changes accepted – use Update placement to move the components to the new door position.");
        }

        private void Place()
        {
            var id = Instance.Id;
            _doors.RunPlacement(new List<string> { id }, PlacementMode.Batch, false, "Placing " + _row.Mark + "…", null);
        }

        private void DeleteComponents()
        {
            var n = Instance.Components.Count(c => !string.IsNullOrEmpty(c.ElementUniqueId));
            if (!_main.Dialogs.Confirm("Delete placed components",
                    "Delete the " + n + " placed component(s) of " + _row.Mark + "? The set assignment and its overrides are kept, so it can be placed again.",
                    null, "Delete", "Cancel")) return;
            _main.CancelPendingSave();
            _main.BeginBusy("Deleting components…");
            _main.Host.DeletePlacedComponents(new List<string> { Instance.Id }, false, r =>
            {
                _main.EndBusy();
                _main.SetStatus(r.Message, !r.Success);
                _doors.UpdateRows();
            });
        }

        private void MarkReviewed()
        {
            Instance.ReviewState = ReviewState.Reviewed;
            Instance.ReviewedBy = Environment.UserName;
            Instance.ReviewedUtc = DateTime.UtcNow.ToString("o");
            Changed("mark reviewed");
        }

        // ------------------------------------------------------------------ helpers for component rows

        internal SentinelProject ProjectData => Project;
        internal DoorSetDefinition Definition => Project.FindDoorSet(Instance?.DefinitionId);
        internal MainViewModel Main => _main;
    }
}
