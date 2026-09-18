using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Data;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.Core.Rules;
using Sentinel.UI.Mvvm;
using Sentinel.UI.Services;

namespace Sentinel.UI.ViewModels
{
    public class LevelOption : ObservableObject
    {
        private bool _isChecked;
        public string Name { get; set; }
        public bool IsChecked { get => _isChecked; set { if (Set(ref _isChecked, value)) Changed?.Invoke(this, EventArgs.Empty); } }
        public event EventHandler Changed;
    }

    /// <summary>Doors workspace: discovery, the door grid, bulk actions, inspector and preview.</summary>
    public class DoorsViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private LinkInfo _selectedLink;
        private Option<DiscoveryScope> _selectedScope;
        private string _searchText = "";
        private string _filter = "All";
        private DoorRowViewModel _selectedRow;
        private List<DoorRowViewModel> _selectedRows = new List<DoorRowViewModel>();
        private DoorSetDefinition _assignDefinition;
        private string _summary = "";
        private bool _startedUp;

        public DoorsViewModel(MainViewModel main)
        {
            _main = main;
            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = o => FilterRow(o as DoorRowViewModel);
            _selectedScope = UiChoices.Scopes[0];

            Inspector = new DoorInspectorViewModel(main, this);
            Preview = new PreviewViewModel(main, this);

            FindDoorsCommand = new RelayCommand(FindDoors, () => SelectedLink != null && SelectedLink.IsLoaded && !_main.IsBusy);
            RefreshCommand = new RelayCommand(() => RefreshStatus(null), () => !_main.IsBusy);
            FilterCommand = new RelayCommand(p => ActiveFilter = p as string ?? "All");
            AssignCommand = new RelayCommand(Assign, () => CanEdit && AssignDefinition != null && _selectedRows.Any(r => r.Door != null || r.Instance != null));
            UnassignCommand = new RelayCommand(Unassign, () => CanEdit && _selectedRows.Any(r => r.Instance != null));
            SuggestCommand = new RelayCommand(Suggest, () => CanEdit && Project.AssignmentRules.Any(r => r.Enabled));
            PreviewCommand = new RelayCommand(StartPreview, () => CanEdit && _selectedRows.Any(r => r.IsAssigned));
            PlaceCommand = new RelayCommand(PlaceAutomatically, () => CanEdit && _selectedRows.Any(r => r.IsAssigned));
            UpdateCommand = new RelayCommand(() => UpdatePlacement(_selectedRows, false), () => CanEdit && _selectedRows.Any(r => r.IsAssigned && r.HasPlacedElements));
            IgnoreCommand = new RelayCommand(Ignore, () => CanEdit && _selectedRows.Any(r => r.Instance == null || !r.Instance.IsIgnored));
            UnignoreCommand = new RelayCommand(Unignore, () => CanEdit && _selectedRows.Any(r => r.Instance != null && r.Instance.IsIgnored));
            ZoomCommand = new RelayCommand(() => Zoom(SelectedRow), () => SelectedRow != null);
            SelectComponentsCommand = new RelayCommand(() => SelectComponents(SelectedRow), () => SelectedRow != null && SelectedRow.HasPlacedElements);
        }

        private SentinelSession Session => _main.Session;
        private SentinelProject Project => _main.Session.Project;
        private bool CanEdit => _main.IsEditable && !_main.IsBusy && !Preview.IsActive;

        public MainViewModel Main => _main;
        public DoorInspectorViewModel Inspector { get; }
        public PreviewViewModel Preview { get; }

        public ObservableCollection<LinkInfo> Links { get; } = new ObservableCollection<LinkInfo>();
        public List<Option<DiscoveryScope>> Scopes => UiChoices.Scopes;
        public ObservableCollection<LevelOption> Levels { get; } = new ObservableCollection<LevelOption>();
        public ObservableCollection<DoorRowViewModel> Rows { get; } = new ObservableCollection<DoorRowViewModel>();
        public ICollectionView RowsView { get; }
        public ObservableCollection<DoorSetDefinition> Definitions { get; } = new ObservableCollection<DoorSetDefinition>();

        public RelayCommand FindDoorsCommand { get; }
        public RelayCommand RefreshCommand { get; }
        public RelayCommand FilterCommand { get; }
        public RelayCommand AssignCommand { get; }
        public RelayCommand UnassignCommand { get; }
        public RelayCommand SuggestCommand { get; }
        public RelayCommand PreviewCommand { get; }
        public RelayCommand PlaceCommand { get; }
        public RelayCommand UpdateCommand { get; }
        public RelayCommand IgnoreCommand { get; }
        public RelayCommand UnignoreCommand { get; }
        public RelayCommand ZoomCommand { get; }
        public RelayCommand SelectComponentsCommand { get; }

        /// <summary>Raised after a rebuild so the view can restore the grid selection (keys).</summary>
        public event EventHandler<List<string>> RestoreSelectionRequested;

        public LinkInfo SelectedLink
        {
            get => _selectedLink;
            set
            {
                if (!Set(ref _selectedLink, value)) return;
                LoadLevels();
            }
        }

        public Option<DiscoveryScope> SelectedScope
        {
            get => _selectedScope;
            set
            {
                if (Set(ref _selectedScope, value)) OnPropertyChanged(nameof(IsLevelScope));
            }
        }

        public bool IsLevelScope => _selectedScope != null && _selectedScope.Value == DiscoveryScope.SelectedLevels;

        public string LevelSummary
        {
            get
            {
                var n = Levels.Count(l => l.IsChecked);
                return n == 0 ? "Choose levels…" : n == 1 ? Levels.First(l => l.IsChecked).Name : n + " levels";
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value)) ApplyFilter();
            }
        }

        public string ActiveFilter
        {
            get => _filter;
            set
            {
                if (Set(ref _filter, value)) ApplyFilter();
            }
        }

        public DoorRowViewModel SelectedRow
        {
            get => _selectedRow;
            private set
            {
                if (Set(ref _selectedRow, value)) Inspector.Load(value);
            }
        }

        public IReadOnlyList<DoorRowViewModel> SelectedRows => _selectedRows;
        public int SelectedCount => _selectedRows.Count;

        public DoorSetDefinition AssignDefinition { get => _assignDefinition; set => Set(ref _assignDefinition, value); }

        public string Summary { get => _summary; private set => Set(ref _summary, value); }

        public bool HasRows => Rows.Count > 0;

        // ------------------------------------------------------------------ lifecycle

        public void OnProjectLoaded()
        {
            ReloadDefinitions();
            var s = Project.Settings;
            SelectedScope = UiChoices.Find(UiChoices.Scopes, s.DiscoveryScope) ?? UiChoices.Scopes[0];
            RebuildRows();
        }

        public void OnActivated()
        {
            ReloadDefinitions();
            UpdateRows();
        }

        /// <summary>First load: list links, restore the stored discovery settings and run discovery + refresh.</summary>
        public void StartUp()
        {
            if (_startedUp) return;
            _startedUp = true;
            _main.Host.GetLinks(links =>
            {
                Links.Clear();
                foreach (var l in links) Links.Add(l);
                var stored = Project.Settings.DiscoveryLinkUniqueId;
                var link = links.FirstOrDefault(l => l.UniqueId == stored && l.IsLoaded)
                           ?? links.FirstOrDefault(l => l.IsLoaded && l.IsIfc)
                           ?? links.FirstOrDefault(l => l.IsLoaded);
                SelectedLink = link;

                if (links.Count == 0)
                {
                    _main.SetStatus("No linked models in this project. Link the architectural IFC/Revit model to discover doors.", true);
                    if (Project.DoorSetInstances.Count > 0) RefreshStatus(null);
                    return;
                }
                if (link != null && !string.IsNullOrEmpty(stored) && link.UniqueId == stored) FindDoors();
                else if (Project.DoorSetInstances.Count > 0) RefreshStatus(null);
                else _main.SetStatus("Select the linked model and press Find Doors.");
            });
        }

        public void ReloadDefinitions()
        {
            var current = AssignDefinition?.Id;
            Definitions.Clear();
            foreach (var d in Project.DoorSetDefinitions.OrderBy(d => d.Code, StringComparer.OrdinalIgnoreCase)) Definitions.Add(d);
            AssignDefinition = Definitions.FirstOrDefault(d => d.Id == current) ?? Definitions.FirstOrDefault();
        }

        private void LoadLevels()
        {
            Levels.Clear();
            OnPropertyChanged(nameof(LevelSummary));
            if (_selectedLink == null || !_selectedLink.IsLoaded) return;
            var stored = new HashSet<string>(Project.Settings.DiscoveryLevelNames ?? new List<string>());
            _main.Host.GetLinkLevels(_selectedLink.UniqueId, names =>
            {
                Levels.Clear();
                foreach (var n in names)
                {
                    var lo = new LevelOption { Name = n, IsChecked = stored.Contains(n) };
                    lo.Changed += (s, e) => OnPropertyChanged(nameof(LevelSummary));
                    Levels.Add(lo);
                }
                OnPropertyChanged(nameof(LevelSummary));
            });
        }

        // ------------------------------------------------------------------ discovery / refresh

        private void FindDoors()
        {
            if (SelectedLink == null) return;
            var req = new DiscoveryRequest
            {
                LinkUniqueId = SelectedLink.UniqueId,
                Scope = SelectedScope?.Value ?? DiscoveryScope.AllDoors,
                LevelNames = Levels.Where(l => l.IsChecked).Select(l => l.Name).ToList()
            };
            if (req.Scope == DiscoveryScope.SelectedLevels && req.LevelNames.Count == 0)
            {
                _main.SetStatus("Choose at least one level for the 'Selected levels' scope.", true);
                return;
            }

            // Remember the discovery settings with the project.
            var s = Project.Settings;
            var settingsChanged = s.DiscoveryLinkUniqueId != req.LinkUniqueId || s.DiscoveryScope != req.Scope ||
                                  !s.DiscoveryLevelNames.SequenceEqual(req.LevelNames);
            s.DiscoveryLinkUniqueId = req.LinkUniqueId;
            s.DiscoveryScope = req.Scope;
            s.DiscoveryLevelNames = req.LevelNames;
            if (settingsChanged && _main.IsEditable && !_main.Host.IsNewProject) _main.MarkDirty("discovery settings");

            _main.BeginBusy("Finding doors in " + SelectedLink.Name + "…");
            _main.Host.DiscoverDoors(req, result =>
            {
                _main.EndBusy();
                if (!result.Success)
                {
                    _main.SetStatus(result.Error, true);
                    return;
                }
                Session.DiscoveredDoors = result.Doors;
                Session.DiscoveryLinkName = result.LinkName;
                RebuildRows();
                var msg = result.Doors.Count + " door(s) found in " + result.LinkName + ".";
                if (result.Warnings.Count > 0) msg += " " + string.Join(" ", result.Warnings);
                _main.SetStatus(msg, false);
                if (Project.DoorSetInstances.Count > 0) RefreshStatus(null);
            });
        }

        /// <summary>Verifies sources and placed components, then updates all rows.</summary>
        public void RefreshStatus(Action after)
        {
            _main.BeginBusy("Checking door sets…");
            _main.Host.Refresh(r =>
            {
                _main.EndBusy();
                if (!string.IsNullOrEmpty(r.Error))
                {
                    _main.SetStatus(r.Error, true);
                    after?.Invoke();
                    return;
                }
                Session.SourceChecks = r.Sources;
                Session.HasRefreshed = true;
                RebuildRows();
                var parts = new List<string> { Project.DoorSetInstances.Count(i => !string.IsNullOrEmpty(i.DefinitionId)) + " set(s) checked" };
                if (r.MissingComponents > 0) parts.Add(r.MissingComponents + " missing component(s)");
                if (r.ModifiedComponents > 0) parts.Add(r.ModifiedComponents + " manually modified");
                var changedSources = r.Sources.Values.Count(s => s.State == SourceState.Changed);
                if (changedSources > 0) parts.Add(changedSources + " source door(s) changed");
                var missingSources = r.Sources.Values.Count(s => s.State == SourceState.Missing);
                if (missingSources > 0) parts.Add(missingSources + " orphaned");
                parts.AddRange(r.Messages);
                _main.SetStatus(string.Join(" • ", parts) + ".", r.MissingComponents > 0 || missingSources > 0);
                after?.Invoke();
            });
        }

        /// <summary>Immediate feedback when a tracked element is deleted/moved in Revit (full check on Refresh).</summary>
        public void OnTrackedElementsChanged(TrackedElementsChangedEventArgs e)
        {
            var deleted = new HashSet<long>(e.DeletedElementIds);
            var modified = new HashSet<long>(e.ModifiedElementIds);
            var touched = 0;
            foreach (var inst in Project.DoorSetInstances)
                foreach (var c in inst.Components)
                {
                    if (c.ElementId <= 0) continue;
                    if (deleted.Contains(c.ElementId))
                    {
                        c.State = ComponentState.Missing;
                        touched++;
                    }
                    else if (modified.Contains(c.ElementId)) touched++;
                }
            if (touched == 0) return;
            UpdateRows();
            _main.SetStatus(deleted.Count > 0
                ? "Sentinel components were deleted in Revit – affected sets are marked. Press Refresh for a full check."
                : "Sentinel components were changed in Revit – press Refresh to check for manual modifications.", deleted.Count > 0);
        }

        // ------------------------------------------------------------------ rows

        public void RebuildRows()
        {
            var keep = _selectedRows.Select(r => r.Key).ToList();
            var matches = DoorMatcher.Match(Session.DiscoveredDoors, Project.DoorSetInstances);

            var reidentified = false;
            foreach (var m in matches.Where(x => x.MatchedByIfcGlobalId && x.Door != null && x.Instance != null))
            {
                m.Instance.Source.DoorUniqueId = m.Door.Current.DoorUniqueId;
                m.Instance.Source.DoorElementId = m.Door.Current.DoorElementId;
                m.Instance.Source.LinkInstanceUniqueId = m.Door.Current.LinkInstanceUniqueId;
                reidentified = true;
            }
            if (reidentified) _main.MarkDirty("re-identified doors");

            Rows.Clear();
            foreach (var m in matches.OrderBy(x => (x.Door?.Current ?? x.Instance?.Source)?.LevelName ?? "", NaturalComparer.Instance)
                                     .ThenBy(x => (x.Door?.Current ?? x.Instance?.Source)?.DisplayName ?? "", NaturalComparer.Instance))
            {
                Rows.Add(new DoorRowViewModel(Session, m.Door, m.Instance));
            }
            OnPropertyChanged(nameof(HasRows));
            ApplyFilter();
            RestoreSelectionRequested?.Invoke(this, keep);
        }

        /// <summary>Recomputes status/text of all rows in place (after edits, placement, refresh).</summary>
        public void UpdateRows()
        {
            foreach (var r in Rows)
            {
                if (r.Instance != null && !Project.DoorSetInstances.Contains(r.Instance)) r.Instance = null;
                if (r.Instance == null && r.Door != null) r.Instance = Project.FindInstanceBySourceKey(r.Door.Key);
                r.Update();
            }
            // Instances created by recovery etc. that have no row yet.
            var shown = new HashSet<DoorSetInstance>(Rows.Where(r => r.Instance != null).Select(r => r.Instance));
            foreach (var inst in Project.DoorSetInstances.Where(i => !shown.Contains(i)))
                Rows.Add(new DoorRowViewModel(Session, null, inst));
            // Rows that no longer have a door nor an instance.
            foreach (var dead in Rows.Where(r => r.Door == null && r.Instance == null).ToList()) Rows.Remove(dead);

            ApplyFilter();
            Inspector.Load(SelectedRow);
            RelayCommand.Requery();
        }

        public void UpdateRow(DoorRowViewModel row)
        {
            if (row == null) return;
            row.Update();
            UpdateSummary();
            if (row == SelectedRow) Inspector.Load(row);
            RelayCommand.Requery();
        }

        public void SetSelection(IEnumerable<DoorRowViewModel> rows)
        {
            _selectedRows = (rows ?? Enumerable.Empty<DoorRowViewModel>()).Where(r => r != null).ToList();
            SelectedRow = _selectedRows.Count == 1 ? _selectedRows[0] : null;
            if (_selectedRows.Count != 1) Inspector.Load(null, _selectedRows.Count);
            OnPropertyChanged(nameof(SelectedCount));
            RelayCommand.Requery();
        }

        private void ApplyFilter()
        {
            RowsView.Refresh();
            UpdateSummary();
        }

        private bool FilterRow(DoorRowViewModel r)
        {
            if (r == null) return false;
            var q = (_searchText ?? "").Trim().ToLowerInvariant();
            if (q.Length > 0 && !q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(t => r.SearchBlob.Contains(t))) return false;

            switch (_filter)
            {
                case "Unassigned": return r.Status == SetStatus.Unassigned;
                case "Ready": return r.Status == SetStatus.Ready || r.Status == SetStatus.Preview;
                case "Placed": return r.Status == SetStatus.Placed;
                case "Attention":
                    return r.Status == SetStatus.Modified || r.Status == SetStatus.MissingComponent || r.Status == SetStatus.SourceChanged ||
                           r.Status == SetStatus.Orphaned || r.Status == SetStatus.Error ||
                           (r.Instance != null && r.Instance.ReviewState == ReviewState.NeedsReview);
                case "Ignored": return r.Status == SetStatus.Ignored;
                default: return true;
            }
        }

        private void UpdateSummary()
        {
            var shown = RowsView.Cast<object>().Count();
            var total = Rows.Count;
            if (total == 0)
            {
                Summary = Session.DiscoveredDoors.Count == 0 && Project.DoorSetInstances.Count == 0
                    ? "No doors yet – choose a link and press Find Doors."
                    : "No doors.";
                return;
            }
            int C(SetStatus s) => Rows.Count(r => r.Status == s);
            var attention = C(SetStatus.Modified) + C(SetStatus.MissingComponent) + C(SetStatus.SourceChanged) + C(SetStatus.Orphaned) + C(SetStatus.Error);
            Summary = total + " doors • " + C(SetStatus.Unassigned) + " unassigned • " + C(SetStatus.Ready) + " ready • " +
                      C(SetStatus.Placed) + " placed • " + attention + " need attention • showing " + shown;
        }

        // ------------------------------------------------------------------ edits

        /// <summary>Returns the row's instance, creating it from the discovered door when needed.</summary>
        public DoorSetInstance EnsureInstance(DoorRowViewModel row, DoorSetDefinition def)
        {
            if (row.Instance != null) return row.Instance;
            if (row.Door == null) return null;
            var inst = DoorSetInstanceOperations.Create(row.Door, def);
            Project.DoorSetInstances.Add(inst);
            row.Instance = inst;
            return inst;
        }

        private void Assign()
        {
            var def = AssignDefinition;
            if (def == null) return;
            var placedChanged = 0;
            foreach (var row in _selectedRows)
            {
                var existing = row.Instance;
                var inst = EnsureInstance(row, def);
                if (inst == null) continue;
                if (existing != null)
                {
                    if (existing.HasPlacedElements && existing.DefinitionId != def.Id) placedChanged++;
                    DoorSetInstanceOperations.AssignDefinition(inst, def);
                }
                row.Update();
            }
            _main.MarkDirty("assign " + def.Code);
            UpdateRows();
            _main.SetStatus(def.DisplayName + " assigned to " + _selectedRows.Count + " door(s)." +
                            (placedChanged > 0 ? " " + placedChanged + " already placed set(s) changed – use Update placement to apply." : ""));
        }

        private void Unassign()
        {
            var rows = _selectedRows.Where(r => r.Instance != null).ToList();
            var placed = rows.Where(r => r.HasPlacedElements).ToList();
            if (placed.Count > 0)
            {
                var n = placed.Sum(r => r.Instance.Components.Count(c => !string.IsNullOrEmpty(c.ElementUniqueId)));
                if (!_main.Dialogs.Confirm("Remove door sets",
                        "Remove the set from " + rows.Count + " door(s)?\n\n" + placed.Count + " of them have placed components; " + n +
                        " element(s) will be deleted from the model.", null, "Remove and delete", "Cancel"))
                    return;

                _main.CancelPendingSave();
                _main.BeginBusy("Deleting components…");
                _main.Host.DeletePlacedComponents(placed.Select(r => r.Instance.Id).ToList(), true, r =>
                {
                    _main.EndBusy();
                    if (!r.Success)
                    {
                        _main.SetStatus(r.Message, true);
                        UpdateRows();
                        return;
                    }
                    RemoveInstances(rows);
                    _main.SetStatus("Set removed from " + rows.Count + " door(s); " + r.Message);
                });
                return;
            }
            RemoveInstances(rows);
            _main.SetStatus("Set removed from " + rows.Count + " door(s).");
        }

        private void RemoveInstances(IEnumerable<DoorRowViewModel> rows)
        {
            foreach (var row in rows)
            {
                if (row.Instance == null) continue;
                Project.DoorSetInstances.Remove(row.Instance);
                row.Instance = null;
            }
            _main.MarkDirty("unassign");
            UpdateRows();
        }

        private void Ignore()
        {
            foreach (var row in _selectedRows)
            {
                var inst = EnsureInstance(row, null);
                if (inst == null) continue;
                inst.IsIgnored = true;
                inst.Touch();
            }
            _main.MarkDirty("ignore");
            UpdateRows();
        }

        private void Unignore()
        {
            foreach (var row in _selectedRows.Where(r => r.Instance != null && r.Instance.IsIgnored))
            {
                row.Instance.IsIgnored = false;
                row.Instance.IgnoreReason = null;
                if (string.IsNullOrEmpty(row.Instance.DefinitionId) && !row.Instance.HasPlacedElements)
                {
                    Project.DoorSetInstances.Remove(row.Instance);
                    row.Instance = null;
                }
            }
            _main.MarkDirty("unignore");
            UpdateRows();
        }

        private void Suggest()
        {
            var candidates = (_selectedRows.Count > 0 ? _selectedRows : Rows.Where(r => RowsView.Contains(r)).ToList())
                .Where(r => r.Status == SetStatus.Unassigned && r.Door != null).ToList();
            if (candidates.Count == 0)
            {
                _main.SetStatus("No unassigned doors to suggest sets for (select doors or clear the filter).");
                return;
            }

            var suggestions = new List<Tuple<DoorRowViewModel, DoorSetDefinition, string>>();
            foreach (var r in candidates)
            {
                var s = AssignmentRuleEvaluator.Suggest(Project.AssignmentRules, DoorFacts.FromSource(r.Source));
                var def = s != null ? Project.FindDoorSet(s.DefinitionId) : null;
                if (def != null) suggestions.Add(Tuple.Create(r, def, s.Explanation));
            }
            if (suggestions.Count == 0)
            {
                _main.Dialogs.Show("Rule suggestions", "No rule matched the " + candidates.Count + " unassigned door(s).");
                return;
            }

            var details = new StringBuilder();
            foreach (var s in suggestions) details.AppendLine(s.Item1.Mark + " → " + s.Item2.DisplayName + "   (" + s.Item3 + ")");
            if (!_main.Dialogs.Confirm("Rule suggestions",
                    suggestions.Count + " of " + candidates.Count + " unassigned door(s) match an assignment rule. Assign the suggested sets?",
                    details.ToString(), "Assign " + suggestions.Count, "Cancel"))
                return;

            foreach (var s in suggestions)
            {
                var inst = EnsureInstance(s.Item1, s.Item2);
                DoorSetInstanceOperations.AssignDefinition(inst, s.Item2);
                inst.Notes = string.IsNullOrWhiteSpace(inst.Notes) ? "Assigned by " + s.Item3 : inst.Notes;
            }
            _main.MarkDirty("rule suggestions");
            UpdateRows();
            _main.SetStatus(suggestions.Count + " set(s) assigned from rules. Review them before placing.");
        }

        private void StartPreview()
        {
            var rows = _selectedRows.Where(r => r.IsAssigned).ToList();
            Preview.Start(rows);
        }

        // ------------------------------------------------------------------ placement

        private void PlaceAutomatically()
        {
            var rows = _selectedRows.Where(r => r.IsAssigned).ToList();
            var fresh = rows.Where(r => !r.HasPlacedElements).ToList();
            var alreadyPlaced = rows.Count - fresh.Count;
            if (fresh.Count == 0)
            {
                _main.SetStatus("The selected sets are already placed – use Update placement to apply changes.");
                return;
            }

            var withErrors = fresh.Where(r => r.Status == SetStatus.Error || r.Status == SetStatus.Orphaned).ToList();
            var details = new StringBuilder();
            foreach (var r in withErrors) details.AppendLine(r.Mark + ": " + r.Reason);

            var msg = "Place " + fresh.Count + " door set(s) automatically?" +
                      (alreadyPlaced > 0 ? "\n" + alreadyPlaced + " already placed set(s) are skipped (use Update placement)." : "") +
                      (withErrors.Count > 0 ? "\n\n" + withErrors.Count + " set(s) currently have errors and will fail with the reasons below." : "") +
                      "\n\nEvery placed set stays reviewable and editable afterwards.";
            if (!_main.Dialogs.Confirm("Place automatically", msg, withErrors.Count > 0 ? details.ToString() : null, "Place", "Cancel")) return;

            RunPlacement(fresh.Select(r => r.Instance.Id).ToList(), PlacementMode.Batch, false, "Placing " + fresh.Count + " door set(s)…", null);
        }

        public void UpdatePlacement(IEnumerable<DoorRowViewModel> selection, bool allowOverwriteManual)
        {
            var rows = selection.Where(r => r.IsAssigned && r.HasPlacedElements).ToList();
            if (rows.Count == 0) return;

            var details = new StringBuilder();
            var totalWork = 0;
            var manualKept = 0;
            foreach (var r in rows)
            {
                var plan = Session.Plan(r.Instance);
                if (plan == null) continue;
                var diff = PlacementDiff.Compute(r.Instance, plan, false);
                var work = diff.Count(d => d.Action != DiffAction.Unchanged && d.Action != DiffAction.KeepManual);
                manualKept += diff.Count(d => d.Action == DiffAction.KeepManual);
                totalWork += work;
                if (work > 0 || diff.Any(d => d.Action == DiffAction.KeepManual))
                {
                    details.AppendLine(r.Mark + ": " + PlacementDiff.Summarize(diff));
                    foreach (var d in diff.Where(x => x.Action != DiffAction.Unchanged))
                        details.AppendLine("    " + d.Action + " " + d.Label + " – " + d.Reason);
                }
            }

            if (totalWork == 0 && manualKept == 0)
            {
                _main.SetStatus("The selected sets are up to date.");
                return;
            }

            var overwrite = false;
            bool ok;
            var msg = "Update " + rows.Count + " placed set(s) to their current configuration?" +
                      (manualKept > 0 ? "\n\n" + manualKept + " manually modified component(s) are kept unless you choose to overwrite them." : "") +
                      "\nHosted components that move are re-created.";
            if (allowOverwriteManual && manualKept > 0)
                ok = _main.Dialogs.ConfirmWithOption("Update placement", msg, details.ToString(),
                    "Also move manually modified components back to their calculated position", ref overwrite, "Update", "Cancel");
            else
                ok = _main.Dialogs.Confirm("Update placement", msg, details.ToString(), "Update", "Cancel");
            if (!ok) return;

            RunPlacement(rows.Select(r => r.Instance.Id).ToList(), PlacementMode.Update, overwrite, "Updating " + rows.Count + " door set(s)…", null);
        }

        /// <summary>Runs placement through the host and shows the result summary.</summary>
        public void RunPlacement(List<string> instanceIds, PlacementMode mode, bool overwriteManual, string busyText,
            Action<PlacementBatchResult> after)
        {
            _main.CancelPendingSave();
            _main.BeginBusy(busyText);
            _main.Host.Place(new PlacementRequest { InstanceIds = instanceIds, Mode = mode, OverwriteManual = overwriteManual }, result =>
            {
                _main.EndBusy();
                if (result.FatalError == null) _main.OnSavedByOperation();
                UpdateRows();

                var details = new StringBuilder();
                foreach (var d in result.Doors.Where(d => d.Outcome != PlacementOutcome.Placed || mode != PlacementMode.Batch))
                {
                    details.AppendLine(d.DoorName + ": " + d.Outcome);
                    foreach (var m in d.Messages) details.AppendLine("    " + m);
                }

                var isError = result.FatalError != null || result.Count(PlacementOutcome.Failed) > 0;
                _main.SetStatus(result.Summary, isError);
                if (after == null || isError || result.Count(PlacementOutcome.NeedsReview) > 0)
                {
                    _main.Dialogs.Show(mode == PlacementMode.Update ? "Update placement" : "Placement result",
                        result.Summary, details.Length > 0 ? details.ToString() : null, isError);
                }
                RefreshStatus(() => after?.Invoke(result));
            });
        }

        // ------------------------------------------------------------------ navigation

        public void Zoom(DoorRowViewModel row)
        {
            if (row == null) return;
            _main.Host.Navigate(new NavigationRequest
            {
                Kind = NavigationKind.ZoomToDoor,
                Source = row.Source,
                Geometry = row.Door?.Geometry ?? row.Instance?.Source?.LastKnownGeometry,
                ElementUniqueIds = row.Instance?.Components.Select(c => c.ElementUniqueId).Where(u => !string.IsNullOrEmpty(u)).ToList()
                                   ?? new List<string>()
            }, r => { if (!r.Success) _main.SetStatus(r.Message, true); });
        }

        public void SelectComponents(DoorRowViewModel row)
        {
            if (row?.Instance == null) return;
            _main.Host.Navigate(new NavigationRequest
            {
                Kind = NavigationKind.SelectComponents,
                ElementUniqueIds = row.Instance.Components.Select(c => c.ElementUniqueId).Where(u => !string.IsNullOrEmpty(u)).ToList()
            }, r => _main.SetStatus(r.Message, !r.Success));
        }

        public void SelectSourceDoor(DoorRowViewModel row)
        {
            if (row == null) return;
            _main.Host.Navigate(new NavigationRequest
            {
                Kind = NavigationKind.SelectSourceDoor,
                Source = row.Source,
                Geometry = row.Door?.Geometry ?? row.Instance?.Source?.LastKnownGeometry
            }, r => _main.SetStatus(r.Message, !r.Success));
        }

        public DoorRowViewModel FindRow(string instanceId) => Rows.FirstOrDefault(r => r.Instance != null && r.Instance.Id == instanceId);
    }

    /// <summary>Natural string order ("D2" before "D10").</summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new NaturalComparer();
        private static readonly Regex Chunks = new Regex(@"(\d+|\D+)", RegexOptions.Compiled);

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            var a = Chunks.Matches(x);
            var b = Chunks.Matches(y);
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                var sa = a[i].Value;
                var sb = b[i].Value;
                long na, nb;
                int c;
                if (long.TryParse(sa, out na) && long.TryParse(sb, out nb)) c = na.CompareTo(nb);
                else c = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
            }
            return a.Count.CompareTo(b.Count);
        }
    }
}
