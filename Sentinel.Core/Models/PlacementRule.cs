namespace Sentinel.Core.Models
{
    /// <summary>
    /// Data-driven placement rule for one component relative to a door. Placement logic never
    /// branches on family or component names — everything comes from these values.
    ///
    /// Coordinate conventions (door local frame):
    ///  - Along-wall offset: measured from <see cref="Reference"/> along the door width axis. For jamb
    ///    references a positive value moves away from the opening (outwards, beyond the jamb); a negative
    ///    value moves over the door leaf. For DoorCenter a positive value moves towards the hinge jamb.
    ///  - From-wall offset: distance from the wall face on the chosen side, into the room. For InWall it is
    ///    measured from the wall centre line towards side A.
    ///  - Mounting height: measured from the door bottom or top (<see cref="HeightReference"/>).
    /// </summary>
    public class PlacementRule
    {
        public PlacementReference Reference { get; set; } = PlacementReference.LatchJamb;
        public PlacementSide Side { get; set; } = PlacementSide.UnsecuredSide;
        public double AlongWallOffsetMm { get; set; }
        public double FromWallOffsetMm { get; set; }

        /// <summary>Null = use the component definition's default mounting height.</summary>
        public double? MountingHeightMm { get; set; }

        public HeightReference HeightReference { get; set; } = HeightReference.DoorBottom;
        public OrientationMode Orientation { get; set; } = OrientationMode.FaceAwayFromWall;

        /// <summary>Additional rotation (degrees, counter-clockwise in plan). For Fixed orientation it is the full rotation.</summary>
        public double RotationDeg { get; set; }

        /// <summary>
        /// When the access direction is Both and the side is Unsecured/Secured, place a second copy
        /// on the opposite side (e.g. readers on both sides).
        /// </summary>
        public bool DuplicateWhenBothDirections { get; set; }

        public PlacementRule Clone() => (PlacementRule)MemberwiseClone();
    }
}
