using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Sentinel.Core.Models
{
    /// <summary>A parameter value written to the placed family instance (by parameter name).</summary>
    public class ParameterAssignment
    {
        public string Name { get; set; }
        public string Value { get; set; }

        public ParameterAssignment Clone() => (ParameterAssignment)MemberwiseClone();
    }

    /// <summary>
    /// Library entry describing a kind of security device and how it maps to a Revit family type.
    /// Family and type names are user configuration — placement code never hard-codes them.
    /// </summary>
    public class ComponentDefinition
    {
        public string Id { get; set; } = Ids.New();
        public string Name { get; set; }
        public ComponentCategory Category { get; set; } = ComponentCategory.Custom;
        public string Description { get; set; }

        /// <summary>Revit family name of the mapped family type. Empty = not configured.</summary>
        public string FamilyName { get; set; }

        /// <summary>Revit type name of the mapped family type. Empty = not configured.</summary>
        public string TypeName { get; set; }

        public double DefaultMountingHeightMm { get; set; } = 1000;
        public HostBehavior HostBehavior { get; set; } = HostBehavior.Auto;

        /// <summary>Rule used when this component is added to a set without an explicit rule.</summary>
        public PlacementRule DefaultPlacement { get; set; } = new PlacementRule();

        /// <summary>
        /// Family calibration: extra plan rotation (degrees) so that the family's front ends up pointing in the
        /// calculated facing direction. Sentinel assumes the device front is the family's local -Y axis (the side
        /// Revit's "Front" view looks at): such families need 0; families whose front is +Y need 180.
        /// </summary>
        public double FamilyRotationOffsetDeg { get; set; }

        /// <summary>Preview marker size (mm) used by the transient preview graphics.</summary>
        public double PreviewSizeMm { get; set; } = 120;

        public List<ParameterAssignment> Parameters { get; set; } = new List<ParameterAssignment>();

        // ---- built into another component's family ----
        public ComponentModelling Modelling { get; set; } = ComponentModelling.OwnFamily;

        /// <summary>
        /// Components whose family contains this one (e.g. the magnet contacts that can carry the lock). All of them use
        /// the same left/right parameters below; a door set uses whichever of them it has.
        /// </summary>
        public List<string> CarrierComponentIds { get; set; } = new List<string>();

        /// <summary>
        /// Single carrier as stored before several were allowed: read from older files into
        /// <see cref="CarrierComponentIds"/>, never written. Setting it replaces the list with that one carrier.
        /// </summary>
        public string CarrierComponentId
        {
            get => Carriers.FirstOrDefault();
            set => CarrierComponentIds = string.IsNullOrWhiteSpace(value) ? new List<string>() : new List<string> { value };
        }

        public bool ShouldSerializeCarrierComponentId() => false;

        /// <summary>The carrier ids without blanks or repeats.</summary>
        [JsonIgnore]
        public List<string> Carriers => (CarrierComponentIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();

        public bool IsCarriedBy(string componentId) => !string.IsNullOrEmpty(componentId) && Carriers.Contains(componentId);

        /// <summary>Yes/No instance parameter of the carrier switched on when this component is on its left (seen from the front).</summary>
        public string CarrierParameterLeft { get; set; }

        /// <summary>Yes/No instance parameter switched on when this component is on the carrier's right.</summary>
        public string CarrierParameterRight { get; set; }

        /// <summary>The carrier family defines left and right the other way round.</summary>
        public bool SwapCarrierSides { get; set; }

        /// <summary>When the door set has no carrier, place this component as its own family (if one is mapped).</summary>
        public bool UseOwnFamilyAsBackup { get; set; } = true;

        [JsonIgnore]
        public bool IsBuiltIn => Modelling == ComponentModelling.BuiltIntoOtherComponent;

        [JsonIgnore]
        public bool IsBuiltInConfigured => IsBuiltIn && Carriers.Count > 0 &&
                                           !string.IsNullOrWhiteSpace(CarrierParameterLeft) && !string.IsNullOrWhiteSpace(CarrierParameterRight);

        /// <summary>Has everything it needs to be placed: its own family, or a complete built-in setup.</summary>
        [JsonIgnore]
        public bool IsModelled => IsBuiltIn ? IsBuiltInConfigured : IsFamilyConfigured;

        /// <summary>Short description for lists: the family, or the carrier parameters.</summary>
        [JsonIgnore]
        public string ModellingDisplay => !IsBuiltIn ? FamilyDisplay :
            IsBuiltInConfigured ? "Built in: “" + CarrierParameterLeft + "” / “" + CarrierParameterRight + "”" : "Built in (not set up)";

        [JsonIgnore]
        public bool IsFamilyConfigured =>
            !string.IsNullOrWhiteSpace(FamilyName) && !string.IsNullOrWhiteSpace(TypeName);

        [JsonIgnore]
        public string FamilyDisplay => IsFamilyConfigured ? FamilyName + " : " + TypeName : "(not configured)";

        public ComponentDefinition Clone()
        {
            var c = (ComponentDefinition)MemberwiseClone();
            c.DefaultPlacement = DefaultPlacement != null ? DefaultPlacement.Clone() : new PlacementRule();
            c.Parameters = (Parameters ?? new List<ParameterAssignment>()).Select(p => p.Clone()).ToList();
            c.CarrierComponentIds = (CarrierComponentIds ?? new List<string>()).ToList();
            return c;
        }
    }
}
