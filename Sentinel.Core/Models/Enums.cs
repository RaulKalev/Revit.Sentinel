namespace Sentinel.Core.Models
{
    // All enums are persisted by name (StringEnumConverter), so values can be added or reordered
    // without corrupting stored data. Never rename a member that has shipped.

    /// <summary>Which kind of source a security set is attached to.</summary>
    public enum SecuritySetKind
    {
        Door,
        Room // reserved for Milestone 2+ (room-based detector sets)
    }

    /// <summary>
    /// Controlled direction through a door. Side A is the side the source door faces
    /// (Revit FacingOrientation, or the stable footprint normal for IFC geometry).
    /// </summary>
    public enum AccessDirection
    {
        /// <summary>Access is controlled when moving from side A into side B (reader on side A).</summary>
        SideAToSideB,
        /// <summary>Access is controlled when moving from side B into side A (reader on side B).</summary>
        SideBToSideA,
        /// <summary>Access is controlled in both directions.</summary>
        Both
    }

    public enum ComponentCategory
    {
        CardReader,
        DoorContact,
        ElectricLock,
        RexPir,
        RexButton,
        EmergencyRelease,
        Keypad,
        Intercom,
        GlassBreakDetector,
        PirDetector,
        PanicButton,
        Custom
    }

    /// <summary>How a component exists in Revit.</summary>
    public enum ComponentModelling
    {
        /// <summary>Its own family instance (default).</summary>
        OwnFamily,

        /// <summary>
        /// Part of another component's family, switched on with a Yes/No instance parameter (e.g. a magnet contact whose
        /// family contains the lock, toggled by "Lukk vasakul" / "Lukk paremal").
        /// </summary>
        BuiltIntoOtherComponent
    }

    /// <summary>How a component is created in Revit.</summary>
    public enum HostBehavior
    {
        /// <summary>Decide from the family placement type: work-plane/face based → linked wall face, level based → unhosted.</summary>
        Auto,
        /// <summary>Unhosted placement at the calculated point (level based, or on a vertical work plane for face-based families).</summary>
        Unhosted,
        /// <summary>Hosted on the face of the wall in the linked model found behind the calculated point.</summary>
        LinkedWallFace
    }

    /// <summary>Reference point along the door width the offsets are measured from.</summary>
    public enum PlacementReference
    {
        DoorCenter,
        HingeJamb,
        /// <summary>The jamb opposite the hinges, i.e. the opening (latch) edge of the door.</summary>
        LatchJamb
    }

    public enum PlacementSide
    {
        SideA,
        SideB,
        /// <summary>The side access is requested from (reader side). Follows the access direction.</summary>
        UnsecuredSide,
        /// <summary>The protected side (REX side). Follows the access direction.</summary>
        SecuredSide,
        /// <summary>Inside the wall / door frame (e.g. lock, door contact in the frame).</summary>
        InWall
    }

    public enum HeightReference
    {
        /// <summary>Mounting height measured from the bottom of the door (threshold).</summary>
        DoorBottom,
        /// <summary>Mounting height measured from the top of the door opening (head).</summary>
        DoorTop
    }

    public enum OrientationMode
    {
        /// <summary>Device front points away from the wall into the room on its side.</summary>
        FaceAwayFromWall,
        /// <summary>Device front points along the wall towards the door opening.</summary>
        FaceTowardDoor,
        /// <summary>Device keeps the wall direction regardless of side (front = side A normal).</summary>
        FollowWall,
        /// <summary>Fixed rotation relative to the door (RotationDeg from the side A normal).</summary>
        Fixed
    }

    /// <summary>Hinge location relative to the door width axis.</summary>
    public enum HingeSide
    {
        Unknown,
        PositiveWidthAxis,
        NegativeWidthAxis
    }

    /// <summary>Where the door geometry came from.</summary>
    public enum DoorGeometrySource
    {
        /// <summary>FamilyInstance location, facing and hand orientation.</summary>
        FamilyInstance,
        /// <summary>Estimated from element geometry (e.g. IFC DirectShape) using the plan footprint.</summary>
        EstimatedFromGeometry
    }

    public enum ComponentState
    {
        /// <summary>Part of the set but not (yet) placed in the model.</summary>
        Planned,
        Placed,
        /// <summary>Was placed, but the Revit element no longer exists.</summary>
        Missing,
        /// <summary>Placed, but the element was moved/rotated away from its placed position.</summary>
        ManuallyModified,
        /// <summary>Placement was attempted and failed; see LastError.</summary>
        Failed
    }

    public enum ReviewState
    {
        NotReviewed,
        Reviewed,
        NeedsReview
    }

    /// <summary>Derived status shown in the Doors grid.</summary>
    public enum SetStatus
    {
        Unassigned,
        Ready,
        Preview,
        Placed,
        Modified,
        MissingComponent,
        SourceChanged,
        Orphaned,
        Error,
        Ignored
    }

    public enum IssueSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>State of the source door compared with the stored snapshot.</summary>
    public enum SourceState
    {
        /// <summary>The source has not been checked in this session (link not scanned yet).</summary>
        NotChecked,
        Ok,
        Changed,
        /// <summary>The link is loaded but the door cannot be found (deleted in the source model).</summary>
        Missing,
        /// <summary>The link instance is missing or unloaded.</summary>
        LinkUnavailable
    }
}
