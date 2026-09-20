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

    /// <summary>One component in the "Carried by" list of a built-in component, ticked when it can carry it.</summary>
    public class CarrierChoiceViewModel : ObservableObject
    {
        private readonly ComponentsViewModel _owner;
        private bool _isChecked;

        public CarrierChoiceViewModel(ComponentsViewModel owner, ComponentDefinition component, bool isChecked)
        {
            _owner = owner;
            Component = component;
            _isChecked = isChecked;
        }

        public ComponentDefinition Component { get; }
        public string Name => Component.Name;

        public bool IsChecked
        {
            get => _isChecked;
            set { if (Set(ref _isChecked, value)) _owner.SetCarrier(Component, value); }
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

        // ---- how the component exists in Revit: its own family, or built into another component's family ----

        public bool IsOwnFamily
        {
            get => _selected != null && !_selected.IsBuiltIn;
            set { if (value) SetModelling(ComponentModelling.OwnFamily); }
        }

        public bool IsBuiltIn
        {
            get => _selected != null && _selected.IsBuiltIn;
            set { if (value) SetModelling(ComponentModelling.BuiltIntoOtherComponent); }
        }

        private void SetModelling(ComponentModelling m)
        {
            if (_loading || _selected == null || _selected.Modelling == m) return;
            _selected.Modelling = m;
            if (m == ComponentModelling.BuiltIntoOtherComponent)
            {
                // Sensible starting point: the first door contact carries it; lock parameters as used in the RK families.
                if (_selected.Carriers.Count == 0)
                    _selected.CarrierComponentId = Project.ComponentDefinitions
                        .Where(c => c != _selected && !c.IsBuiltIn && c.Category == ComponentCategory.DoorContact)
                        .OrderBy(c => c.Name).FirstOrDefault()?.Id;
                if (_selected.Category == ComponentCategory.ElectricLock)
                {
                    if (string.IsNullOrWhiteSpace(_selected.CarrierParameterLeft)) _selected.CarrierParameterLeft = "Lukk vasakul";
                    if (string.IsNullOrWhiteSpace(_selected.CarrierParameterRight)) _selected.CarrierParameterRight = "Lukk paremal";
                }
            }
            OnEdited("component modelling");
            OnBuiltInChanged();
        }

        /// <summary>
        /// Components that can carry this one (not itself, not other built-in components), each ticked when it does.
        /// Several can be ticked (e.g. two magnet contact types); they share the left/right parameters.
        /// </summary>
        public List<CarrierChoiceViewModel> CarrierChoices => _selected == null
            ? new List<CarrierChoiceViewModel>()
            : Project.ComponentDefinitions.Where(c => c != _selected && !c.IsBuiltIn).OrderBy(c => c.Name)
                .Select(c => new CarrierChoiceViewModel(this, c, _selected.IsCarriedBy(c.Id))).ToList();

        /// <summary>The ticked carriers, in the order they were ticked.</summary>
        public List<ComponentDefinition> Carriers => _selected == null
            ? new List<ComponentDefinition>()
            : _selected.Carriers.Select(Project.FindComponent).Where(c => c != null).ToList();

        internal void SetCarrier(ComponentDefinition carrier, bool carries)
        {
            if (_loading || _selected == null || carrier == null || _selected.IsCarriedBy(carrier.Id) == carries) return;
            var ids = _selected.Carriers;
            if (carries) ids.Add(carrier.Id);
            else ids.Remove(carrier.Id);
            _selected.CarrierComponentIds = ids;
            OnEdited("carrier component");
            OnBuiltInChanged();
        }

        public string CarrierParameterLeft
        {
            get => _selected?.CarrierParameterLeft;
            set
            {
                if (_selected == null || _selected.CarrierParameterLeft == value) return;
                _selected.CarrierParameterLeft = value?.Trim();
                OnEdited("carrier parameter");
                OnBuiltInChanged();
            }
        }

        public string CarrierParameterRight
        {
            get => _selected?.CarrierParameterRight;
            set
            {
                if (_selected == null || _selected.CarrierParameterRight == value) return;
                _selected.CarrierParameterRight = value?.Trim();
                OnEdited("carrier parameter");
                OnBuiltInChanged();
            }
        }

        public bool SwapCarrierSides
        {
            get => _selected != null && _selected.SwapCarrierSides;
            set
            {
                if (_selected == null || _selected.SwapCarrierSides == value) return;
                _selected.SwapCarrierSides = value;
                OnEdited("carrier sides");
                OnBuiltInChanged();
            }
        }

        public bool UseOwnFamilyAsBackup
        {
            get => _selected != null && _selected.UseOwnFamilyAsBackup;
            set
            {
                if (_selected == null || _selected.UseOwnFamilyAsBackup == value) return;
                _selected.UseOwnFamilyAsBackup = value;
                OnEdited("backup family");
                OnBuiltInChanged();
            }
        }

        public bool BuiltInIsOk => _selected != null && _selected.IsBuiltInConfigured;

        /// <summary>Plain-language description of what Sentinel will do with the built-in setup.</summary>
        public string BuiltInSummary
        {
            get
            {
                if (_selected == null || !_selected.IsBuiltIn) return "";
                var carriers = Carriers;
                if (carriers.Count == 0) return "Choose the component whose family contains the " + _selected.Name + ".";
                var names = string.Join(" or ", carriers.Select(c => c.Name));
                var families = carriers.Count == 1 ? "the " + names + " family" : "the " + names + " families (all of them use the same parameters)";
                if (!_selected.IsBuiltInConfigured) return "Enter both Yes/No parameters of " + families + ".";
                var backup = !_selected.UseOwnFamilyAsBackup ? "Door sets without a " + names + " report an error." :
                    _selected.IsFamilyConfigured ? "Door sets without a " + names + " place the own family below instead." :
                    "Door sets without a " + names + " report an error until an own family is mapped below.";
                return "Sentinel switches “" + _selected.CarrierParameterRight + "” on when the " + _selected.Name +
                       " is to the right of the " + names + " (seen from the front of its family), or “" +
                       _selected.CarrierParameterLeft + "” when it is to the left. " + backup;
            }
        }

        /// <summary>Heading of the family section: the family is only a backup for built-in components.</summary>
        public string FamilySectionTitle => IsBuiltIn ? "Own family (backup)" : "Revit family type";

        private void OnBuiltInChanged()
        {
            OnPropertiesChanged(nameof(IsOwnFamily), nameof(IsBuiltIn), nameof(Carriers), nameof(CarrierChoices), nameof(CarrierParameterLeft),
                nameof(CarrierParameterRight), nameof(SwapCarrierSides), nameof(UseOwnFamilyAsBackup), nameof(BuiltInIsOk),
                nameof(BuiltInSummary), nameof(FamilySectionTitle), nameof(MappingText), nameof(MappingIsOk));
            RefreshList();
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
                OnPropertiesChanged(nameof(SelectedFamilyType), nameof(MappingText), nameof(MappingIsOk), nameof(BuiltInSummary));
                RefreshList();
            }
        }

        public bool MappingIsOk => _selected != null && ((_selected.IsFamilyConfigured && (!_familiesLoaded || SelectedFamilyType != null)) ||
                                                        (_selected.IsBuiltIn && !_selected.IsFamilyConfigured));

        public string MappingText
        {
            get
            {
                if (_selected == null) return "";
                if (!_selected.IsFamilyConfigured)
                    return _selected.IsBuiltIn
                        ? "No backup family – only used when a door set has no carrier component."
                        : "Not configured – placements of this component fail with \"" + _selected.Name + " family not configured\".";
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
            _selected = null;
            RefreshList();
            Selected = Items.FirstOrDefault(c => c.Id == id) ?? Items.FirstOrDefault();
        }

        public void OnActivated()
        {
            if (!_familiesLoaded) LoadFamilies();
            LoadEditor();
        }

        private void RefreshList()
        {
            CollectionSync.Sync(Items, Project.ComponentDefinitions.OrderBy(c => c.Name).ToList());
            CollectionViewSource.GetDefaultView(Items).Refresh(); // redraw renamed items
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
            RelayCommand.Requery();
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
            var target = _selected;
            _main.Dialogs.Confirm("Delete component", "Delete " + target.Name + " from the library?", null, "Delete", "Cancel", ok =>
            {
                if (!ok || !Project.ComponentDefinitions.Contains(target)) return;
                Project.ComponentDefinitions.Remove(target);
                _main.MarkDirty("delete component");
                _selected = null;
                RefreshList();
                Selected = Items.FirstOrDefault();
            });
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
