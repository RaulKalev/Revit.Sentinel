using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Xunit;

namespace Sentinel.Core.Tests
{
    public class RuleFromPositionTests
    {
        private static void MarkPlaced(DoorSetInstance inst, PlacementPlan plan)
        {
            inst.Components = plan.Placements.Select(pl => new PlacedComponentInstance
            {
                SlotKey = pl.SlotKey,
                ComponentDefinitionId = pl.ComponentDefinitionId,
                Label = pl.Label,
                ElementUniqueId = "elem-" + pl.SlotKey,
                State = ComponentState.Placed,
                CalculatedPosition = pl.Position,
                CalculatedRotationDeg = pl.InstanceRotationDeg,
                PlacedPosition = pl.Position,
                PlacedRotationDeg = pl.InstanceRotationDeg
            }).ToList();
            inst.PlacedConfigurationHash = plan.ConfigurationHash;
        }

        /// <summary>Moves the slot's element by (along the door width, out of the wall, up) and a turn, then derives the rule.</summary>
        private static RuleFromPositionResult Move(SentinelProject p, DoorSetInstance inst, DoorGeometry g, string label,
            double along, double outOfWall, double up, double turn, out CalculatedPlacement slot, out Vec3 moved, out double rot)
        {
            var def = p.FindDoorSet(inst.DefinitionId);
            var plan = DoorSetPlacementCalculator.Calculate(inst, def, g, p);
            var s = plan.Placements.First(x => x.Label == label && !x.IsMirrorCopy);
            slot = s;
            var w = g.WidthAxis.Flatten().Normalize();
            moved = slot.Position + w * along + slot.WallNormal * outOfWall + Vec3.UnitZ * up;
            rot = slot.InstanceRotationDeg + turn;
            var comp = EffectiveSetResolver.Resolve(def, inst.Overrides, p.FindComponent).First(c => c.RuleId == s.RuleId);
            return RuleFromPosition.Derive(comp.Rule, s, g, plan.ResolvedHinge, moved, rot, comp.Component?.DefaultMountingHeightMm);
        }

        [Theory]
        [InlineData(HingeSide.NegativeWidthAxis, AccessDirection.SideAToSideB, 0)]
        [InlineData(HingeSide.PositiveWidthAxis, AccessDirection.SideAToSideB, 0)]
        [InlineData(HingeSide.NegativeWidthAxis, AccessDirection.SideBToSideA, 0)]
        [InlineData(HingeSide.PositiveWidthAxis, AccessDirection.SideBToSideA, 45)]
        public void DerivedRule_PutsTheComponentExactlyWhereItWasMoved(HingeSide hinge, AccessDirection dir, double clearance)
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var inst = TestData.Instance(p, def);
            inst.AccessDirection = dir;
            var g = TestData.StraightDoor(hinge);
            var first = DoorSetPlacementCalculator.Calculate(inst, def, g, p).Placements.First(x => x.Label == "Reader");
            if (clearance > 0) inst.WallClearances[WallClearance.Key(first.SlotKey, first.Side.ToString())] = clearance;

            CalculatedPlacement slot;
            Vec3 moved;
            double rot;
            var r = Move(p, inst, g, "Reader", 80, 12, 100, 15, out slot, out moved, out rot);
            Assert.True(r.Success, r.Error);

            // Use it as this door's rule: recalculating gives the moved position and rotation.
            DoorSetInstanceOperations.OverrideRule(inst, new ComponentRuleOverride
            {
                RuleId = slot.RuleId,
                AlongWallOffsetMm = r.Rule.AlongWallOffsetMm,
                FromWallOffsetMm = r.Rule.FromWallOffsetMm,
                MountingHeightMm = r.Rule.MountingHeightMm,
                RotationDeg = r.Rule.RotationDeg
            });
            var again = DoorSetPlacementCalculator.Calculate(inst, def, g, p).Placements.First(x => x.SlotKey == slot.SlotKey);
            Assert.True(again.Position.DistanceTo(moved) < 1.0, "position off by " + again.Position.DistanceTo(moved));
            Assert.True(Angles.Difference(again.InstanceRotationDeg, rot) < 0.1);
            Assert.Contains("height", r.ChangeText);
            Assert.Contains("rotation", r.ChangeText);
        }

        [Fact]
        public void HeightMeasuredFromTheDoorHead_StaysMeasuredFromTheHead()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var inst = TestData.Instance(p, def);
            var slotRule = def.Components.First();
            slotRule.Rule.HeightReference = HeightReference.DoorTop;
            slotRule.Rule.MountingHeightMm = 150;
            var g = TestData.StraightDoor();
            var label = EffectiveSetResolver.Resolve(def, inst.Overrides, p.FindComponent).First(c => c.RuleId == slotRule.Id).DisplayLabel;

            CalculatedPlacement slot;
            Vec3 moved;
            double rot;
            var r = Move(p, inst, g, label, 0, 0, 50, 0, out slot, out moved, out rot);
            Assert.Equal(HeightReference.DoorTop, r.Rule.HeightReference);
            Assert.Equal(200, r.Rule.MountingHeightMm.Value, 3);
            Assert.Equal(slotRule.Rule.RotationDeg, r.Rule.RotationDeg);
        }

        [Fact]
        public void MovedThroughTheWall_IsRefused()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));
            var g = TestData.StraightDoor();
            CalculatedPlacement slot;
            Vec3 moved;
            double rot;
            var r = Move(p, inst, g, "Reader", 0, -(g.WallThicknessMm + 100), 0, 0, out slot, out moved, out rot);
            Assert.False(r.Success);
            Assert.Contains("Flip sides", r.Error);
        }

        [Fact]
        public void Adopting_MakesTheRecordPlacedAgain_AndKeepsTheSetInSync()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var inst = TestData.Instance(p, def);
            var g = TestData.StraightDoor();
            var before = DoorSetPlacementCalculator.Calculate(inst, def, g, p);
            MarkPlaced(inst, before);

            CalculatedPlacement slot;
            Vec3 moved;
            double rot;
            var r = Move(p, inst, g, "Reader", 60, 0, 0, 0, out slot, out moved, out rot);
            var rec = inst.Components.First(c => c.SlotKey == slot.SlotKey);
            rec.State = ComponentState.ManuallyModified;
            rec.ActualPosition = moved;
            rec.ActualRotationDeg = rot;

            DoorSetInstanceOperations.OverrideRule(inst, new ComponentRuleOverride { RuleId = slot.RuleId, AlongWallOffsetMm = r.Rule.AlongWallOffsetMm });
            var after = DoorSetPlacementCalculator.Calculate(inst, def, g, p);
            RuleFromPosition.AdoptPlacedPosition(inst, before, after, slot.RuleId);

            Assert.Equal(ComponentState.Placed, rec.State);
            Assert.Equal(moved, rec.PlacedPosition);
            Assert.Equal(after.ConfigurationHash, inst.PlacedConfigurationHash);
        }

        [Fact]
        public void Adopting_DoesNotHidePendingChangesOfOtherComponents()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var inst = TestData.Instance(p, def);
            var g = TestData.StraightDoor();
            var before = DoorSetPlacementCalculator.Calculate(inst, def, g, p);
            MarkPlaced(inst, before);
            inst.PlacedConfigurationHash = "older"; // something else was already out of date

            var after = DoorSetPlacementCalculator.Calculate(inst, def, g, p);
            RuleFromPosition.AdoptPlacedPosition(inst, before, after, before.Placements[0].RuleId);
            Assert.Equal("older", inst.PlacedConfigurationHash);
        }
    }
}
