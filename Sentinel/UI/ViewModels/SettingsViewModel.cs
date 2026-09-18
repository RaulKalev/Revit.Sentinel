using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Sentinel.Core.Library;
using Sentinel.Core.Models;
using Sentinel.Core.Persistence;
using Sentinel.UI.Mvvm;

namespace Sentinel.UI.ViewModels
{
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
        }

        private SentinelProject Project => _main.Session.Project;
        private SentinelSettings S => Project.Settings;
        public bool IsEditable => _main.IsEditable;

        public RelayCommand ExportCommand { get; }
        public RelayCommand ImportCommand { get; }
        public RelayCommand AddStarterCommand { get; }

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
            RefreshAll();
        }

        public void OnActivated() => RefreshAll();

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

        private void Export()
        {
            var path = _main.Dialogs.PickSaveFile("Export Sentinel library", "Sentinel library (*.sentinel-library.json)|*.sentinel-library.json|JSON (*.json)|*.json",
                "Sentinel.sentinel-library.json");
            if (path == null) return;
            try
            {
                File.WriteAllText(path, LibraryFile.FromProject(Project, Environment.UserName).ToJson());
                _main.SetStatus("Library exported to " + path + ".");
            }
            catch (Exception ex)
            {
                _main.Dialogs.Show("Export library", "Export failed: " + ex.Message, null, true);
            }
        }

        private void Import()
        {
            var path = _main.Dialogs.PickOpenFile("Import Sentinel library", "Sentinel library (*.json)|*.json");
            if (path == null) return;
            try
            {
                var lib = LibraryFile.Parse(File.ReadAllText(path));
                if (!_main.Dialogs.Confirm("Import library",
                        "Import " + lib.ComponentDefinitions.Count + " component(s), " + lib.DoorSetDefinitions.Count + " door set type(s) and " +
                        lib.AssignmentRules.Count + " rule(s)?\nItems with the same id are replaced; door set instances are not affected.",
                        null, "Import", "Cancel")) return;
                var summary = lib.MergeInto(Project);
                _main.MarkDirty("import library");
                _main.DoorSets.OnProjectLoaded();
                _main.Components.OnProjectLoaded();
                _main.Rules.OnProjectLoaded();
                _main.Doors.ReloadDefinitions();
                _main.Doors.UpdateRows();
                RefreshAll();
                _main.SetStatus("Library imported. " + summary);
            }
            catch (Exception ex)
            {
                _main.Dialogs.Show("Import library", "Import failed: " + ex.Message, null, true);
            }
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
