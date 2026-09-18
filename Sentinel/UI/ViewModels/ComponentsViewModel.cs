using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using Sentinel.Core.Models;
using Sentinel.UI.Mvvm;
using Sentinel.UI.Services;

namespace Sentinel.UI.ViewModels
{
    public class ParameterRowViewModel : ObservableObject
    {
        private readonly ComponentsViewModel _owner;

        public ParameterRowViewModel(ComponentsViewModel owner, ParameterAssignment p)
        {
            _owner = owner;
            Model = p;
            RemoveCommand = new RelayCommand(() => _owner.RemoveParameter(this), () => _owner.IsEditable);
        }

        public ParameterAssignment Model { get; }
        public RelayCommand RemoveCommand { get; }

        public string Name
        {
            get => Model.Name;
            set { if (Model.Name != value) { Model.Name = value; OnPropertyChanged(); _owner.OnEdited("edit parameter"); } }
        }

        public string Value
        {
            get => Model.Value;
            set { if (Model.Value != value) { Model.Value = value; OnPropertyChanged(); _owner.OnEdited("edit parameter"); } }
        }
    }

    /// <summary>Component library page: device definitions, family/type mapping and default placement.</summary>
    public class ComponentsViewModel : ObservableObject, IDataErrorInfo
    {
        private readonly MainViewModel _main;
        private ComponentDefinition _selected;
        private bool _loading;
        private string _familyFilter = "";
        private bool _showUnsupported;
        private bool _familiesLoaded;
        private string _heightText, _rotationText, _previewText;

        public ComponentsViewModel(MainViewModel main)
        {
            _main = main;
            FamilyTypesView = CollectionViewSource.GetDefaultView(FamilyTypes);
            FamilyTypesView.Filter = o => FilterFamily(o as FamilyTypeInfo);
            DefaultRule = new PlacementRuleEditor { HeightMayBeBlank = true };
            DefaultRule.Edited += (s, e) =>
            {
                if (_loading || _selected == null) return;
                var r = DefaultRule.ToRule();
                if (r == null) return;
                _selected.DefaultPlacement = r;
                OnEdited("edit default rule");
            };

            NewCommand = new RelayCommand(New, () => IsEditable);
            DuplicateCommand = new RelayCommand(Duplicate, () => IsEditable && _selected != null);
            DeleteCommand = new RelayCommand(Delete, () => IsEditable && _selected != null);
            LoadFamiliesCommand = new RelayCommand(LoadFamilies);
            ClearMappingCommand = new RelayCommand(ClearMapping, () => IsEditable && _selected != null && _selected.IsFamilyConfigured);
            AddParameterCommand = new RelayCommand(AddParameter, () => IsEditable && _selected != null);
        }

        private SentinelProject Project => _main.Session.Project;
        public bool IsEditable => _main.IsEditable;

        public ObservableCollection<ComponentDefinition> Items { get; } = new ObservableCollection<ComponentDefinition>();
        public ObservableCollection<FamilyTypeInfo> FamilyTypes { get; } = new ObservableCollection<FamilyTypeInfo>();
        public ICollectionView FamilyTypesView { get; }
        public ObservableCollection<ParameterRowViewModel> Parameters { get; } = new ObservableCollection<ParameterRowViewModel>();
        public PlacementRuleEditor DefaultRule { get; }
        public List<Option<ComponentCategory>> Categories => UiChoices.Categories;
        public List<Option<HostBehavior>> HostBehaviors => UiChoices.HostBehaviors;

        public RelayCommand NewCommand { get; }
        public RelayCommand DuplicateCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand LoadFamiliesCommand { get; }
        public RelayCommand ClearMappingCommand { get; }
        public RelayCommand AddParameterCommand { get; }

        public ComponentDefinition Selected
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;
                LoadEditor();
            }
        }

        public bool HasSelection => _selected != null;

        public string Name
        {
            get => _selected?.Name;
            set { if (_selected != null && _selected.Name != value) { _selected.Name = value; OnEdited("rename component"); OnPropertyChanged(); RefreshList(); } }
        }

        public string Description
        {
            get => _selected?.Description;
            set { if (_selected != null && _selected.Description != value) { _selected.Description = value; OnEdited("edit component"); OnPropertyChanged(); } }
        }

        public Option<ComponentCategory> Category
        {
            get => _selected == null ? null : UiChoices.Find(UiChoices.Categories, _selected.Category);
            set { if (_selected != null && value != null && _selected.Category != value.Value) { _selected.Category = value.Value; OnEdited("edit component"); OnPropertyChanged(); } }
        }

        public Option<HostBehavior> HostBehavior
        {
            get => _selected == null ? null : UiChoices.Find(UiChoices.HostBehaviors, _selected.HostBehavior);
            set { if (_selected != null && value != null && _selected.HostBehavior != value.Value) { _selected.HostBehavior = value.Value; OnEdited("edit component"); OnPropertyChanged(); } }
        }

        public string DefaultHeight
        {
            get => _heightText;
            set
            {
                if (!Set(ref _heightText, value) || _selected == null) return;
                double v;
                if (NumberText.TryParse(value, out v)) { _selected.DefaultMountingHeightMm = v; OnEdited("edit component"); }
            }
        }

        public string RotationOffset
        {
            get => _rotationText;
            set
            {
                if (!Set(ref _rotationText, value) || _selected == null) return;
                double v;
                if (NumberText.TryParse(value, out v)) { _selected.FamilyRotationOffsetDeg = v; OnEdited("edit component"); }
            }
        }

        public string PreviewSize
        {
            get => _previewText;
            set
            {
                if (!Set(ref _previewText, value) || _selected == null) return;
                double v;
                if (NumberText.TryParse(value, out v) && v > 0) { _selected.PreviewSizeMm = v; OnEdited("edit component"); }
            }
        }

        // ---- family mapping ----

        public string FamilyFilter
        {
            get => _familyFilter;
            set { if (Set(ref _familyFilter, value)) FamilyTypesView.Refresh(); }
        }

        public bool ShowUnsupported
        {
            get => _showUnsupported;
            set { if (Set(ref _showUnsupported, value)) FamilyTypesView.Refresh(); }
        }

        public FamilyTypeInfo SelectedFamilyType
        {
            get => _selected == null ? null : FamilyTypes.FirstOrDefault(f => f.FamilyName == _selected.FamilyName && f.TypeName == _selected.TypeName);
            set
            {
                if (_loading || _selected == null || value == null) return;
                if (_selected.FamilyName == value.FamilyName && _selected.TypeName == value.TypeName) return;
                _selected.FamilyName = value.FamilyName;
                _selected.TypeName = value.TypeName;
                OnEdited("map family");
                OnPropertiesChanged(nameof(SelectedFamilyType), nameof(MappingText), nameof(MappingIsOk));
                RefreshList();
            }
        }

        public bool MappingIsOk => _selected != null && _selected.IsFamilyConfigured && (!_familiesLoaded || SelectedFamilyType != null);

        public string MappingText
        {
            get
            {
                if (_selected == null) return "";
                if (!_selected.IsFamilyConfigured) return "Not configured – placements of this component fail with \"" + _selected.Name + " family not configured\".";
                if (!_familiesLoaded) return _selected.FamilyDisplay;
                var f = SelectedFamilyType;
                if (f == null) return _selected.FamilyDisplay + " – not loaded in this project (load the family before placing).";
                return f.DisplayName + " • " + f.CategoryName + " • " + f.PlacementType + (f.IsSupported ? "" : " (unsupported placement type)");
            }
        }

        public string UsageText => _selected == null ? "" : "Used in " + Project.CountUsagesOfComponent(_selected.Id) + " set rule(s)";

        // ------------------------------------------------------------------

        public void OnProjectLoaded()
        {
            var id = _selected?.Id;
            RefreshList();
            _selected = null;
            Selected = Items.FirstOrDefault(c => c.Id == id) ?? Items.FirstOrDefault();
        }

        public void OnActivated()
        {
            if (!_familiesLoaded) LoadFamilies();
            LoadEditor();
        }

        private void RefreshList()
        {
            var sel = _selected;
            Items.Clear();
            foreach (var c in Project.ComponentDefinitions.OrderBy(c => c.Name)) Items.Add(c);
            _selected = sel;
            OnPropertyChanged(nameof(Selected));
        }

        private void LoadFamilies()
        {
            _main.Host.GetFamilyTypes(list =>
            {
                FamilyTypes.Clear();
                foreach (var f in list) FamilyTypes.Add(f);
                _familiesLoaded = true;
                FamilyTypesView.Refresh();
                OnPropertiesChanged(nameof(SelectedFamilyType), nameof(MappingText), nameof(MappingIsOk));
            });
        }

        private bool FilterFamily(FamilyTypeInfo f)
        {
            if (f == null) return false;
            if (!_showUnsupported && !f.IsSupported) return false;
            var q = (_familyFilter ?? "").Trim();
            if (q.Length == 0) return true;
            var blob = (f.FamilyName + " " + f.TypeName + " " + f.CategoryName).ToLowerInvariant();
            return q.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(blob.Contains);
        }

        private void LoadEditor()
        {
            _loading = true;
            Parameters.Clear();
            if (_selected != null)
            {
                _heightText = NumberText.Format(_selected.DefaultMountingHeightMm);
                _rotationText = NumberText.Format(_selected.FamilyRotationOffsetDeg);
                _previewText = NumberText.Format(_selected.PreviewSizeMm);
                DefaultRule.Load(_selected.DefaultPlacement);
                foreach (var p in _selected.Parameters ?? new List<ParameterAssignment>()) Parameters.Add(new ParameterRowViewModel(this, p));
            }
            _loading = false;
            RefreshAll();
        }

        internal void OnEdited(string reason)
        {
            if (_loading || _selected == null) return;
            _main.MarkDirty(reason);
            OnPropertyChanged(nameof(UsageText));
        }

        private void ClearMapping()
        {
            _selected.FamilyName = null;
            _selected.TypeName = null;
            OnEdited("clear family mapping");
            OnPropertiesChanged(nameof(SelectedFamilyType), nameof(MappingText), nameof(MappingIsOk));
            RefreshList();
        }

        private void AddParameter()
        {
            var p = new ParameterAssignment { Name = "Comments", Value = "" };
            _selected.Parameters.Add(p);
            Parameters.Add(new ParameterRowViewModel(this, p));
            OnEdited("add parameter");
        }

        internal void RemoveParameter(ParameterRowViewModel row)
        {
            _selected.Parameters.Remove(row.Model);
            Parameters.Remove(row);
            OnEdited("remove parameter");
        }

        private void New()
        {
            var c = new ComponentDefinition { Name = "New component", Category = ComponentCategory.Custom };
            Project.ComponentDefinitions.Add(c);
            _main.MarkDirty("new component");
            RefreshList();
            Selected = c;
        }

        private void Duplicate()
        {
            var c = _selected.Clone();
            c.Id = Ids.New();
            c.Name = _selected.Name + " (copy)";
            Project.ComponentDefinitions.Add(c);
            _main.MarkDirty("duplicate component");
            RefreshList();
            Selected = c;
        }

        private void Delete()
        {
            var uses = Project.CountUsagesOfComponent(_selected.Id);
            if (uses > 0)
            {
                _main.Dialogs.Show("Delete component", _selected.Name + " is used in " + uses +
                    " door set rule(s) or door overrides. Remove it from those first.", null, true);
                return;
            }
            if (!_main.Dialogs.Confirm("Delete component", "Delete " + _selected.Name + " from the library?", null, "Delete", "Cancel")) return;
            Project.ComponentDefinitions.Remove(_selected);
            _main.MarkDirty("delete component");
            _selected = null;
            RefreshList();
            Selected = Items.FirstOrDefault();
        }

        public string Error => null;

        public string this[string columnName]
        {
            get
            {
                double v;
                switch (columnName)
                {
                    case nameof(DefaultHeight): return NumberText.TryParse(DefaultHeight, out v) ? null : "Enter a number (mm).";
                    case nameof(RotationOffset): return NumberText.TryParse(RotationOffset, out v) ? null : "Enter a number (degrees).";
                    case nameof(PreviewSize): return NumberText.TryParse(PreviewSize, out v) && v > 0 ? null : "Enter a positive number (mm).";
                    default: return null;
                }
            }
        }
    }
}
