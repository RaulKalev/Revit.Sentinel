using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Xunit;

namespace Sentinel.Core.Tests
{
    public class OverrideAndOperationTests
    {
        [Fact]
        public void AddedComponent_IsStoredAsOverride_NotInTemplate()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var templateCount = def.Components.Count;
            var inst = TestData.Instance(p, def);

            DoorSetInstanceOperations.AddComponent(inst, TestData.Component(p, ComponentCategory.EmergencyRelease));

            Assert.Equal(templateCount, def.Components.Count);
            Assert.Single(inst.Overrides.AddedComponents);
            var effective = EffectiveSetResolver.Resolve(def, inst.Overrides, p.FindComponent);
            Assert.Equal(templateCount + 1, effective.Count);
            Assert.True(effective.Last().IsAddedByOverride);
            Assert.Equal("Emergency Release", effective.Last().DisplayLabel);
        }

        [Fact]
        public void ReaderOffsetOverride_AppliesOnlyToThisInstance()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var readerRuleId = def.Components.First(c => c.Label == "Reader").Id;
            var inst = TestData.Instance(p, def);
            var other = TestData.Instance(p, def);

            DoorSetInstanceOperations.OverrideRule(inst, new ComponentRuleOverride { RuleId = readerRuleId, AlongWallOffsetMm = 350 });

            var mine = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p).Placements.Single(x => x.Label == "Reader");
            var theirs = DoorSetPlacementCalculator.Calculate(other, def, TestData.StraightDoor(), p).Placements.Single(x => x.Label == "Reader");

            Assert.Equal(850, mine.Position.X, 6);
            Assert.Equal(650, theirs.Position.X, 6);
            Assert.Equal(150, def.Components.First(c => c.Id == readerRuleId).Rule.AlongWallOffsetMm);
            Assert.True(EffectiveSetResolver.Resolve(def, inst.Overrides, p.FindComponent).First(c => c.RuleId == readerRuleId).IsOverridden);
        }

        [Fact]
        public void RemoveTemplateComponent_ExcludesIt_AndRestoreBringsItBack()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var rexId = def.Components.First(c => c.Label == "REX").Id;
            var inst = TestData.Instance(p, def);

            DoorSetInstanceOperations.RemoveComponent(inst, rexId);
            Assert.DoesNotContain(EffectiveSetResolver.Resolve(def, inst.Overrides, p.FindComponent), c => c.RuleId == rexId);
            Assert.Contains(rexId, inst.Overrides.RemovedRuleIds);

            DoorSetInstanceOperations.RestoreComponent(inst, rexId);
            Assert.Contains(EffectiveSetResolver.Resolve(def, inst.Overrides, p.FindComponent), c => c.RuleId == rexId);
        }

        [Fact]
        public void RemoveAddedComponent_DeletesTheOverrideEntry()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));
            var slot = DoorSetInstanceOperations.AddComponent(inst, TestData.Component(p, ComponentCategory.Keypad));

            DoorSetInstanceOperations.RemoveComponent(inst, slot.Id);

            Assert.Empty(inst.Overrides.AddedComponents);
            Assert.Empty(inst.Overrides.RemovedRuleIds);
        }

        [Fact]
        public void ChangeComponentType_SwapsDefinitionForThisDoor()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var rexId = def.Components.First(c => c.Label == "REX").Id;
            var button = TestData.Component(p, ComponentCategory.RexButton);
            var inst = TestData.Instance(p, def);

            DoorSetInstanceOperations.ChangeComponentType(inst, rexId, button.Id);

            var rex = EffectiveSetResolver.Resolve(def, inst.Overrides, p.FindComponent).Single(c => c.RuleId == rexId);
            Assert.Same(button, rex.Component);
            // The slot keeps the template rule; only the device type changed.
            Assert.Equal(PlacementReference.DoorCenter, rex.Rule.Reference);
        }

        [Fact]
        public void ChangingSetType_DropsStaleOverrides_KeepsDoorSpecificAdditions()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var ds02 = TestData.Ds02(p);
            var ds01 = TestData.Ds01(p);
            var inst = TestData.Instance(p, ds02);
            var rexId = ds02.Components.First(c => c.Label == "REX").Id;
            DoorSetInstanceOperations.RemoveComponent(inst, rexId);
            DoorSetInstanceOperations.OverrideRule(inst, new ComponentRuleOverride { RuleId = rexId, MountingHeightMm = 10 });
            var added = DoorSetInstanceOperations.AddComponent(inst, TestData.Component(p, ComponentCategory.Intercom));

            DoorSetInstanceOperations.AssignDefinition(inst, ds01);

            Assert.Equal(ds01.Id, inst.DefinitionId);
            Assert.Empty(inst.Overrides.RemovedRuleIds);
            Assert.Empty(inst.Overrides.RuleOverrides);
            Assert.Single(inst.Overrides.AddedComponents);
            Assert.Equal(added.Id, inst.Overrides.AddedComponents[0].Id);
        }

        [Fact]
        public void MissingComponentDefinition_IsReportedPerSlot()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var inst = TestData.Instance(p, def);
            p.ComponentDefinitions.RemoveAll(c => c.Category == ComponentCategory.RexPir);

            var plan = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p);

            var rex = plan.Placements.Single(x => x.Label == "REX");
            Assert.Contains(rex.Issues, i => i.Code == IssueCodes.ComponentDefinitionMissing);
        }

        [Fact]
        public void AcceptManualPosition_UsesActualPositionAsNewReference()
        {
            var c = new PlacedComponentInstance
            {
                State = ComponentState.ManuallyModified,
                PlacedPosition = new Geometry.Vec3(0, 0, 0),
                ActualPosition = new Geometry.Vec3(300, 0, 0),
                ActualRotationDeg = 90
            };

            DoorSetInstanceOperations.AcceptManualPosition(c);

            Assert.True(c.ManualPositionAccepted);
            Assert.Equal(ComponentState.Placed, c.State);
            string detail;
            Assert.False(Status.ComponentDriftDetector.IsDrifted(c, new Geometry.Vec3(300, 0, 0), 90, new SentinelSettings(), out detail));
        }
    }
}
