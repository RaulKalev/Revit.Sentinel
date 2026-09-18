using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Xunit;

namespace Sentinel.Core.Tests
{
    public class PlacementCalculatorTests
    {
        private const double Tol = 0.001;

        private static PlacementPlan Plan(SentinelProject p, DoorSetInstance inst, DoorGeometry door = null) =>
            DoorSetPlacementCalculator.Calculate(inst, p.FindDoorSet(inst.DefinitionId), door ?? TestData.StraightDoor(), p);

        private static void AssertVec(Vec3 expected, Vec3 actual)
        {
            Assert.True(expected.DistanceTo(actual) < Tol, "Expected " + expected + " but was " + actual);
        }

        [Fact]
        public void Reader_IsPlacedBesideLatchJamb_OnUnsecuredSide()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));

            var plan = Plan(p, inst);
            var reader = plan.Placements.Single(x => x.Label == "Reader");

            // Hinge on -X → latch jamb at +500; +150 outward; side A face at y = +100; h = 1000.
            AssertVec(new Vec3(650, 100, 1000), reader.Position);
            AssertVec(Vec3.UnitY, reader.Facing);
            Assert.Equal(ResolvedSide.SideA, reader.Side);
            Assert.True(reader.CanPlace);
            Assert.Contains("latch jamb", reader.Explanation);
        }

        [Fact]
        public void FlipSet_MovesReaderAndRexToOppositeSides()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));

            var before = Plan(p, inst);
            Assert.True(DoorSetInstanceOperations.Flip(inst));
            var after = Plan(p, inst);

            var readerBefore = before.Placements.Single(x => x.Label == "Reader");
            var readerAfter = after.Placements.Single(x => x.Label == "Reader");
            var rexBefore = before.Placements.Single(x => x.Label == "REX");
            var rexAfter = after.Placements.Single(x => x.Label == "REX");

            Assert.Equal(ResolvedSide.SideA, readerBefore.Side);
            Assert.Equal(ResolvedSide.SideB, readerAfter.Side);
            Assert.Equal(ResolvedSide.SideB, rexBefore.Side);
            Assert.Equal(ResolvedSide.SideA, rexAfter.Side);

            AssertVec(new Vec3(650, -100, 1000), readerAfter.Position);
            AssertVec(-Vec3.UnitY, readerAfter.Facing);
            Assert.NotEqual(before.ConfigurationHash, after.ConfigurationHash);
        }

        [Fact]
        public void Flip_WithBothDirections_ReturnsFalse()
        {
            var inst = new DoorSetInstance { AccessDirection = AccessDirection.Both };
            Assert.False(DoorSetInstanceOperations.Flip(inst));
            Assert.Equal(AccessDirection.Both, inst.AccessDirection);
        }

        [Fact]
        public void BothDirections_DuplicatesReaderOnBothSides()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));
            inst.AccessDirection = AccessDirection.Both;

            var readers = Plan(p, inst).Placements.Where(x => x.Label == "Reader").ToList();

            Assert.Equal(2, readers.Count);
            Assert.Contains(readers, r => r.Side == ResolvedSide.SideA && !r.IsMirrorCopy);
            Assert.Contains(readers, r => r.Side == ResolvedSide.SideB && r.IsMirrorCopy && r.SlotKey.EndsWith(DoorSetPlacementCalculator.MirrorSuffix));
        }

        [Fact]
        public void RexPir_IsAboveDoorHead_OnSecuredSide()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));

            var rex = Plan(p, inst).Placements.Single(x => x.Label == "REX");

            AssertVec(new Vec3(0, -100, 2100 + 150), rex.Position);
            AssertVec(-Vec3.UnitY, rex.Facing);
        }

        [Fact]
        public void Lock_IsInsideTheWall()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));

            var lk = Plan(p, inst).Placements.Single(x => x.Label == "Lock");

            AssertVec(new Vec3(500, 0, 1050), lk.Position);
            Assert.Equal(ResolvedSide.InWall, lk.Side);
            Assert.True(lk.WallNormal.IsZero);
        }

        [Fact]
        public void RotatedAndMovedDoor_TransformsPlacements()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));

            var reader = Plan(p, inst, TestData.RotatedDoor()).Placements.Single(x => x.Label == "Reader");

            // origin (1000,2000,500) + 650 * (0,1,0) + 100 * (-1,0,0) + 1000 z
            AssertVec(new Vec3(900, 2650, 1500), reader.Position);
            AssertVec(new Vec3(-1, 0, 0), reader.Facing);
            Assert.Equal(180.0, reader.FacingAngleDeg, 6);
        }

        [Fact]
        public void HingeOverride_MovesLatchReferencedComponentsToOtherJamb()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));
            var before = Plan(p, inst).Placements.Single(x => x.Label == "Reader");

            DoorSetInstanceOperations.SwapHinge(inst, HingeSide.NegativeWidthAxis);
            var after = Plan(p, inst).Placements.Single(x => x.Label == "Reader");

            Assert.Equal(650, before.Position.X, 6);
            Assert.Equal(-650, after.Position.X, 6);
        }

        [Fact]
        public void UnknownHinge_IsAssumedAndReportedAsWarning()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));

            var plan = Plan(p, inst, TestData.StraightDoor(HingeSide.Unknown));

            Assert.True(plan.HingeAssumed);
            Assert.Contains(plan.Issues, i => i.Code == IssueCodes.HingeAssumed && i.Severity == IssueSeverity.Warning);
            Assert.Equal(p.Settings.DefaultHingeSide, plan.ResolvedHinge);
        }

        [Fact]
        public void UnconfiguredFamily_IsAVisibleErrorButStillPreviewable()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var reader = TestData.Component(p, ComponentCategory.CardReader);
            reader.FamilyName = null;
            var inst = TestData.Instance(p, TestData.Ds02(p));

            var placement = Plan(p, inst).Placements.Single(x => x.Label == "Reader");

            Assert.False(placement.CanPlace);
            var issue = placement.Issues.Single();
            Assert.Equal(IssueCodes.FamilyNotConfigured, issue.Code);
            Assert.Equal("Card Reader family not configured (Components page).", issue.Message);
        }

        [Fact]
        public void InvalidGeometry_ProducesUnsupportedGeometryError()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));
            var door = TestData.StraightDoor();
            door.WidthMm = 0;

            var plan = Plan(p, inst, door);

            Assert.Empty(plan.Placements);
            Assert.Contains(plan.Issues, i => i.Code == IssueCodes.UnsupportedGeometry && i.Severity == IssueSeverity.Error);
        }

        [Fact]
        public void ConfigurationHash_IsDeterministic()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));
            Assert.Equal(Plan(p, inst).ConfigurationHash, Plan(p, inst).ConfigurationHash);
        }

        [Fact]
        public void FaceTowardDoor_PointsAlongWallToTheOpening()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds01(p);
            var readerSlot = def.Components.First(c => c.Label == "Reader");
            readerSlot.Rule.Orientation = OrientationMode.FaceTowardDoor;
            var inst = TestData.Instance(p, def);

            var reader = Plan(p, inst).Placements.Single(x => x.Label == "Reader");

            AssertVec(new Vec3(-1, 0, 0), reader.Facing); // reader at +650 faces towards -X (the door)
        }

        [Fact]
        public void FamilyRotationOffset_IsAddedToInstanceRotationOnly()
        {
            var p = TestData.ProjectWithMappedFamilies();
            TestData.Component(p, ComponentCategory.CardReader).FamilyRotationOffsetDeg = 180;
            var inst = TestData.Instance(p, TestData.Ds02(p));

            var reader = Plan(p, inst).Placements.Single(x => x.Label == "Reader");

            // facing +Y (90°) + 90° family front convention + 180° calibration = 360° ≡ 0°
            Assert.Equal(90.0, reader.FacingAngleDeg, 6);
            Assert.Equal(0.0, reader.InstanceRotationDeg, 6);
        }

        [Fact]
        public void UncalibratedFamily_FrontMinusY_IsRotatedToFaceTheRoom()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));

            var reader = Plan(p, inst).Placements.Single(x => x.Label == "Reader");

            // A family whose front is local -Y must turn 180° to face +Y (side A).
            Assert.Equal(180.0, reader.InstanceRotationDeg, 6);
            Assert.True(new Vec3(0, -1, 0).RotateAboutZ(reader.InstanceRotationDeg).DistanceTo(reader.Facing) < 1e-9);
        }

        [Fact]
        public void MissingDefinition_IsReportedAsError()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));
            inst.DefinitionId = "does-not-exist";

            var plan = Plan(p, inst);

            Assert.Contains(plan.Issues, i => i.Code == IssueCodes.DefinitionMissing);
        }
    }
}
