using System;
using Newtonsoft.Json;
using Sentinel.Core.Models;

namespace Sentinel.Core.Geometry
{
    /// <summary>
    /// Door abstraction used by the placement engine, in host project coordinates (mm).
    ///
    /// Local frame:
    ///  - <see cref="Origin"/>: centre of the door opening at the door bottom, on the wall centre line.
    ///  - <see cref="Facing"/>: horizontal unit normal pointing to side A.
    ///  - <see cref="WidthAxis"/>: horizontal unit vector along the wall (perpendicular to Facing).
    ///  - Hinge jamb = Origin + WidthAxis * (±Width/2) depending on <see cref="Hinge"/>.
    /// </summary>
    public class DoorGeometry
    {
        public Vec3 Origin { get; set; }
        public Vec3 Facing { get; set; } = Vec3.UnitY;
        public Vec3 WidthAxis { get; set; } = Vec3.UnitX;
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double WallThicknessMm { get; set; }
        public HingeSide Hinge { get; set; } = HingeSide.Unknown;

        /// <summary>The hinge side was read from the door handle in the geometry (IFC doors without orientation data).</summary>
        public bool HingeFromHandle { get; set; }
        public DoorGeometrySource Source { get; set; } = DoorGeometrySource.FamilyInstance;

        /// <summary>Plan rotation of the door (angle of the side A normal), degrees.</summary>
        [JsonIgnore]
        public double RotationDeg => Facing.PlanAngleDeg();

        /// <summary>True when the frame is usable for placement.</summary>
        [JsonIgnore]
        public bool IsValid =>
            !Facing.IsZero && !WidthAxis.IsZero && WidthMm > 1 && HeightMm > 1 &&
            Math.Abs(Facing.Normalize().Dot(WidthAxis.Normalize())) < 0.05;

        public DoorGeometry Clone() => (DoorGeometry)MemberwiseClone();
    }
}
