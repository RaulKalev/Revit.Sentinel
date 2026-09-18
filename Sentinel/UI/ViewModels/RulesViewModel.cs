using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Rules;
using Sentinel.UI.Mvvm;

namespace Sentinel.UI.ViewModels
{
    public class ConditionRowViewModel : ObservableObject
    {
        private readonly RulesViewModel _owner;

        public ConditionRowViewModel(RulesViewModel owner, RuleCondition c)
        {
            _owner = owner;
            Model = c;
            RemoveCommand = new RelayCommand(() => _owner.RemoveCondition(this), () => _owner.IsEditable);
        }

        public RuleCondition Model { get; }
        public RelayCommand RemoveCommand { get; }
        public IEnumerable<string> FieldChoices => _owner.FieldChoices;
        public List<Option<RuleOperator>> Operators => UiChoices.Operators;

        public string Field
        {
            get => Model.Field;
            set { if (Model.Field != value) { Model.Field = value; OnPropertyChanged(); _owner.OnEdited(); } }
        }

        public Option<RuleOperator> Operator
        {
            get => UiChoices.Find(UiChoices.Operators, Model.Operator);
            set
            {
                if (value == null || Model.Operator == value.Value) return;
                Model.Operator = value.Value;
                OnPropertiesChanged(nameof(Operator), nameof(NeedsValue));
                _owner.OnEdited();
            }
        }

        public bool NeedsValue => Model.Operator != RuleOperator.IsEmpty && Model.Operator != RuleOperator.IsNotEmpty;

        public string Value
        {
            get => Model.Value;
            set { if (Model.Value != value) { Model.Value = value; OnPropertyChanged(); _owner.OnEdited(); } }
        }
    }

    /// <summary>
    /// Assignment rules page. Rules only suggest sets (applied from the Doors page after confirmation); manual
    /// assignment always works regardless of rules.
    /// </summary>
    public class RulesViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private AssignmentRule _selected;
        private bool _loading;
        private string _priorityText;
        private string _testResult;

        public RulesViewModel(MainViewModel main)
        {
            _main = main;
            NewCommand = new RelayCommand(New, () => IsEditable && Project.DoorSetDefinitions.Count > 0);
            DeleteCommand = new RelayCommand(Delete, () => IsEditable && _selected != null);
            AddConditionCommand = new RelayCommand(AddCondition, () => IsEditable && _selected != null);
            TestCommand = new RelayCommand(Test, () => _selected != null && _main.Doors.SelectedRow != null);
        }

        private SentinelProject Project => _main.Session.Project;
        public bool IsEditable => _main.IsEditable;

        public ObservableCollection<AssignmentRule> Rules { get; } = new ObservableCollection<AssignmentRule>();
        public ObservableCollection<ConditionRowViewModel> Conditions { get; } = new ObservableCollection<ConditionRowViewModel>();
        public List<Option<RuleMatchMode>> MatchModes => UiChoices.MatchModes;
        public IEnumerable<DoorSetDefinition> Definitions => Project.DoorSetDefinitions.OrderBy(d => d.Code, StringComparer.OrdinalIgnoreCase).ToList();

        public IEnumerable<string> FieldChoices =>
            DoorFacts.BuiltInFields.Concat(Project.Settings.CapturedParameterNames ?? new List<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public RelayCommand NewCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand AddConditionCommand { get; }
        public RelayCommand TestCommand { get; }

        public AssignmentRule Selected
        {
            get => _selected;
            set { if (Set(ref _selected, value)) LoadEditor(); }
        }

        public bool HasSelection => _selected != null;

        public string RuleName
        {
            get => _selected?.Name;
            set { if (_selected != null && _selected.Name != value) { _selected.Name = value; OnEdited(); OnPropertyChanged(); RefreshList(); } }
        }

        public bool Enabled
        {
            get => _selected != null && _selected.Enabled;
            set { if (_selected != null && _selected.Enabled != value) { _selected.Enabled = value; OnEdited(); OnPropertyChanged(); RefreshList(); } }
        }

        public string Priority
        {
            get => _priorityText;
            set
            {
                if (!Set(ref _priorityText, value) || _selected == null) return;
                double v;
                if (NumberText.TryParse(value, out v)) { _selected.Priority = (int)Math.Round(v); OnEdited(); RefreshList(); }
            }
        }

        public Option<RuleMatchMode> MatchMode
        {
            get => _selected == null ? null : UiChoices.Find(UiChoices.MatchModes, _selected.MatchMode);
            set { if (_selected != null && value != null && _selected.MatchMode != value.Value) { _selected.MatchMode = value.Value; OnEdited(); OnPropertyChanged(); } }
        }

        public DoorSetDefinition TargetSet
        {
            get => _selected == null ? null : Project.FindDoorSet(_selected.DefinitionId);
            set { if (_selected != null && value != null && _selected.DefinitionId != value.Id) { _selected.DefinitionId = value.Id; OnEdited(); OnPropertyChanged(); RefreshList(); } }
        }

        public string TestResult { get => _testResult; private set => Set(ref _testResult, value); }

        public string TestTarget => _main.Doors.SelectedRow != null ? "Selected door: " + _main.Doors.SelectedRow.Mark : "Select one door on the Doors page to test rules.";

        // ------------------------------------------------------------------

        public void OnProjectLoaded()
        {
            _selected = null;
            RefreshList();
            Selected = Rules.FirstOrDefault();
        }

        public void OnActivated()
        {
            OnPropertiesChanged(nameof(Definitions), nameof(FieldChoices), nameof(TestTarget));
            TestResult = null;
        }

        public static string Describe(AssignmentRule r, SentinelProject p)
        {
            var target = p.FindDoorSet(r.DefinitionId);
            return r.Priority + " • " + (string.IsNullOrWhiteSpace(r.Name) ? "(unnamed)" : r.Name) + " → " +
                   (target?.Code ?? "(no set)") + (r.Enabled ? "" : " • disabled");
        }

        private void RefreshList()
        {
            CollectionSync.Sync(Rules, Project.AssignmentRules.OrderBy(r => r.Priority).ThenBy(r => r.Name).ToList());
            System.Windows.Data.CollectionViewSource.GetDefaultView(Rules).Refresh(); // redraw renamed items
        }

        private void LoadEditor()
        {
            _loading = true;
            Conditions.Clear();
            if (_selected != null)
            {
                _priorityText = _selected.Priority.ToString();
                foreach (var c in _selected.Conditions) Conditions.Add(new ConditionRowViewModel(this, c));
            }
            _loading = false;
            TestResult = null;
            RefreshAll();
            RelayCommand.Requery();
        }

        internal void OnEdited()
        {
            if (_loading || _selected == null) return;
            _main.MarkDirty("edit rule");
        }

        private void New()
        {
            var r = new AssignmentRule
            {
                Name = "New rule",
                Priority = (Project.AssignmentRules.Count == 0 ? 10 : Project.AssignmentRules.Max(x => x.Priority) + 10),
                DefinitionId = Project.DoorSetDefinitions.First().Id,
                Conditions = { new RuleCondition { Field = DoorFacts.SideBRoom, Operator = RuleOperator.Contains, Value = "" } }
            };
            Project.AssignmentRules.Add(r);
            _main.MarkDirty("new rule");
            RefreshList();
            Selected = r;
        }

        private void Delete()
        {
            if (!_main.Dialogs.Confirm("Delete rule", "Delete rule \"" + _selected.Name + "\"?", null, "Delete", "Cancel")) return;
            Project.AssignmentRules.Remove(_selected);
            _main.MarkDirty("delete rule");
            _selected = null;
            RefreshList();
            Selected = Rules.FirstOrDefault();
        }

        private void AddCondition()
        {
            var c = new RuleCondition { Field = DoorFacts.Mark, Operator = RuleOperator.StartsWith, Value = "" };
            _selected.Conditions.Add(c);
            Conditions.Add(new ConditionRowViewModel(this, c));
            OnEdited();
        }

        internal void RemoveCondition(ConditionRowViewModel row)
        {
            _selected.Conditions.Remove(row.Model);
            Conditions.Remove(row);
            OnEdited();
        }

        private void Test()
        {
            var row = _main.Doors.SelectedRow;
            if (row == null) return;
            var facts = DoorFacts.FromSource(row.Source);
            string explanation;
            var match = AssignmentRuleEvaluator.Matches(_selected, facts, out explanation);
            var winner = AssignmentRuleEvaluator.Suggest(Project.AssignmentRules, facts);
            TestResult = row.Mark + ": this rule " + (match ? "MATCHES (" + explanation + ")" : "does not match") + ".\n" +
                         "Overall suggestion: " + (winner == null ? "none" : Project.FindDoorSet(winner.DefinitionId)?.DisplayName + " – " + winner.Explanation);
        }
    }
}
