using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.UI.Mvvm;
using Sentinel.UI.Services;

namespace Sentinel.UI.ViewModels
{
    public class PreviewPlacementItem
    {
        public string Label { get; set; }
        public string Explanation { get; set; }
        public string IssuesText { get; set; }
        public bool HasErrors { get; set; }
        public bool HasIssues => !string.IsNullOrEmpty(IssuesText);
    }

    /// <summary>
    /// Preview-first placement: walk through the selected doors, see transient graphics in Revit, flip / change set /
    /// edit, then confirm (place) or skip each door. Nothing is written to the model until Confirm.
    /// </summary>
    public class PreviewViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly DoorsViewModel _doors;
        private readonly List<string> _queue = new List<string>();
        private readonly Dictionary<string, string> _outcomes = new Dictionary<string, string>();
        private int _index;
        private bool _isActive;
        private bool _loading;
        private SetChoice _selectedSet;
        private bool _autoZoom = true;

        public PreviewViewModel(MainViewModel main, DoorsViewModel doors)
        {
            _main = main;
            _doors = doors;
            PreviousCommand = new RelayCommand(() => Go(_index - 1), () => _isActive && _index > 0 && !_main.IsBusy);
            NextCommand = new RelayCommand(() => Go(_index + 1), () => _isActive && _index < _queue.Count - 1 && !_main.IsBusy);
            FlipCommand = new RelayCommand(Flip, () => Current != null && Current.AccessDirection != AccessDirection.Both && !_main.IsBusy);
            SwapHingeCommand = new RelayCommand(SwapHinge, () => Current != null && !_main.IsBusy);
            EditComponentsCommand = new RelayCommand(EditComponents, () => Current != null);
            SkipCommand = new RelayCommand(Skip, () => _isActive && !_main.IsBusy);
            ConfirmCommand = new RelayCommand(Confirm, () => Current != null && CanPlaceCurrent && !_main.IsBusy);
            ExitCommand = new RelayCommand(Exit, () => _isActive);
            ZoomCommand = new RelayCommand(() => Push(true), () => Current != null);
        }

        private SentinelProject Project => _main.Session.Project;

        public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }
        public bool AutoZoom { get => _autoZoom; set => Set(ref _autoZoom, value); }

        public DoorSetInstance Current => _isActive && _index >= 0 && _index < _queue.Count ? Project.FindInstance(_queue[_index]) : null;

        public RelayCommand PreviousCommand { get; }
        public RelayCommand NextCommand { get; }
        public RelayCommand FlipCommand { get; }
        public RelayCommand SwapHingeCommand { get; }
        public RelayCommand EditComponentsCommand { get; }
        public RelayCommand SkipCommand { get; }
        public RelayCommand ConfirmCommand { get; }
        public RelayCommand ExitCommand { get; }
        public RelayCommand ZoomCommand { get; }

        public string PositionText { get; private set; }
        public string DoorTitle { get; private set; }
        public string RoomsText { get; private set; }
        public string AccessText { get; private set; }
        public string HingeText { get; private set; }
        public string OutcomeText { get; private set; }
        public string PlanIssuesText { get; private set; }
        public bool HasPlanIssues => !string.IsNullOrEmpty(PlanIssuesText);
        public bool CanPlaceCurrent { get; private set; }
        public bool IsAlreadyPlaced { get; private set; }
        public string ConfirmText => IsAlreadyPlaced ? "Confirm update" : "Confirm";
        public ObservableCollection<PreviewPlacementItem> Placements { get; } = new ObservableCollection<PreviewPlacementItem>();
        public ObservableCollection<SetChoice> SetChoices { get; } = new ObservableCollection<SetChoice>();

        public SetChoice SelectedSet
        {
            get => _selectedSet;
            set
            {
                if (!Set(ref _selectedSet, value) || _loading || Current == null || value?.Definition == null) return;
                if (Current.DefinitionId == value.Definition.Id) return;
                DoorSetInstanceOperations.AssignDefinition(Current, value.Definition);
                AfterEdit("change set");
            }
        }

        // ------------------------------------------------------------------ lifecycle

        public void Start(IList<DoorRowViewModel> rows)
        {
            var ids = rows.Where(r => r != null && r.IsAssigned).Select(r => r.Instance.Id).Distinct().ToList();
            if (ids.Count == 0)
            {
                _main.SetStatus("Assign a door set before previewing.", true);
                return;
            }
            _queue.Clear();
            _queue.AddRange(ids);
            _outcomes.Clear();
            IsActive = true;
            Go(0);
        }

        public void Exit()
        {
            IsActive = false;
            _main.Session.PreviewInstanceIds.Clear();
            _main.Host.ClearPreview();
            _queue.Clear();
            _doors.UpdateRows();
            var placed = _outcomes.Values.Count(v => v.StartsWith("Placed") || v.StartsWith("Updated"));
            var skipped = _outcomes.Values.Count(v => v == "Skipped");
            _main.SetStatus("Preview closed. " + placed + " confirmed, " + skipped + " skipped.");
            RefreshAll();
            RelayCommand.Requery();
        }

        private void Go(int index)
        {
            if (index < 0 || index >= _queue.Count) return;
            _index = index;
            Load();
            Push(AutoZoom);
            var row = _doors.FindRow(_queue[_index]);
            if (row != null) _doors.Inspector.Load(row);
        }

        /// <summary>Called by the inspector when the current instance was edited there ("Edit Components").</summary>
        public void OnInstanceEdited(DoorSetInstance inst)
        {
            if (!_isActive || inst == null || Current == null || inst.Id != Current.Id) return;
            Load();
            Push(false);
        }

        private void Load()
        {
            _loading = true;
            try
            {
                var inst = Current;
                _main.Session.PreviewInstanceIds.Clear();
                Placements.Clear();
                SetChoices.Clear();
                if (inst == null)
                {
                    RefreshAll();
                    return;
                }
                _main.Session.PreviewInstanceIds.Add(inst.Id);

                var src = _main.Session.LiveDoor(inst)?.Current ?? inst.Source;
                PositionText = "Door " + (_index + 1) + " of " + _queue.Count;
                DoorTitle = src?.DisplayName;
                RoomsText = "Side A: " + SentinelSession.SideName(src, true) + "     Side B: " + SentinelSession.SideName(src, false);
                AccessText = SentinelSession.AccessText(src, inst.AccessDirection);

                foreach (var d in Project.DoorSetDefinitions.OrderBy(d => d.Code, StringComparer.OrdinalIgnoreCase))
                    SetChoices.Add(new SetChoice { Definition = d });
                _selectedSet = SetChoices.FirstOrDefault(c => c.Definition.Id == inst.DefinitionId);

                var plan = _main.Session.Plan(inst);
                HingeText = plan != null ? DoorSetPlacementCalculator.HingeText(plan.ResolvedHinge) + (plan.HingeAssumed ? " (assumed)" : "") : "";
                if (plan != null)
                {
                    foreach (var p in plan.Placements)
                        Placements.Add(new PreviewPlacementItem
                        {
                            Label = p.Label,
                            Explanation = p.Explanation,
                            IssuesText = string.Join("\n", p.Issues.Select(i => i.Message)),
                            HasErrors = p.HasErrors
                        });
                    PlanIssuesText = string.Join("\n", plan.Issues.Select(i => (i.Severity == IssueSeverity.Error ? "✗ " : "⚠ ") + i.Message));
                }
                else PlanIssuesText = "No placement plan (door geometry unavailable).";

                IsAlreadyPlaced = inst.HasPlacedElements;
                CanPlaceCurrent = plan != null && !plan.HasBlockingErrors && plan.Placements.Any(p => p.CanPlace);
                string outcome;
                OutcomeText = _outcomes.TryGetValue(inst.Id, out outcome) ? outcome : (IsAlreadyPlaced ? "Already placed – Confirm updates it." : "");
            }
            finally
            {
                _loading = false;
            }
            RefreshAll();
            RelayCommand.Requery();
        }

        /// <summary>Sends the current plan to the transient preview graphics (no model change).</summary>
        private void Push(bool zoom)
        {
            var inst = Current;
            if (inst == null) return;
            var plan = _main.Session.Plan(inst);
            var geometry = _main.Session.GeometryFor(inst);
            var scene = new PreviewScene();
            if (geometry != null && plan != null)
            {
                scene.Doors.Add(new PreviewDoor
                {
                    Door = geometry,
                    Direction = inst.AccessDirection,
                    Hinge = plan.ResolvedHinge,
                    Placements = plan.Placements,
                    IsCurrent = true
                });
            }
            _main.Host.ShowPreview(scene, zoom, r => { if (!r.Success) _main.SetStatus(r.Message, true); });
            var row = _doors.FindRow(inst.Id);
            if (row != null) _doors.UpdateRow(row);
        }

        // ------------------------------------------------------------------ actions

        private void AfterEdit(string reason)
        {
            _main.MarkDirty(reason);
            Load();
            Push(false);
        }

        private void Flip()
        {
            if (DoorSetInstanceOperations.Flip(Current)) AfterEdit("flip set");
        }

        private void SwapHinge()
        {
            var plan = _main.Session.Plan(Current);
            DoorSetInstanceOperations.SwapHinge(Current, plan?.ResolvedHinge ?? HingeSide.NegativeWidthAxis);
            AfterEdit("swap hinge");
        }

        private void EditComponents()
        {
            var row = _doors.FindRow(Current.Id);
            if (row != null) _doors.Inspector.Load(row);
            _main.SetStatus("Edit the components in the inspector on the right – the preview updates as you apply changes.");
        }

        private void Skip()
        {
            if (Current != null) _outcomes[Current.Id] = "Skipped";
            Advance();
        }

        private void Advance()
        {
            if (_index < _queue.Count - 1) Go(_index + 1);
            else
            {
                Load();
                _main.SetStatus("Last door in the preview queue. Exit the preview or go back.");
            }
        }

        private void Confirm()
        {
            var inst = Current;
            if (inst == null) return;
            // Confirmed mode for both new and already placed sets: the user reviewed this preview.
            var wasPlaced = inst.HasPlacedElements;
            _doors.RunPlacement(new List<string> { inst.Id }, PlacementMode.Confirmed, false, "Placing " + DoorTitle + "…", result =>
            {
                var d = result.Doors.FirstOrDefault();
                _outcomes[inst.Id] = d == null ? "Failed" :
                    d.Outcome == PlacementOutcome.Failed ? "Failed: " + string.Join(" ", d.Messages) :
                    (wasPlaced ? "Updated" : "Placed") + (d.Outcome == PlacementOutcome.NeedsReview ? " (needs review)" : "");
                if (d != null && (d.Outcome == PlacementOutcome.Placed || d.Outcome == PlacementOutcome.NoChanges)) Advance();
                else
                {
                    Load();
                    Push(false);
                }
            });
        }
    }
}
