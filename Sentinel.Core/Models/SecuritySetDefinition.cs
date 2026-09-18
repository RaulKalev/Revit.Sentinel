using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Sentinel.Core.Models
{
    /// <summary>
    /// One component slot inside a set definition (or an override-added component on an instance):
    /// which library component, a display label and its placement rule.
    /// </summary>
    public class SetComponentRule
    {
        /// <summary>Stable key of the slot within its set. Placed components refer back to it.</summary>
        public string Id { get; set; } = Ids.New();

        public string ComponentDefinitionId { get; set; }

        /// <summary>Optional label (e.g. "Reader"). Falls back to the component definition name.</summary>
        public string Label { get; set; }

        public PlacementRule Rule { get; set; } = new PlacementRule();

        public SetComponentRule Clone()
        {
            var c = (SetComponentRule)MemberwiseClone();
            c.Rule = Rule != null ? Rule.Clone() : new PlacementRule();
            return c;
        }
    }

    /// <summary>
    /// Reusable security set type shared by door sets and (future) room sets.
    /// </summary>
    public abstract class SecuritySetDefinition
    {
        public string Id { get; set; } = Ids.New();

        /// <summary>Short identifier shown in the grid, e.g. "DS-02".</summary>
        public string Code { get; set; }

        public string Name { get; set; }
        public string Description { get; set; }

        [JsonIgnore]
        public abstract SecuritySetKind Kind { get; }

        public List<SetComponentRule> Components { get; set; } = new List<SetComponentRule>();

        /// <summary>Incremented on every edit, so instances can later be compared with the template revision they were placed from.</summary>
        public int Revision { get; set; } = 1;

        public string ModifiedUtc { get; set; } = DateTime.UtcNow.ToString("o");

        [JsonIgnore]
        public string DisplayName =>
            string.IsNullOrWhiteSpace(Code) ? (Name ?? "") :
            string.IsNullOrWhiteSpace(Name) ? Code : Code + " " + Name;

        public void Touch()
        {
            Revision++;
            ModifiedUtc = DateTime.UtcNow.ToString("o");
        }
    }

    /// <summary>Door Set Type, e.g. "DS-02 Secure Technical Door".</summary>
    public class DoorSetDefinition : SecuritySetDefinition
    {
        public override SecuritySetKind Kind => SecuritySetKind.Door;

        /// <summary>Access direction given to new instances of this set.</summary>
        public AccessDirection DefaultAccessDirection { get; set; } = AccessDirection.SideAToSideB;

        public DoorSetDefinition Clone()
        {
            var c = (DoorSetDefinition)MemberwiseClone();
            c.Components = (Components ?? new List<SetComponentRule>()).Select(x => x.Clone()).ToList();
            return c;
        }
    }
}
