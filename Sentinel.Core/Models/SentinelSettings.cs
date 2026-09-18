using System.Collections.Generic;

namespace Sentinel.Core.Models
{
    public enum DiscoveryScope
    {
        AllDoors,
        ActiveView,
        SelectedLevels
    }

    /// <summary>Project-level Sentinel settings (persisted with the project data).</summary>
    public class SentinelSettings
    {
        // ---- discovery ----
        public string DiscoveryLinkUniqueId { get; set; }
        public DiscoveryScope DiscoveryScope { get; set; } = DiscoveryScope.AllDoors;
        public List<string> DiscoveryLevelNames { get; set; } = new List<string>();

        /// <summary>Distance from the wall face used to probe rooms on either side of a door.</summary>
        public double RoomProbeDistanceMm { get; set; } = 400;

        /// <summary>Source parameters captured into the door record (used for display and assignment rules).</summary>
        public List<string> CapturedParameterNames { get; set; } = new List<string>
        {
            "FireRating", "IsExternal", "SecurityRating", "OperationType", "AcousticRating",
            "HandicapAccessible", "Reference", "Description", "Comments"
        };

        // ---- hinge convention ----
        /// <summary>
        /// Revit door families differ in which side HandOrientation points to. When true, the hinge is taken to be on
        /// the HandOrientation side; when false (default), on the opposite side. Per-door overrides always win.
        /// </summary>
        public bool HingeOnHandOrientationSide { get; set; }

        /// <summary>Hinge side assumed when the source provides none (IFC geometry). Always reported as a warning.</summary>
        public HingeSide DefaultHingeSide { get; set; } = HingeSide.NegativeWidthAxis;

        // ---- tolerances ----
        public double SourceMoveToleranceMm { get; set; } = 20;
        public double SourceRotationToleranceDeg { get; set; } = 1.0;
        public double SourceSizeToleranceMm { get; set; } = 5;
        public double ComponentMoveToleranceMm { get; set; } = 10;
        public double ComponentRotationToleranceDeg { get; set; } = 1.0;

        // ---- placement ----
        /// <summary>Optional instance text parameter that receives "SetCode / DoorMark" on placed elements.</summary>
        public string IdentityParameterName { get; set; }

        /// <summary>Maximum distance searched for a linked wall face behind a face-hosted component.</summary>
        public double WallSearchDistanceMm { get; set; } = 600;

        // ---- preview ----
        /// <summary>
        /// When true and the active view is a 3D view, the preview zooms there without switching views. Otherwise
        /// (default) the "Sentinel Focus" 3D view with a section box around the door is used.
        /// </summary>
        public bool PreviewInActiveView { get; set; }

        public SentinelSettings Clone()
        {
            var c = (SentinelSettings)MemberwiseClone();
            c.DiscoveryLevelNames = new List<string>(DiscoveryLevelNames ?? new List<string>());
            c.CapturedParameterNames = new List<string>(CapturedParameterNames ?? new List<string>());
            return c;
        }
    }
}
