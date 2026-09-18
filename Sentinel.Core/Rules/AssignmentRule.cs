using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Models;

namespace Sentinel.Core.Rules
{
    public enum RuleOperator
    {
        Equals,
        NotEquals,
        Contains,
        NotContains,
        StartsWith,
        GreaterThan,
        LessThan,
        IsEmpty,
        IsNotEmpty
    }

    public enum RuleMatchMode
    {
        /// <summary>All conditions must match (AND).</summary>
        All,
        /// <summary>At least one condition must match (OR).</summary>
        Any
    }

    public class RuleCondition
    {
        /// <summary>Door fact name, e.g. "Mark", "SideBRoom", "FireRating" (see <see cref="DoorFacts"/>).</summary>
        public string Field { get; set; }
        public RuleOperator Operator { get; set; } = RuleOperator.Equals;
        public string Value { get; set; }

        public RuleCondition Clone() => (RuleCondition)MemberwiseClone();
    }

    /// <summary>
    /// Rule-based suggestion of a door set. Rules only ever suggest; applying a suggestion is an explicit user
    /// action, and door set instances do not depend on rules (manual assignment is always possible).
    /// </summary>
    public class AssignmentRule
    {
        public string Id { get; set; } = Ids.New();
        public string Name { get; set; }
        public bool Enabled { get; set; } = true;

        /// <summary>Lower number = evaluated first.</summary>
        public int Priority { get; set; } = 100;

        public RuleMatchMode MatchMode { get; set; } = RuleMatchMode.All;
        public List<RuleCondition> Conditions { get; set; } = new List<RuleCondition>();

        /// <summary>Door set definition to suggest.</summary>
        public string DefinitionId { get; set; }

        public AssignmentRule Clone()
        {
            var c = (AssignmentRule)MemberwiseClone();
            c.Conditions = (Conditions ?? new List<RuleCondition>()).Select(x => x.Clone()).ToList();
            return c;
        }
    }
}
