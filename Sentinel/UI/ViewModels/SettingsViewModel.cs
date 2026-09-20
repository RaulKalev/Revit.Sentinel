using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Sentinel.Core.Library;
using Sentinel.Core.Models;
using Sentinel.Core.Persistence;
using Sentinel.UI.Mvvm;
using Sentinel.UI.Services;

namespace Sentinel.UI.ViewModels
{
    /// <summary>One linked model in the duplicate-door priority list.</summary>
    public class LinkPriorityItem
    {
        public string Name { get; set; }
        public int Rank { get; set; }
    }

    /// <summary>Project settings, library import/export and data information.</summary>
    public class SettingsViewModel : ObservableObject, IDataErrorInfo
    {
        private readonly MainViewModel _main;
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>();

        public SettingsViewModel(MainViewModel main)
        {
            _main = main;
            ExportCommand = new RelayCommand(Export);
            ImportCommand = new RelayCommand(Import, () => IsEditable);
            AddStarterCommand = new RelayCommand(AddStarterItems, () => IsEditable);
            MoveLinkUpCommand = new RelayCommand(p => MoveLink(p as LinkPriorityItem, -1), p => IsEditable && (p as LinkPriorityItem)?.Rank > 1);
            MoveLinkDownCommand = new RelayCommand(p => MoveLink(p as LinkPriorityItem, +1), p => IsEditable && (p as LinkPriorityItem)?.Rank < LinkPriority.Count);
        }

        private SentinelProject Project => _main.Session.Project;
        private SentinelSettings S => Project.Settings;
        public bool IsEditable => _main.IsEditable;

        public RelayCommand ExportCommand { get; }
        public RelayCommand ImportCommand { get; }
        public RelayCommand AddStarterCommand { get; }
        public RelayCommand MoveLinkUpCommand { get; }
        public RelayCommand MoveLinkDownCommand { get; }

        /// <summary>Linked models by priority for doors modelled in several links (first = listed).</summary>
        public ObservableCollection<LinkPriorityItem> LinkPriority { get; } = new ObservableCollection<LinkPriorityItem>();
        public bool HasLinkPriority => LinkPriority.Count > 1;

        /// <summary>Stored order first, then links of this project that are not in it yet (in the Doors page order).</summary>
        private void LoadLinkPriority()
        {
            var names = new List<string>();
            foreach (var n in (S.LinkPriority ?? new List<string>())
                         .Concat(_main.Doors.Links.Select(l => l.Link?.ShortName))
                         .Concat(_main.Session.DiscoveredDoors.Select(d => LinkInfo.Short(d.Current?.LinkName))))
                if (!string.IsNullOrWhiteSpace(n) && !names.Contains(n, StringComparer.OrdinalIgnoreCase)) names.Add(n);
            LinkPriority.Clear();
            for (var i = 0; i < names.Count; i++) LinkPriority.Add(new LinkPriorityItem { Name = names[i], Rank = i + 1 });
            OnPropertyChanged(nameof(HasLinkPriority));
        }

        private void MoveLink(LinkPriorityItem item, int step)
        {
            var i = item == null ? -1 : LinkPriority.IndexOf(item);
            var j = i + step;
            if (i < 0 || j < 0 || j >= LinkPriority.Count) return;
            var names = LinkPriority.Select(x => x.Name).ToList();
            names.RemoveAt(i);
            names.Insert(j, item.Name);
            S.LinkPriority = names;
            Edited();
            LoadLinkPriority();
            RelayCommand.Requery();
        }

        public List<Option<HingeSide>> HingeSides => UiChoices.HingeSides;

        public bool HingeOnHandSide
        {
            get => S.HingeOnHandOrientationSide;
            set { if (S.HingeOnHandOrientationSide != value) { S.HingeOnHandOrientationSide = value; Edited(); OnPropertyChanged(); } }
        }

        public Option<HingeSide> DefaultHinge
        {
            get => UiChoices.Find(UiChoices.HingeSides, S.DefaultHingeSide);
            set { if (value != null && S.DefaultHingeSide != value.Value) { S.DefaultHingeSide = value.Value; Edited(); OnPropertyChanged(); } }
        }

        public bool PreviewInActiveView
        {
            get => S.PreviewInActiveView;
            set { if (S.PreviewInActiveView != value) { S.PreviewInActiveView = value; Edited(); OnPropertyChanged(); } }
        }

        /// <summary>"Zoom to door" opens a floor plan of the door's level (the 3D view stays available as an extra command).</summary>
        public bool ZoomInFloorPlan
        {
            get => S.ZoomView == DoorZoomView.FloorPlan;
            set { if (value && S.ZoomView != DoorZoomView.FloorPlan) { S.ZoomView = DoorZoomView.FloorPlan; Edited(); OnPropertiesChanged(nameof(ZoomInFloorPlan), nameof(ZoomIn3D)); } }
        }

        /// <summary>Preferred floor plan name keywords, as typed (e.g. "Security, EL"); parsed when a plan is chosen.</summary>
        public string PlanNameKeywords
        {
            get => S.PlanNameKeywords ?? "";
            set
            {
                if ((S.PlanNameKeywords ?? "") == (value ?? "")) return;
                S.PlanNameKeywords = value;
                Edited();
                OnPropertyChanged();
            }
        }

        // ---- zoom amount per view (stepper: 25 % … 400 %, 100 % = standard framing) ----
        public string PlanZoomText => ZoomLevels.Clamp(S.PlanZoomPercent).ToString("0") + " %";
        public string View3DZoomText => ZoomLevels.Clamp(S.View3DZoomPercent).ToString("0") + " %";

        public RelayCommand PlanZoomInCommand => _planIn ?? (_planIn = new RelayCommand(() => StepZoom(true, +1), () => IsEditable && ZoomLevels.Clamp(S.PlanZoomPercent) < ZoomLevels.Max));
        public RelayCommand PlanZoomOutCommand => _planOut ?? (_planOut = new RelayCommand(() => StepZoom(true, -1), () => IsEditable && ZoomLevels.Clamp(S.PlanZoomPercent) > ZoomLevels.Min));
        public RelayCommand View3DZoomInCommand => _3dIn ?? (_3dIn = new RelayCommand(() => StepZoom(false, +1), () => IsEditable && ZoomLevels.Clamp(S.View3DZoomPercent) < ZoomLevels.Max));
        public RelayCommand View3DZoomOutCommand => _3dOut ?? (_3dOut = new RelayCommand(() => StepZoom(false, -1), () => IsEditable && ZoomLevels.Clamp(S.View3DZoomPercent) > ZoomLevels.Min));
        private RelayCommand _planIn, _planOut, _3dIn, _3dOut;

        private void StepZoom(bool plan, int direction)
        {
            if (plan) S.PlanZoomPercent = ZoomLevels.Step(S.PlanZoomPercent, direction);
            else S.View3DZoomPercent = ZoomLevels.Step(S.View3DZoomPercent, direction);
            Edited();
            OnPropertiesChanged(nameof(PlanZoomText), nameof(View3DZoomText));
            RelayCommand.Requery();
        }

        public bool ZoomIn3D
        {
            get => S.ZoomView == DoorZoomView.View3D;
            set { if (value && S.ZoomView != DoorZoomView.View3D) { S.ZoomView = DoorZoomView.View3D; Edited(); OnPropertiesChanged(nameof(ZoomInFloorPlan), nameof(ZoomIn3D)); } }
        }

        public string IdentityParameterName
        {
            get => S.IdentityParameterName;
            set { if (S.IdentityParameterName != value) { S.IdentityParameterName = value; Edited(); OnPropertyChanged(); } }
        }

        public string CapturedParameters
        {
            get => string.Join(", ", S.CapturedParameterNames ?? new List<string>());
            set
            {
                var list = (value ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (list.SequenceEqual(S.CapturedParameterNames ?? new List<string>())) return;
                S.CapturedParameterNames = list;
                Edited();
                OnPropertyChanged();
            }
        }

        // Numeric settings (validated text)
        public string SourceMoveTolerance { get => Text(nameof(SourceMoveTolerance), S.SourceMoveToleranceMm); set => SetNumber(nameof(SourceMoveTolerance), value, v => S.SourceMoveToleranceMm = v); }
        public string SourceRotationTolerance { get => Text(nameof(SourceRotationTolerance), S.SourceRotationToleranceDeg); set => SetNumber(nameof(SourceRotationTolerance), value, v => S.SourceRotationToleranceDeg = v); }
        public string SourceSizeTolerance { get => Text(nameof(SourceSizeTolerance), S.SourceSizeToleranceMm); set => SetNumber(nameof(SourceSizeTolerance), value, v => S.SourceSizeToleranceMm = v); }
        public string ComponentMoveTolerance { get => Text(nameof(ComponentMoveTolerance), S.ComponentMoveToleranceMm); set => SetNumber(nameof(ComponentMoveTolerance), value, v => S.ComponentMoveToleranceMm = v); }
        public string ComponentRotationTolerance { get => Text(nameof(ComponentRotationTolerance), S.ComponentRotationToleranceDeg); set => SetNumber(nameof(ComponentRotationTolerance), value, v => S.ComponentRotationToleranceDeg = v); }
        public string WallSearchDistance { get => Text(nameof(WallSearchDistance), S.WallSearchDistanceMm); set => SetNumber(nameof(WallSearchDistance), value, v => S.WallSearchDistanceMm = v); }
        public string RoomProbeDistance { get => Text(nameof(RoomProbeDistance), S.RoomProbeDistanceMm); set => SetNumber(nameof(RoomProbeDistance), value, v => S.RoomProbeDistanceMm = v); }

        // Info
        public string DocumentText => _main.Host.DocumentTitle;
        public string DataVersionText => "SentinelDataVersion " + SentinelProjectSerializer.CurrentDataVersion + (_main.IsReadOnly ? " (stored data is newer – read-only)" : "");
        public string ProjectIdText => Project.ProjectId;
        public string CountsText => Project.ComponentDefinitions.Count + " components • " + Project.DoorSetDefinitions.Count + " door set types • " +
                                    Project.DoorSetInstances.Count + " door set instances • " + Project.AssignmentRules.Count + " rules" +
                                    (Project.PreservedRawItems.Count > 0 ? " • " + Project.PreservedRawItems.Count + " unreadable item(s) preserved" : "");
        public string LogPathText => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RK Tools", "Sentinel", "sentinel.log");
        public string VersionText => "Sentinel " + typeof(SettingsViewModel).Assembly.GetName().Version;

        public void OnProjectLoaded()
        {
            _texts.Clear();
            LoadLinkPriority();
            RefreshAll();
        }

        public void OnActivated()
        {
            LoadLinkPriority();
            RefreshAll();
        }

        private void Edited() => _main.MarkDirty("settings");

        private string Text(string key, double value)
        {
            string t;
            return _texts.TryGetValue(key, out t) ? t : NumberText.Format(value);
        }

        private void SetNumber(string key, string text, Action<double> apply)
        {
            _texts[key] = text;
            double v;
            if (NumberText.TryParse(text, out v) && v >= 0)
            {
                apply(v);
                Edited();
            }
            OnPropertyChanged(key);
        }

        public string Error => null;

        public string this[string columnName]
        {
            get
            {
                string t;
                if (!_texts.TryGetValue(columnName, out t)) return null;
                double v;
                return NumberText.TryParse(t, out v) && v >= 0 ? null : "Enter a non-negative number.";
            }
        }

        // ------------------------------------------------------------------ library

        /// <summary>Last library path used in this session (prefills the next export/import).</summary>
        private static string _lastLibraryPath;

        private static string DefaultLibraryPath =>
            _lastLibraryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "RK Tools", "Sentinel", "Sentinel.sentinel-library.json");

        private void Export()
        {
            // Path sheet instead of a modal file dialog: plugin windows must never block Revit (AGENTS.md).
            _main.Dialogs.PromptPath("Export library",
                "Save the components, door set types and rules to a JSON file. Door set instances stay in this project.",
                DefaultLibraryPath, "Export", path =>
                {
                    if (path == null) return;
                    try
                    {
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        File.WriteAllText(path, LibraryFile.FromProject(Project, Environment.UserName).ToJson());
                        _lastLibraryPath = path;
                        _main.SetStatus("Library exported to " + path + ".");
                    }
                    catch (Exception ex)
                    {
                        _main.Dialogs.Show("Export library", "Export failed: " + ex.Message, null, true);
                    }
                });
        }

        private void Import()
        {
            _main.Dialogs.PromptPath("Import library", "Path of a Sentinel library file (.json) to merge into this project.",
                DefaultLibraryPath, "Continue", path =>
                {
                    if (path == null) return;
                    LibraryFile lib;
                    try
                    {
                        lib = LibraryFile.Parse(File.ReadAllText(path));
                        _lastLibraryPath = path;
                    }
                    catch (Exception ex)
                    {
                        _main.Dialogs.Show("Import library", "Import failed: " + ex.Message, null, true);
                        return;
                    }
                    _main.Dialogs.Confirm("Import library",
                        "Import " + lib.ComponentDefinitions.Count + " component(s), " + lib.DoorSetDefinitions.Count + " door set type(s) and " +
                        lib.AssignmentRules.Count + " rule(s)?\nItems with the same id are replaced; door set instances are not affected.",
                        null, "Import", "Cancel", ok =>
                        {
                            if (!ok) return;
                            var summary = lib.MergeInto(Project);
                            _main.MarkDirty("import library");
                            _main.DoorSets.OnProjectLoaded();
                            _main.Components.OnProjectLoaded();
                            _main.Rules.OnProjectLoaded();
                            _main.Doors.ReloadDefinitions();
                            _main.Doors.UpdateRows();
                            RefreshAll();
                            _main.SetStatus("Library imported. " + summary);
                        });
                });
        }

        private void AddStarterItems()
        {
            var starter = DefaultLibrary.CreateProject();
            var added = 0;
            foreach (var c in starter.ComponentDefinitions)
            {
                if (Project.ComponentDefinitions.Any(x => x.Category == c.Category && string.Equals(x.Name, c.Name, StringComparison.OrdinalIgnoreCase))) continue;
                Project.ComponentDefinitions.Add(c);
                added++;
            }
            if (added > 0)
            {
                _main.MarkDirty("add starter components");
                _main.Components.OnProjectLoaded();
            }
            _main.SetStatus(added + " starter component(s) added.");
            RefreshAll();
        }
    }
}
