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

        /// <summary>
        /// Decided: this source gets no access control (no set, nothing placed). Unlike <see cref="IsIgnored"/>
        /// ("not relevant, skip it") this is a design decision and counts as done.
        /// </summary>
        public bool NoAccessControl { get; set; }

        [JsonIgnore]
        public bool IsNoAccessControl => NoAccessControl && !IsIgnored && string.IsNullOrEmpty(DefinitionId);

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

        /// <summary>
        /// How far (mm) a wall-side component is pushed out from the door's wall face because wall material is in the
        /// way there (e.g. a lining modelled in another link). Measured in Revit before preview/placement, keyed by
        /// slot. Part of the calculated plan, so preview, placement and status agree.
        /// </summary>
        public Dictionary<string, double> WallClearances { get; set; } = new Dictionary<string, double>();

        public DoorSetInstance Clone()
        {
            var c = (DoorSetInstance)MemberwiseClone();
            c.Source = Source != null ? Source.Clone() : new SourceDoorReference();
            c.Overrides = Overrides != null ? Overrides.Clone() : new SetOverrides();
            c.Components = (Components ?? new List<PlacedComponentInstance>()).Select(x => x.Clone()).ToList();
            c.WallClearances = new Dictionary<string, double>(WallClearances ?? new Dictionary<string, double>());
            return c;
        }
    }
}
