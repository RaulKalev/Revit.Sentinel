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

    /// <summary>A linked model in the door source list (several can be ticked).</summary>
    public class LinkOption : ObservableObject
    {
        private bool _isChecked;
        public LinkInfo Link { get; set; }
        public string UniqueId => Link?.UniqueId;
        public string DisplayName => Link?.DisplayName;
        public bool IsLoaded => Link != null && Link.IsLoaded;
        public bool IsChecked { get => _isChecked; set { if (Set(ref _isChecked, value)) Changed?.Invoke(this, EventArgs.Empty); } }
        public event EventHandler Changed;
    }

    /// <summary>Doors workspace: discovery, the door grid, bulk actions, inspector and preview.</summary>
    public class DoorsViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
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

            FindDoorsCommand = new RelayCommand(FindDoors, () => SelectedLinks.Count > 0 && !_main.IsBusy);
            RefreshCommand = new RelayCommand(() => RefreshStatus(null), () => !_main.IsBusy);
            FilterCommand = new RelayCommand(p => ActiveFilter = p as string ?? "All");
            AssignCommand = new RelayCommand(Assign, () => CanEdit && AssignDefinition != null && _selectedRows.Any(r => r.Door != null || r.Instance != null));
            UnassignCommand = new RelayCommand(Unassign, () => CanEdit && _selectedRows.Any(r => r.Instance != null));
            SuggestCommand = new RelayCommand(Suggest, () => CanEdit && Project.AssignmentRules.Any(r => r.Enabled));
            PreviewCommand = new RelayCommand(StartPreview, () => CanEdit && _selectedRows.Any(r => r.IsAssigned));
            PlaceCommand = new RelayCommand(PlaceAutomatically, () => CanEdit && _selectedRows.Any(r => r.IsAssigned));
            UpdateCommand = new RelayCommand(() => UpdatePlacement(_selectedRows, false), () => CanEdit && _selectedRows.Any(r => r.IsAssigned && r.HasPlacedElements));
            IgnoreCommand = new RelayCommand(Ignore, () => CanEdit && _selectedRows.Any(r => r.Instance == null || !r.Instance.IsIgnored));
            UnignoreCommand = new RelayCommand(Unignore, () => CanEdit && _selectedRows.Any(r => r.IsLeftOutDuplicate || (r.Instance != null && r.Instance.IsIgnored)));
            NoAccessControlCommand = new RelayCommand(() => MarkNoAccessControl(_selectedRows.ToList()),
                () => CanEdit && _selectedRows.Any(r => r.Status != SetStatus.NoAccessControl));
            ZoomCommand = new RelayCommand(() => Zoom(SelectedRow), () => SelectedRow != null);
            ZoomAlternativeCommand = new RelayCommand(() => Zoom(SelectedRow, AlternativeZoomView), () => SelectedRow != null);
            SelectComponentsCommand = new RelayCommand(() => SelectComponents(SelectedRow), () => SelectedRow != null && SelectedRow.HasPlacedElements);
            ClearSelectionCommand = new RelayCommand(() => SelectKeys(new List<string>()), () => _selectedRows.Count > 0);
            SelectSourceDoorCommand = new RelayCommand(() => SelectSourceDoor(SelectedRow), () => SelectedRow != null);
            ToggleInspectorCommand = new RelayCommand(() => IsInspectorVisible = !IsInspectorVisible);
        }

        private SentinelSession Session => _main.Session;
        private SentinelProject Project => _main.Session.Project;
        private bool CanEdit => _main.IsEditable && !_main.IsBusy && !Preview.IsActive;

        public MainViewModel Main => _main;
        public DoorInspectorViewModel Inspector { get; }
        public PreviewViewModel Preview { get; }

        public ObservableCollection<LinkOption> Links { get; } = new ObservableCollection<LinkOption>();

        /// <summary>Ticked, loaded links doors are collected from.</summary>
        public List<LinkInfo> SelectedLinks => Links.Where(l => l.IsChecked && l.IsLoaded).Select(l => l.Link).ToList();

        private bool _skipWindowTypes;

        /// <summary>Leave out Doors-category elements typed "Window…" (IFC exports).</summary>
        public bool SkipWindowTypes { get => _skipWindowTypes; set => Set(ref _skipWindowTypes, value); }
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
        public RelayCommand NoAccessControlCommand { get; }
        public RelayCommand ZoomCommand { get; }
        public RelayCommand ZoomAlternativeCommand { get; }
        public RelayCommand SelectComponentsCommand { get; }

        /// <summary>Raised after a rebuild so the view can restore the grid selection (keys).</summary>
        public event EventHandler<List<string>> RestoreSelectionRequested;

        private void OnLinkToggled(object sender, EventArgs e)
        {
            OnPropertiesChanged(nameof(SelectedLinks), nameof(SourceText));
            LoadLevels();
            RelayCommand.Requery();
        }

        private void SetLinks(IEnumerable<LinkInfo> links, ICollection<string> checkedIds)
        {
            foreach (var old in Links) old.Changed -= OnLinkToggled;
            Links.Clear();
            foreach (var l in links)
            {
                var o = new LinkOption { Link = l, IsChecked = l.IsLoaded && checkedIds.Contains(l.UniqueId) };
                o.Changed += OnLinkToggled;
                Links.Add(o);
            }
            OnLinkToggled(this, EventArgs.Empty);
        }

        public Option<DiscoveryScope> SelectedScope
        {
            get => _selectedScope;
            set
            {
                if (Set(ref _selectedScope, value)) OnPropertiesChanged(nameof(IsLevelScope), nameof(SourceText));
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

        /// <summary>Compact description of where doors come from ("ARH_Model.ifc · All linked doors").</summary>
        public string SourceText => LinksText + "  ·  " + (IsLevelScope ? LevelSummary : _selectedScope?.Text ?? "");

        /// <summary>"AR.ifc + SA.ifc", or "3 linked models" when the names would not fit.</summary>
        private string LinksText
        {
            get
            {
                var links = SelectedLinks;
                if (links.Count == 0) return "Choose linked models";
                if (links.Count == 1) return links[0].DisplayName;
                var joined = string.Join(" + ", links.Select(l => l.ShortName));
                return joined.Length <= 48 ? joined : links.Count + " linked models";
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
                if (!Set(ref _selectedRow, value)) return;
                // While previewing, the workspace stays on the preview door; picking a queued door jumps to it.
                if (Preview.IsActive)
                {
                    if (value?.Instance == null) return;
                    Preview.TryGoTo(value.Instance.Id);
                    // The click was only "go to this door": drop the selection right away, so a later refresh (e.g.
                    // after placing) cannot restore it and pull the review back to this row.
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        new Action(() => { if (Preview.IsActive) SelectKeys(new List<string>()); }),
                        System.Windows.Threading.DispatcherPriority.Background);
                    return;
                }
                Inspector.Load(value);
            }
        }

        public IReadOnlyList<DoorRowViewModel> SelectedRows => _selectedRows;
        public int SelectedCount => _selectedRows.Count;

        public RelayCommand ClearSelectionCommand { get; }
        public RelayCommand SelectSourceDoorCommand { get; }
        public RelayCommand ToggleInspectorCommand { get; }

        // ---- filter counts (shown in the segmented filter) ----
        public int CountAll { get; private set; }
        public int CountUnassigned { get; private set; }
        public int CountReady { get; private set; }
        public int CountPlaced { get; private set; }
        public int CountAttention { get; private set; }
        public int CountIgnored { get; private set; }
        public int CountNoAccessControl { get; private set; }

        private bool _inspectorVisible = true;

        /// <summary>Review workspace shown next to the grid (the view persists the choice per user).</summary>
        public bool IsInspectorVisible { get => _inspectorVisible; set => Set(ref _inspectorVisible, value); }

        /// <summary>"3 doors selected" – the scope every action bar command works on.</summary>
        public string SelectionText =>
            _selectedRows.Count == 0 ? "Select doors in the list to assign, review or place them" :
            _selectedRows.Count == 1 ? "1 door selected (" + _selectedRows[0].Mark + ")" :
            _selectedRows.Count + " doors selected";

        public bool HasSelection => _selectedRows.Count > 0;

        private string _primaryAction = "";

        /// <summary>
        /// The next step for the current selection, which the action bar emphasises:
        /// "Assign" (unassigned doors), "Review" (sets not placed yet → preview), "Update" (placed sets needing an update).
        /// </summary>
        public string PrimaryAction { get => _primaryAction; private set { if (Set(ref _primaryAction, value)) OnPropertiesChanged(nameof(IsAssignPrimary), nameof(IsReviewPrimary), nameof(IsUpdatePrimary)); } }
        public bool IsAssignPrimary => _primaryAction == "Assign";
        public bool IsReviewPrimary => _primaryAction == "Review";
        public bool IsUpdatePrimary => _primaryAction == "Update";

        private void UpdatePrimaryAction()
        {
            OnPropertiesChanged(nameof(SelectionText), nameof(HasSelection));
            if (_selectedRows.Count == 0) { PrimaryAction = ""; return; }
            if (_selectedRows.Any(r => r.IsAssigned && !r.HasPlacedElements)) { PrimaryAction = "Review"; return; }
            if (_selectedRows.Any(r => r.IsAssigned && r.HasPlacedElements &&
                                       (r.Status == SetStatus.Modified || r.Status == SetStatus.MissingComponent ||
                                        r.Status == SetStatus.SourceChanged || r.Status == SetStatus.Error)))
            {
                PrimaryAction = "Update";
                return;
            }
            PrimaryAction = _selectedRows.Any(r => r.Status == SetStatus.Unassigned) ? "Assign" : "";
        }

        private static bool IsAttention(DoorRowViewModel r) =>
            r.Status == SetStatus.Modified || r.Status == SetStatus.MissingComponent || r.Status == SetStatus.SourceChanged ||
            r.Status == SetStatus.Orphaned || r.Status == SetStatus.Error ||
            (r.Instance != null && r.Instance.ReviewState == ReviewState.NeedsReview);

        public DoorSetDefinition AssignDefinition { get => _assignDefinition; set => Set(ref _assignDefinition, value); }

        public string Summary { get => _summary; private set => Set(ref _summary, value); }

        public bool HasRows => Rows.Count > 0;

        /// <summary>Doors exist, but the search or filter hides all of them.</summary>
        public bool HasNoMatches => Rows.Count > 0 && RowsView.IsEmpty;

        // ------------------------------------------------------------------ lifecycle

        public void OnProjectLoaded()
        {
            ReloadDefinitions();
            var s = Project.Settings;
            SelectedScope = UiChoices.Find(UiChoices.Scopes, s.DiscoveryScope) ?? UiChoices.Scopes[0];
            SkipWindowTypes = s.DiscoverySkipWindowTypes;
            RebuildRows();
        }

        public void OnActivated()
        {
            OnPropertiesChanged(nameof(ZoomText), nameof(ZoomAlternativeText), nameof(IsAlternativeZoom3D)); // setting may have changed
            ReloadDefinitions();
            UpdateRows();
            RereadIfParametersChanged();
        }

        /// <summary>First load: list links, restore the stored discovery settings and run discovery + refresh.</summary>
        public void StartUp()
        {
            if (_startedUp) return;
            _startedUp = true;
            _main.Host.GetLinks(links =>
            {
                var stored = Project.Settings.GetDiscoveryLinks();
                var storedLoaded = links.Where(l => l.IsLoaded && stored.Contains(l.UniqueId)).Select(l => l.UniqueId).ToList();
                var initial = storedLoaded.Count > 0
                    ? storedLoaded
                    : new[] { (links.FirstOrDefault(l => l.IsLoaded && l.IsIfc) ?? links.FirstOrDefault(l => l.IsLoaded))?.UniqueId }
                        .Where(id => id != null).ToList();
                SetLinks(links, initial);

                if (links.Count == 0)
                {
                    _main.SetStatus("No linked models in this project. Link the architectural IFC/Revit model to discover doors.", true);
                    if (Project.DoorSetInstances.Count > 0) RefreshStatus(null);
                    return;
                }
                if (storedLoaded.Count > 0) FindDoors();
                else if (Project.DoorSetInstances.Count > 0) RefreshStatus(null);
                else _main.SetStatus("Choose the linked models next to the page title, then find doors.");
            });
        }

        public void ReloadDefinitions()
        {
            var current = AssignDefinition?.Id;
            var choices = Project.DoorSetDefinitions.OrderBy(d => d.Code, StringComparer.OrdinalIgnoreCase).ToList();
            choices.Add(NoAccessControlChoice.Definition); // last, so it is never the default choice
            CollectionSync.Sync(Definitions, choices);
            AssignDefinition = Definitions.FirstOrDefault(d => d.Id == current) ?? Definitions.FirstOrDefault();
        }

        private int _levelRequest;

        /// <summary>Levels of all ticked links (union). Ticked levels survive link changes.</summary>
        private void LoadLevels()
        {
            var keep = new HashSet<string>(Levels.Where(l => l.IsChecked).Select(l => l.Name));
            if (keep.Count == 0) keep.UnionWith(Project.Settings.DiscoveryLevelNames ?? new List<string>());
            var ids = SelectedLinks.Select(l => l.UniqueId).ToList();
            var request = ++_levelRequest;
            if (ids.Count == 0)
            {
                Levels.Clear();
                OnPropertiesChanged(nameof(LevelSummary), nameof(SourceText));
                return;
            }
            _main.Host.GetLinkLevels(ids, names =>
            {
                if (request != _levelRequest) return; // a newer link selection is loading
                Levels.Clear();
                foreach (var n in names)
                {
                    var lo = new LevelOption { Name = n, IsChecked = keep.Contains(n) };
                    lo.Changed += (s, e) => OnPropertiesChanged(nameof(LevelSummary), nameof(SourceText));
                    Levels.Add(lo);
                }
                OnPropertiesChanged(nameof(LevelSummary), nameof(SourceText));
            });
        }

        // ------------------------------------------------------------------ discovery / refresh

        private void FindDoors() => FindDoors(null);

        /// <summary>Captured-parameter names added in Settings since the doors were last read.</summary>
        public bool ParametersOutOfDate =>
            Session.DiscoveredDoors.Count > 0 &&
            (Project.Settings.CapturedParameterNames ?? new List<string>()).Any(n => !string.IsNullOrWhiteSpace(n) && !Session.ParametersRead.Contains(n, StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Doors carry the parameter values read when they were found, so a newly captured parameter (e.g. for a rule)
        /// has no value until the doors are read again. Does that automatically with the same links and levels.
        /// </summary>
        public void RereadIfParametersChanged()
        {
            if (!ParametersOutOfDate || _main.IsBusy || Preview.IsActive || SelectedLinks.Count == 0) return;
            FindDoors("Reading the new captured parameters from the doors…");
        }

        /// <param name="rereadText">Busy text when discovery re-runs to read newly captured parameters (null = Find doors).</param>
        private void FindDoors(string rereadText)
        {
            var links = SelectedLinks;
            if (links.Count == 0) return;
            var req = new DiscoveryRequest
            {
                LinkUniqueIds = links.Select(l => l.UniqueId).ToList(),
                SkipWindowTypes = SkipWindowTypes,
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
            var settingsChanged = !s.GetDiscoveryLinks().SequenceEqual(req.LinkUniqueIds) || s.DiscoveryScope != req.Scope ||
                                  !s.DiscoveryLevelNames.SequenceEqual(req.LevelNames) || s.DiscoverySkipWindowTypes != req.SkipWindowTypes;
            s.SetDiscoveryLinks(req.LinkUniqueIds);
            s.DiscoveryScope = req.Scope;
            s.DiscoveryLevelNames = req.LevelNames;
            s.DiscoverySkipWindowTypes = req.SkipWindowTypes;
            if (settingsChanged && _main.IsEditable && !_main.Host.IsNewProject) _main.MarkDirty("discovery settings");

            var readingParameters = (Project.Settings.CapturedParameterNames ?? new List<string>()).ToList();
            _main.BeginBusy(rereadText ?? (links.Count == 1 ? "Finding doors in " + links[0].ShortName + "…" : "Finding doors in " + links.Count + " linked models…"));
            _main.Host.DiscoverDoors(req, result =>
            {
                _main.EndBusy();
                if (!result.Success)
                {
                    _main.SetStatus(result.Error, true);
                    return;
                }
                Session.DiscoveredDoors = result.Doors;
                Session.ParametersRead = readingParameters;
                Session.DiscoveryLinkName = result.LinkName;
                RebuildRows();
                var msg = result.Doors.Count + " door(s) found" +
                          (result.CountsByLink.Count > 1 ? " (" + string.Join(", ", result.CountsByLink) + ")." : " in " + result.LinkName + ".");
                if (rereadText != null) msg = "New captured parameters read. " + msg;
                if (result.Warnings.Count > 0) msg += " " + string.Join(" ", result.Warnings);
                var leftOut = Rows.Count(r => r.IsLeftOutDuplicate);
                if (leftOut > 0) msg += " " + leftOut + " door(s) are also in a higher-priority model and are listed under Ignored.";
                // The refresh that follows keeps the discovery result in front of its own summary.
                if (Project.DoorSetInstances.Count > 0) RefreshStatus(null, msg);
                else _main.SetStatus(msg, false);
            });
        }

        /// <summary>Verifies sources and placed components, then updates all rows.</summary>
        /// <param name="after">Continuation after the rows were updated.</param>
        /// <param name="leadMessage">Optional text shown before the refresh summary (e.g. a placement result).</param>
        public void RefreshStatus(Action after, string leadMessage = null, bool leadIsError = false)
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
                var text = string.Join(" • ", parts) + ".";
                if (!string.IsNullOrEmpty(leadMessage)) text = leadMessage + "   |   " + text;
                _main.SetStatus(text, leadIsError || r.MissingComponents > 0 || missingSources > 0);
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
            // During a review a selected row means "go to this door", so restoring it after a refresh (e.g. after
            // placing) would jump the review back. The review keeps its own position instead.
            var keep = Preview.IsActive ? new List<string>() : _selectedRows.Select(r => r.Key).ToList();
            DuplicatePriority.Apply(Session.DiscoveredDoors, Project.Settings.LinkPriority);
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
            DuplicatePriority.Apply(Session.DiscoveredDoors, Project.Settings.LinkPriority); // priority may have changed in Settings
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
            if (_selectedRows.Count != 1 && !Preview.IsActive) Inspector.Load(null, _selectedRows.Count);
            OnPropertyChanged(nameof(SelectedCount));
            UpdatePrimaryAction();
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
                case "Attention": return IsAttention(r);
                case "Ignored": return r.Status == SetStatus.Ignored;
                case "NoAccessControl": return r.Status == SetStatus.NoAccessControl;
                default: return r.Status != SetStatus.Ignored; // ignored doors (incl. left-out duplicates) have their own filter
            }
        }

        private void UpdateSummary()
        {
            OnPropertyChanged(nameof(HasNoMatches));
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
            CountAll = total - C(SetStatus.Ignored);
            CountUnassigned = C(SetStatus.Unassigned);
            CountReady = C(SetStatus.Ready) + C(SetStatus.Preview);
            CountPlaced = C(SetStatus.Placed);
            CountAttention = Rows.Count(r => IsAttention(r));
            CountIgnored = C(SetStatus.Ignored);
            CountNoAccessControl = C(SetStatus.NoAccessControl);
            OnPropertiesChanged(nameof(CountAll), nameof(CountUnassigned), nameof(CountReady), nameof(CountPlaced), nameof(CountAttention),
                nameof(CountIgnored), nameof(CountNoAccessControl));
            UpdatePrimaryAction();
            Summary = total + " doors • " + C(SetStatus.Unassigned) + " unassigned • " + C(SetStatus.Ready) + " ready • " +
                      C(SetStatus.Placed) + " placed • " + (CountNoAccessControl > 0 ? CountNoAccessControl + " no access control • " : "") + attention +
                      " need attention • showing " + shown;
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
            if (NoAccessControlChoice.Is(def))
            {
                MarkNoAccessControl(_selectedRows.ToList());
                return;
            }
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
                _main.Dialogs.Confirm("Remove door sets",
                    "Remove the set from " + rows.Count + " door(s)?\n\n" + placed.Count + " of them have placed components; " + n +
                    " element(s) will be deleted from the model.", null, "Remove and delete " + n + " element(s)", "Cancel", ok =>
                    {
                        if (!ok) return;
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

        /// <summary>
        /// Marks doors as needing no access control. Doors with placed components ask first: the components are deleted.
        /// </summary>
        /// <param name="done">Called with true once the doors are marked; false when nothing was marked (cancelled, failed).</param>
        /// <param name="ask">False: delete placed components without asking (a decision made door by door in the review;
        /// Revit's Undo brings them back).</param>
        public void MarkNoAccessControl(IList<DoorRowViewModel> rows, Action<bool> done = null, bool ask = true)
        {
            rows = (rows ?? new List<DoorRowViewModel>()).Where(r => r.Status != SetStatus.NoAccessControl).ToList();
            if (rows.Count == 0)
            {
                _main.SetStatus("The selected doors are already marked as no access control.");
                done?.Invoke(false);
                return;
            }
            Action mark = () =>
            {
                foreach (var row in rows)
                {
                    var inst = EnsureInstance(row, null);
                    if (inst == null) continue;
                    DoorSetInstanceOperations.MarkNoAccessControl(inst);
                    row.Update();
                }
                _main.MarkDirty("no access control");
                UpdateRows();
            };

            var placed = rows.Where(r => r.HasPlacedElements).ToList();
            if (placed.Count == 0)
            {
                mark();
                _main.SetStatus(rows.Count + " door(s) marked as no access control.");
                done?.Invoke(true);
                return;
            }
            var n = placed.Sum(r => r.Instance.Components.Count(c => !string.IsNullOrEmpty(c.ElementUniqueId)));
            Action<bool> proceed = ok =>
            {
                if (!ok) { done?.Invoke(false); return; }
                _main.CancelPendingSave();
                _main.BeginBusy("Deleting components…");
                _main.Host.DeletePlacedComponents(placed.Select(r => r.Instance.Id).ToList(), true, r =>
                {
                    _main.EndBusy();
                    if (!r.Success)
                    {
                        _main.SetStatus(r.Message, true);
                        UpdateRows();
                        done?.Invoke(false);
                        return;
                    }
                    mark();
                    _main.SetStatus(rows.Count + " door(s) marked as no access control; " + r.Message);
                    done?.Invoke(true);
                });
            };
            if (!ask)
            {
                proceed(true);
                return;
            }
            _main.Dialogs.Confirm("No access control",
                "Mark " + rows.Count + " door(s) as no access control?\n\n" + placed.Count + " of them have placed components; " + n +
                " element(s) will be deleted from the model.", null, "Mark and delete " + n + " element(s)", "Cancel", proceed);
        }

        private void Ignore()
        {
            foreach (var row in _selectedRows)
            {
                var inst = EnsureInstance(row, null);
                if (inst == null) continue;
                inst.IsIgnored = true;
                inst.NoAccessControl = false;
                inst.Touch();
            }
            _main.MarkDirty("ignore");
            UpdateRows();
            _main.SetStatus("Ignored doors are listed under the Ignored filter.");
        }

        private void Unignore()
        {
            // Left-out duplicates: an (empty) instance of their own keeps them listed next to the preferred door.
            foreach (var row in _selectedRows.Where(r => r.IsLeftOutDuplicate)) EnsureInstance(row, null);
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
                var def = s != null ? Project.FindSetChoice(s.DefinitionId) : null;
                if (def != null) suggestions.Add(Tuple.Create(r, def, s.Explanation));
            }
            if (suggestions.Count == 0)
            {
                _main.Dialogs.Show("Rule suggestions", "No rule matched the " + candidates.Count + " unassigned door(s).");
                return;
            }

            var details = new StringBuilder();
            foreach (var s in suggestions) details.AppendLine(s.Item1.Mark + " → " + s.Item2.DisplayName + "   (" + s.Item3 + ")");
            _main.Dialogs.Confirm("Rule suggestions",
                suggestions.Count + " of " + candidates.Count + " unassigned door(s) match an assignment rule. Assign the suggested sets?",
                details.ToString(), "Assign " + suggestions.Count + " set(s)", "Cancel", ok =>
                {
                    if (!ok) return;
                    foreach (var s in suggestions)
                    {
                        var none = NoAccessControlChoice.Is(s.Item2);
                        var inst = EnsureInstance(s.Item1, none ? null : s.Item2);
                        if (inst == null) continue;
                        if (none) DoorSetInstanceOperations.MarkNoAccessControl(inst);
                        else DoorSetInstanceOperations.AssignDefinition(inst, s.Item2);
                        inst.Notes = string.IsNullOrWhiteSpace(inst.Notes) ? "Assigned by " + s.Item3 : inst.Notes;
                    }
                    _main.MarkDirty("rule suggestions");
                    UpdateRows();
                    _main.SetStatus(suggestions.Count + " set(s) assigned from rules. Review them before placing.");
                });
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
            var ids = fresh.Select(r => r.Instance.Id).ToList();
            _main.Dialogs.Confirm("Place automatically", msg, withErrors.Count > 0 ? details.ToString() : null,
                "Place " + fresh.Count + " set(s)", "Cancel", ok =>
                {
                    if (ok) RunPlacement(ids, PlacementMode.Batch, false, "Placing " + ids.Count + " door set(s)…", null);
                });
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

            var ids = rows.Select(r => r.Instance.Id).ToList();
            var msg = "Update " + rows.Count + " placed set(s) to their current configuration?" +
                      (manualKept > 0 ? "\n\n" + manualKept + " manually modified component(s) are kept unless you choose to overwrite them." : "") +
                      "\nHosted components that move are re-created.";
            var yes = "Update " + rows.Count + " set(s)";
            Action<bool, bool> run = (ok, overwrite) =>
            {
                if (ok) RunPlacement(ids, PlacementMode.Update, overwrite, "Updating " + ids.Count + " door set(s)…", null);
            };
            if (allowOverwriteManual && manualKept > 0)
                _main.Dialogs.ConfirmWithOption("Update placement", msg, details.ToString(),
                    "Also move manually modified components back to their calculated position", false, yes, "Cancel", run);
            else
                _main.Dialogs.Confirm("Update placement", msg, details.ToString(), yes, "Cancel", ok => run(ok, false));
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
                // The placement read these doors fresh; no full refresh afterwards (it re-read every door in the
                // project and froze Revit for seconds after each placement). Refresh still checks everything.
                foreach (var kv in result.Sources ?? new Dictionary<string, SourceCheck>())
                    if (kv.Value != null) Session.SourceChecks[kv.Key] = kv.Value;
                UpdateRows();

                var details = new StringBuilder();
                foreach (var d in result.Doors.Where(d => d.Outcome != PlacementOutcome.Placed || mode != PlacementMode.Batch))
                {
                    details.AppendLine(d.DoorName + ": " + d.Outcome);
                    foreach (var m in d.Messages) details.AppendLine("    " + m);
                }

                var isError = result.FatalError != null || result.Count(PlacementOutcome.Failed) > 0;
                _main.SetStatus(result.Summary, isError);
                // In a review (Confirmed) warnings go to the status line and the review moves on; only errors stop it.
                if (after == null || isError || (mode != PlacementMode.Confirmed && result.Count(PlacementOutcome.NeedsReview) > 0))
                {
                    _main.Dialogs.Show(mode == PlacementMode.Update ? "Update placement" : "Placement result",
                        result.Summary, details.Length > 0 ? details.ToString() : null, isError);
                }
                RelayCommand.Requery();
                after?.Invoke(result);
            });
        }

        // ------------------------------------------------------------------ navigation

        /// <summary>Zooms to the door in the default view (Settings), or in <paramref name="view"/> when given.</summary>
        public void Zoom(DoorRowViewModel row, DoorZoomView? view = null)
        {
            if (row == null) return;
            _main.Host.Navigate(new NavigationRequest
            {
                Kind = NavigationKind.ZoomToDoor,
                View = view,
                Source = row.Source,
                Geometry = row.Door?.Geometry ?? row.Instance?.Source?.LastKnownGeometry,
                ElementUniqueIds = row.Instance?.Components.Select(c => c.ElementUniqueId).Where(u => !string.IsNullOrEmpty(u)).ToList()
                                   ?? new List<string>()
            }, r => { if (!r.Success || !string.IsNullOrEmpty(r.Message)) _main.SetStatus(r.Message, !r.Success); });
        }

        /// <summary>The view "Zoom to door" is not set to open (offered as an extra command).</summary>
        public DoorZoomView AlternativeZoomView =>
            Project.Settings.ZoomView == DoorZoomView.FloorPlan ? DoorZoomView.View3D : DoorZoomView.FloorPlan;

        public string ZoomText => Project.Settings.ZoomView == DoorZoomView.FloorPlan ? "Zoom to door in floor plan" : "Zoom to door in 3D view";
        public string ZoomAlternativeText => AlternativeZoomView == DoorZoomView.View3D ? "Zoom in 3D view" : "Zoom in floor plan";
        public bool IsAlternativeZoom3D => AlternativeZoomView == DoorZoomView.View3D;

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

        /// <summary>Selects rows by door key (the view applies it to the grid).</summary>
        public void SelectKeys(IEnumerable<string> keys) => RestoreSelectionRequested?.Invoke(this, keys.ToList());
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
