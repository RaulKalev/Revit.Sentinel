using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Sentinel.Core.Models
{
    /// <summary>
    /// A security set applied to one source (door now, room later). References its base definition and stores
    /// only per-instance overrides, the component-element relationships and review state.
    /// </summary>
    public abstract class SecuritySetInstance
    {
        /// <summary>Stable id written to placed elements (SentinelSetId).</summary>
        public string Id { get; set; } = Ids.New();

        [JsonIgnore]
        public abstract SecuritySetKind Kind { get; }

        /// <summary>Base set definition id. Null when the source is only marked as ignored.</summary>
        public string DefinitionId { get; set; }

        /// <summary>Definition revision the components were placed from (for future template comparison).</summary>
        public int PlacedDefinitionRevision { get; set; }

        public SetOverrides Overrides { get; set; } = new SetOverrides();
        public List<PlacedComponentInstance> Components { get; set; } = new List<PlacedComponentInstance>();

        /// <summary>Hash of the effective placement plan at the time of the last placement/update.</summary>
        public string PlacedConfigurationHash { get; set; }

        public bool IsIgnored { get; set; }
        public string IgnoreReason { get; set; }

        public ReviewState ReviewState { get; set; } = ReviewState.NotReviewed;
        public string ReviewedBy { get; set; }
        public string ReviewedUtc { get; set; }

        /// <summary>Last placement/update failure summary (component-level errors live on the components).</summary>
        public string LastError { get; set; }

        public string Notes { get; set; }
        public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("o");
        public string ModifiedUtc { get; set; } = DateTime.UtcNow.ToString("o");

        [JsonIgnore]
        public bool HasPlacedElements =>
            Components != null && Components.Any(c => !string.IsNullOrEmpty(c.ElementUniqueId));

        public void Touch() => ModifiedUtc = DateTime.UtcNow.ToString("o");
    }

    /// <summary>Door Set Instance: one access-control configuration for one linked door.</summary>
    public class DoorSetInstance : SecuritySetInstance
    {
        public override SecuritySetKind Kind => SecuritySetKind.Door;

        public SourceDoorReference Source { get; set; } = new SourceDoorReference();

        public AccessDirection AccessDirection { get; set; } = AccessDirection.SideAToSideB;

        /// <summary>Per-door hinge side correction (null = use the source geometry / settings).</summary>
        public HingeSide? HingeSideOverride { get; set; }

        public DoorSetInstance Clone()
        {
            var c = (DoorSetInstance)MemberwiseClone();
            c.Source = Source != null ? Source.Clone() : new SourceDoorReference();
            c.Overrides = Overrides != null ? Overrides.Clone() : new SetOverrides();
            c.Components = (Components ?? new List<PlacedComponentInstance>()).Select(x => x.Clone()).ToList();
            return c;
        }
    }
}
