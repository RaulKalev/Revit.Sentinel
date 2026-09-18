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
            return c;
        }
    }
}
