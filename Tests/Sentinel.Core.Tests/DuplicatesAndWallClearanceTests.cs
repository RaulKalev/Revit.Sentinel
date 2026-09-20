using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Xunit;

namespace Sentinel.Core.Tests
{
    public class DuplicatesAndWallClearanceTests
    {
        private static DiscoveredDoor Door(string link, string mark, double x) => new DiscoveredDoor
        {
            Current = new SourceDoorReference
            {
                LinkInstanceUniqueId = link,
                LinkName = link + ".ifc : 1",
                DoorUniqueId = link + "-" + mark,
                Mark = mark,
                LastKnownGeometry = new DoorGeometry { Origin = new Vec3(x, 0, 0), WidthMm = 900, HeightMm = 2100, WallThicknessMm = 150 }
            }
        };

        [Fact]
        public void Duplicates_KeepTheDoorOfTheHigherModel()
        {
            var ar = Door("AR", "VU-02", 0);
            var sa = Door("SA", "SKU13", 80);
            var doors = new List<DiscoveredDoor> { sa, ar };
            CrossLinkDuplicates.Mark(doors);

            Assert.Equal(1, DuplicatePriority.Apply(doors, new[] { "AR.ifc", "SA.ifc" }));
            Assert.Same(ar, sa.PreferredDuplicate);
            Assert.Null(ar.PreferredDuplicate);

            DuplicatePriority.Apply(doors, new[] { "SA.ifc" }); // SA first now
            Assert.Same(sa, ar.PreferredDuplicate);
            Assert.Null(sa.PreferredDuplicate);
        }

        [Fact]
        public void Duplicates_WithoutPriority_FollowDiscoveryOrder_AndSingleDoorsStay()
        {
            var sa = Door("SA", "SKU13", 80);
            var ar = Door("AR", "VU-02", 0);
            var alone = Door("AR", "VU-09", 5000);
            var doors = new List<DiscoveredDoor> { sa, ar, alone };
            CrossLinkDuplicates.Mark(doors);

            DuplicatePriority.Apply(doors, null);
            Assert.Same(sa, ar.PreferredDuplicate); // SA was found first
            Assert.Null(alone.PreferredDuplicate);
        }

        [Fact]
        public void LinkPriority_SurvivesSaveAndClone()
        {
            var p = TestData.ProjectWithMappedFamilies();
            p.Settings.LinkPriority = new List<string> { "AR.ifc", "SA.ifc" };
            var back = Sentinel.Core.Persistence.SentinelProjectSerializer.FromPayload(
                Sentinel.Core.Persistence.SentinelProjectSerializer.ToPayload(p, "test", "1.0")).Project;
            Assert.Equal(new[] { "AR.ifc", "SA.ifc" }, back.Settings.LinkPriority);
            Assert.Equal(new[] { "AR.ifc", "SA.ifc" }, p.Settings.Clone().LinkPriority);
        }

        [Fact]
        public void ExitDistance_OnlyMovesWhenInsideMaterial()
        {
            // Point on the face of its own wall: touching only.
            Assert.Equal(0, WallClearance.ExitDistance(new[] { new MaterialSpan(-200, 0) }));
            // Lining of another model in front (overlapping by 5 mm) plus the core wall behind.
            Assert.Equal(45, WallClearance.ExitDistance(new[] { new MaterialSpan(-200, 0), new MaterialSpan(-5, 45) }), 3);
            // Two touching layers in front: out of both.
            Assert.Equal(70, WallClearance.ExitDistance(new[] { new MaterialSpan(-5, 45), new MaterialSpan(45, 70) }), 3);
            // Free air, opposite corridor wall further out: nothing to do.
            Assert.Equal(0, WallClearance.ExitDistance(new[] { new MaterialSpan(900, 1100) }));
            // Material goes on (crossing wall along the normal): refuse to move.
            Assert.Equal(-1, WallClearance.ExitDistance(new[] { new MaterialSpan(-3000, 3000) }));
        }

        [Fact]
        public void StoredClearance_MovesTheComponent_OnItsSideOnly_AndChangesTheHash()
        {
            var p = TestData.ProjectWithMappedFamilies();
            p.Settings.CheckWallClearance = true;
            var inst = TestData.Instance(p, TestData.Ds02(p));
            var def = TestData.Ds02(p);
            var before = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p);
            var target = before.Placements.First(x => x.Side == ResolvedSide.SideA);

            Assert.True(WallClearance.Store(inst, new Dictionary<string, double> { { WallClearance.Key(target.SlotKey, "SideA"), 44.6 } }));
            Assert.False(WallClearance.Store(inst, new Dictionary<string, double> { { WallClearance.Key(target.SlotKey, "SideA"), 45.2 } }));

            var after = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p);
            var moved = after.Placements.First(x => x.SlotKey == target.SlotKey);
            Assert.Equal(45, (moved.Position - target.Position).Dot(target.WallNormal), 3);
            Assert.Equal(45, moved.WallClearanceMm, 3);
            Assert.Contains("moved 45 mm", moved.Explanation);
            Assert.NotEqual(before.ConfigurationHash, after.ConfigurationHash);

            // Measuring uses the plan without push-outs.
            var raw = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p, false);
            Assert.Equal(before.ConfigurationHash, raw.ConfigurationHash);

            // Flipped to the other side: the old measurement does not apply.
            DoorSetInstanceOperations.Flip(inst);
            var flipped = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p);
            Assert.All(flipped.Placements, x => Assert.Equal(0, x.WallClearanceMm));
        }

        [Fact]
        public void WithTheCheckOff_StoredClearancesAreIgnored()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var inst = TestData.Instance(p, def);
            var before = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p);
            var slot = before.Placements.First(x => x.Side == ResolvedSide.SideA);
            inst.WallClearances[WallClearance.Key(slot.SlotKey, "SideA")] = 45;

            Assert.False(p.Settings.CheckWallClearance); // off by default (slow; a separate clash check will replace it)
            var after = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p);
            Assert.Equal(before.ConfigurationHash, after.ConfigurationHash);
        }
    }
}
