using Sentinel.Core.Geometry;

namespace Sentinel.Core.Models
{
    /// <summary>
    /// One component of a set instance and its relationship to a Revit element. The record stays stored even
    /// when the element is deleted, so the set remembers that the component should exist.
    /// </summary>
    public class PlacedComponentInstance
    {
        /// <summary>Stable id written to the Revit element (SentinelComponentId).</summary>
        public string Id { get; set; } = Ids.New();

        /// <summary>
        /// Slot key from the placement plan: the SetComponentRule id, with "#B" appended for the mirrored copy
        /// created when the access direction is Both.
        /// </summary>
        public string SlotKey { get; set; }

        public string ComponentDefinitionId { get; set; }
        public string Label { get; set; }

        /// <summary>UniqueId of the placed element in the host document (null until placed).</summary>
        public string ElementUniqueId { get; set; }
        public long ElementId { get; set; }

        public ComponentState State { get; set; } = ComponentState.Planned;

        /// <summary>Family/type actually used when the element was created (detects later re-mapping).</summary>
        public string PlacedFamilyName { get; set; }
        public string PlacedTypeName { get; set; }

        /// <summary>How the element was hosted ("Unhosted", "LinkedWallFace", "WorkPlane").</summary>
        public string PlacedHosting { get; set; }

        // ---- calculated vs. actual ----
        /// <summary>Position/rotation calculated by the placement engine at placement time.</summary>
        public Vec3? CalculatedPosition { get; set; }
        public double CalculatedRotationDeg { get; set; }

        /// <summary>Position/rotation read back from the element right after placement (hosting may project the point).</summary>
        public Vec3? PlacedPosition { get; set; }
        public double PlacedRotationDeg { get; set; }

        /// <summary>Latest actual position seen during refresh.</summary>
        public Vec3? ActualPosition { get; set; }
        public double ActualRotationDeg { get; set; }

        /// <summary>User accepted a manual move; the accepted position becomes the new reference for drift detection.</summary>
        public bool ManualPositionAccepted { get; set; }
        public Vec3? ManualPositionAcceptedAt { get; set; }
        public double ManualRotationAcceptedDeg { get; set; }

        public string PlacedUtc { get; set; }
        public string PlacedBy { get; set; }

        /// <summary>Human readable trace of why the component is where it is.</summary>
        public string PlacementNote { get; set; }

        public string LastError { get; set; }

        /// <summary>Built into another component: <see cref="ElementUniqueId"/> is the carrier element.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool IsBuiltIn => BuiltInHosting.Is(PlacedHosting);

        public PlacedComponentInstance Clone() => (PlacedComponentInstance)MemberwiseClone();
    }

    /// <summary>
    /// <see cref="PlacedComponentInstance.PlacedHosting"/> value of a component switched on by a parameter of another
    /// component's element ("BuiltIn:Lukk paremal"). Such records share the carrier's element and must never delete it.
    /// </summary>
    public static class BuiltInHosting
    {
        public const string Prefix = "BuiltIn:";

        public static string For(string parameterName) => Prefix + parameterName;

        public static bool Is(string hosting) => hosting != null && hosting.StartsWith(Prefix, System.StringComparison.Ordinal);

        public static string ParameterOf(string hosting) => Is(hosting) ? hosting.Substring(Prefix.Length) : null;
    }
}
