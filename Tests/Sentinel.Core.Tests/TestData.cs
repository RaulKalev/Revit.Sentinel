using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Library;
using Sentinel.Core.Models;

namespace Sentinel.Core.Tests
{
    /// <summary>Shared fixtures: a 1000 x 2100 door in a 200 mm wall, facing +Y, width along +X.</summary>
    internal static class TestData
    {
        public static DoorGeometry StraightDoor(HingeSide hinge = HingeSide.NegativeWidthAxis) => new DoorGeometry
        {
            Origin = new Vec3(0, 0, 0),
            Facing = Vec3.UnitY,
            WidthAxis = Vec3.UnitX,
            WidthMm = 1000,
            HeightMm = 2100,
            WallThicknessMm = 200,
            Hinge = hinge,
            Source = DoorGeometrySource.FamilyInstance
        };

        /// <summary>Same door rotated 90° CCW and moved to (1000, 2000, 500).</summary>
        public static DoorGeometry RotatedDoor() => new DoorGeometry
        {
            Origin = new Vec3(1000, 2000, 500),
            Facing = new Vec3(-1, 0, 0),
            WidthAxis = new Vec3(0, 1, 0),
            WidthMm = 1000,
            HeightMm = 2100,
            WallThicknessMm = 200,
            Hinge = HingeSide.NegativeWidthAxis
        };

        /// <summary>Default library with every component mapped to a fake family so placements are placeable.</summary>
        public static SentinelProject ProjectWithMappedFamilies()
        {
            var p = DefaultLibrary.CreateProject();
            foreach (var c in p.ComponentDefinitions)
            {
                c.FamilyName = "Sec_" + c.Category;
                c.TypeName = "Standard";
            }
            return p;
        }

        public static DoorSetDefinition Ds02(SentinelProject p) => p.DoorSetDefinitions.First(d => d.Code == "DS-02");
        public static DoorSetDefinition Ds01(SentinelProject p) => p.DoorSetDefinitions.First(d => d.Code == "DS-01");

        public static ComponentDefinition Component(SentinelProject p, ComponentCategory c) =>
            p.ComponentDefinitions.First(x => x.Category == c);

        public static SourceDoorReference Source(string doorUid = "door-1", string link = "link-1", string guid = "2O2Fr$t4X7Zf8NOew3FLOH") =>
            new SourceDoorReference
            {
                LinkInstanceUniqueId = link,
                LinkDocumentTitle = "ARH.ifc",
                DoorUniqueId = doorUid,
                IfcGlobalId = guid,
                Mark = "D102",
                LastKnownGeometry = StraightDoor()
            };

        public static DoorSetInstance Instance(SentinelProject p, DoorSetDefinition def) => new DoorSetInstance
        {
            DefinitionId = def.Id,
            Source = Source(),
            AccessDirection = AccessDirection.SideAToSideB
        };
    }
}
