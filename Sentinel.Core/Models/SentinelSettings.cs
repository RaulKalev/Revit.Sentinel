using System.Collections.Generic;
using System.Linq;

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
        /// <summary>First source link (kept for projects saved by versions that read a single link).</summary>
        public string DiscoveryLinkUniqueId { get; set; }

        /// <summary>Linked models doors are collected from (architecture is often split, e.g. shell + interior).</summary>
        public List<string> DiscoveryLinkUniqueIds { get; set; } = new List<string>();

        /// <summary>Leave out elements in the Doors category whose type name starts with "Window" (IFC exports).</summary>
        public bool DiscoverySkipWindowTypes { get; set; }

        /// <summary>Source links, falling back to the single link stored by older versions.</summary>
        public List<string> GetDiscoveryLinks()
        {
            var list = (DiscoveryLinkUniqueIds ?? new List<string>()).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            if (list.Count == 0 && !string.IsNullOrEmpty(DiscoveryLinkUniqueId)) list.Add(DiscoveryLinkUniqueId);
            return list;
        }

        /// <summary>Stores the source links (and the first one in the legacy single-link field).</summary>
        public void SetDiscoveryLinks(IEnumerable<string> linkUniqueIds)
        {
            DiscoveryLinkUniqueIds = (linkUniqueIds ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            DiscoveryLinkUniqueId = DiscoveryLinkUniqueIds.FirstOrDefault();
        }
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

        /// <summary>Default view for "Zoom to door" and the preview's auto-zoom; the other one is offered as an extra command.</summary>
        public DoorZoomView ZoomView { get; set; } = DoorZoomView.View3D;

        /// <summary>
        /// Measure walls of all loaded models around wall-side components before review / placement and move them out
        /// of wall material (see WallClearance). Off for now: it slows placement down; a separate clash check will
        /// replace it. While off, no measuring happens and stored push-outs are not applied.
        /// </summary>
        public bool CheckWallClearance { get; set; }

        /// <summary>
        /// Floor plans whose name contains one of these words (comma separated, first wins) are preferred when "Zoom to
        /// door" opens a plan, e.g. "Security, EL". Empty = no preference.
        /// </summary>
        public string PlanNameKeywords { get; set; }

        /// <summary>How close "Zoom to door" gets in floor plans: 100 = the standard framing, 200 = twice as close.</summary>
        public double PlanZoomPercent { get; set; } = ZoomLevels.Default;

        /// <summary>How close "Zoom to door" gets in 3D (also scales the section box when zooming out).</summary>
        public double View3DZoomPercent { get; set; } = ZoomLevels.Default;

        /// <summary>
        /// Linked models by priority (link names without the instance number, e.g. "ITM_TP_AR.ifc"). When the same door
        /// is modelled in several links, only the door of the highest model is listed; the others are shown as Ignored.
        /// Links not named here follow in discovery order.
        /// </summary>
        public List<string> LinkPriority { get; set; } = new List<string>();

        public SentinelSettings Clone()
        {
            var c = (SentinelSettings)MemberwiseClone();
            c.DiscoveryLevelNames = new List<string>(DiscoveryLevelNames ?? new List<string>());
            c.DiscoveryLinkUniqueIds = new List<string>(DiscoveryLinkUniqueIds ?? new List<string>());
            c.CapturedParameterNames = new List<string>(CapturedParameterNames ?? new List<string>());
            c.LinkPriority = new List<string>(LinkPriority ?? new List<string>());
            return c;
        }
    }
}
