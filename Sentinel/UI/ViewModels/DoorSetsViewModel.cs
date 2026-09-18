using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sentinel.Core.Models;
using Sentinel.UI.Mvvm;

namespace Sentinel.UI.ViewModels
{
    /// <summary>One component slot of a door set type (label, component and placement rule).</summary>
    public class SetComponentRowViewModel : ObservableObject
    {
        private readonly DoorSetsViewModel _owner;
        private bool _loading;

        public SetComponentRowViewModel(DoorSetsViewModel owner, SetComponentRule slot)
        {
            _owner = owner;
            Slot = slot;
            Editor = new PlacementRuleEditor { HeightMayBeBlank = true };
            _loading = true;
            Editor.Load(slot.Rule);
            _loading = false;
            Editor.Edited += (s, e) =>
            {
                if (_loading) return;
                var rule = Editor.ToRule();
                if (rule == null) return; // invalid input is shown on the field, never written
                Slot.Rule = rule;
                _owner.OnEdited("edit component rule");
            };
            RemoveCommand = new RelayCommand(() => _owner.RemoveRow(this), () => _owner.IsEditable);
            UpCommand = new RelayCommand(() => _owner.MoveRow(this, -1), () => _owner.IsEditable);
            DownCommand = new RelayCommand(() => _owner.MoveRow(this, 1), () => _owner.IsEditable);
        }

        public SetComponentRule Slot { get; }
        public PlacementRuleEditor Editor { get; }
        public RelayCommand RemoveCommand { get; }
        public RelayCommand UpCommand { get; }
        public RelayCommand DownCommand { get; }
        public IEnumerable<ComponentDefinition> ComponentChoices => _owner.ComponentChoices;

        public string Label
        {
            get => Slot.Label;
            set
            {
                if (Slot.Label == value) return;
                Slot.Label = value;
                OnPropertyChanged();
                _owner.OnEdited("rename component");
            }
        }

        public ComponentDefinition Component
        {
            get => _owner.Project.FindComponent(Slot.ComponentDefinitionId);
            set
            {
                if (value == null || Slot.ComponentDefinitionId == value.Id) return;
                Slot.ComponentDefinitionId = value.Id;
                OnPropertiesChanged(nameof(Component), nameof(FamilyText));
                _owner.OnEdited("change component");
            }
        }

        public string FamilyText => Component?.FamilyDisplay ?? "(missing component)";
    }

    /// <summary>Door Set Types page.</summary>
    public class DoorSetsViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private DoorSetDefinition _selected;
        private ComponentDefinition _selectedAdd;
        private bool _loading;

        public DoorSetsViewModel(MainViewModel main)
        {
            _main = main;
            NewCommand = new RelayCommand(New, () => IsEditable);
            DuplicateCommand = new RelayCommand(Duplicate, () => IsEditable && Selected != null);
            DeleteCommand = new RelayCommand(Delete, () => IsEditable && Selected != null);
            AddRowCommand = new RelayCommand(AddRow, () => IsEditable && Selected != null && SelectedAdd != null);
        }

        internal SentinelProject Project => _main.Session.Project;
        public bool IsEditable => _main.IsEditable;

        public ObservableCollection<DoorSetDefinition> Definitions { get; } = new ObservableCollection<DoorSetDefinition>();
        public ObservableCollection<SetComponentRowViewModel> Rows { get; } = new ObservableCollection<SetComponentRowViewModel>();
        public List<Option<AccessDirection>> Directions => UiChoices.Directions;
        public IEnumerable<ComponentDefinition> ComponentChoices => Project.ComponentDefinitions.OrderBy(c => c.Name).ToList();

        public RelayCommand NewCommand { get; }
        public RelayCommand DuplicateCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand AddRowCommand { get; }

        public DoorSetDefinition Selected
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;
                LoadEditor();
            }
        }

        public bool HasSelection => _selected != null;

        public ComponentDefinition SelectedAdd { get => _selectedAdd; set => Set(ref _selectedAdd, value); }

        public string Code
        {
            get => _selected?.Code;
            set { if (_selected != null && _selected.Code != value) { _selected.Code = value; OnEdited("rename set"); OnPropertyChanged(); RefreshList(); } }
        }

        public string Name
        {
            get => _selected?.Name;
            set { if (_selected != null && _selected.Name != value) { _selected.Name = value; OnEdited("rename set"); OnPropertyChanged(); RefreshList(); } }
        }

        public string Description
        {
            get => _selected?.Description;
            set { if (_selected != null && _selected.Description != value) { _selected.Description = value; OnEdited("edit set"); OnPropertyChanged(); } }
        }

        public Option<AccessDirection> DefaultDirection
        {
            get => _selected == null ? null : UiChoices.Find(UiChoices.Directions, _selected.DefaultAccessDirection);
            set { if (_selected != null && value != null && _selected.DefaultAccessDirection != value.Value) { _selected.DefaultAccessDirection = value.Value; OnEdited("edit set"); OnPropertyChanged(); } }
        }

        public string UsageText
        {
            get
            {
                if (_selected == null) return "";
                var uses = Project.DoorSetInstances.Where(i => i.DefinitionId == _selected.Id).ToList();
                var placed = uses.Count(i => i.HasPlacedElements);
                return "Used by " + uses.Count + " door(s), " + placed + " placed • revision " + _selected.Revision +
                       (placed > 0 ? " • edits mark placed sets as Modified (apply with Update placement)" : "");
            }
        }

        // ------------------------------------------------------------------

        public void OnProjectLoaded()
        {
            var id = _selected?.Id;
            Definitions.Clear();
            foreach (var d in Project.DoorSetDefinitions.OrderBy(d => d.Code, StringComparer.OrdinalIgnoreCase)) Definitions.Add(d);
            _selected = null;
            Selected = Definitions.FirstOrDefault(d => d.Id == id) ?? Definitions.FirstOrDefault();
            OnPropertyChanged(nameof(ComponentChoices));
        }

        public void OnActivated()
        {
            OnPropertyChanged(nameof(ComponentChoices));
            if (SelectedAdd == null) SelectedAdd = ComponentChoices.FirstOrDefault();
            LoadEditor();
        }

        private void RefreshList()
        {
            var sel = _selected;
            Definitions.Clear();
            foreach (var d in Project.DoorSetDefinitions.OrderBy(d => d.Code, StringComparer.OrdinalIgnoreCase)) Definitions.Add(d);
            _selected = sel;
            OnPropertyChanged(nameof(Selected));
        }

        private void LoadEditor()
        {
            _loading = true;
            Rows.Clear();
            if (_selected != null)
                foreach (var slot in _selected.Components) Rows.Add(new SetComponentRowViewModel(this, slot));
            _loading = false;
            if (SelectedAdd == null) SelectedAdd = ComponentChoices.FirstOrDefault();
            RefreshAll();
        }

        internal void OnEdited(string reason)
        {
            if (_loading || _selected == null) return;
            _selected.Touch();
            _main.MarkDirty(reason);
            OnPropertyChanged(nameof(UsageText));
        }

        internal void RemoveRow(SetComponentRowViewModel row)
        {
            _selected.Components.Remove(row.Slot);
            Rows.Remove(row);
            OnEdited("remove component");
        }

        internal void MoveRow(SetComponentRowViewModel row, int delta)
        {
            var list = _selected.Components;
            var i = list.IndexOf(row.Slot);
            var j = i + delta;
            if (i < 0 || j < 0 || j >= list.Count) return;
            list.RemoveAt(i);
            list.Insert(j, row.Slot);
            Rows.Move(i, j);
            OnEdited("reorder components");
        }

        private void AddRow()
        {
            var slot = new SetComponentRule
            {
                ComponentDefinitionId = SelectedAdd.Id,
                Label = SelectedAdd.Name,
                Rule = (SelectedAdd.DefaultPlacement ?? new PlacementRule()).Clone()
            };
            _selected.Components.Add(slot);
            Rows.Add(new SetComponentRowViewModel(this, slot));
            OnEdited("add component");
        }

        private void New()
        {
            var d = new DoorSetDefinition { Code = Project.NextDoorSetCode(), Name = "New door set" };
            Project.DoorSetDefinitions.Add(d);
            _main.MarkDirty("new set");
            RefreshList();
            Selected = d;
        }

        private void Duplicate()
        {
            var copy = _selected.Clone();
            copy.Id = Ids.New();
            copy.Code = Project.NextDoorSetCode();
            copy.Name = (_selected.Name ?? "") + " (copy)";
            copy.Revision = 1;
            foreach (var c in copy.Components) c.Id = Ids.New();
            Project.DoorSetDefinitions.Add(copy);
            _main.MarkDirty("duplicate set");
            RefreshList();
            Selected = copy;
        }

        private void Delete()
        {
            var uses = Project.CountInstancesUsing(_selected.Id);
            if (uses > 0)
            {
                _main.Dialogs.Show("Delete door set type",
                    _selected.DisplayName + " is assigned to " + uses + " door(s). Assign another set to those doors first.", null, true);
                return;
            }
            if (!_main.Dialogs.Confirm("Delete door set type", "Delete " + _selected.DisplayName + "?", null, "Delete", "Cancel")) return;
            Project.DoorSetDefinitions.Remove(_selected);
            Project.AssignmentRules.Where(r => r.DefinitionId == _selected.Id).ToList().ForEach(r => r.Enabled = false);
            _main.MarkDirty("delete set");
            _selected = null;
            RefreshList();
            Selected = Definitions.FirstOrDefault();
        }
    }
}
