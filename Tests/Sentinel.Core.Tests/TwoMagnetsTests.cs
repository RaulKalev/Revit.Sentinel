using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Xunit;

namespace Sentinel.Core.Tests
{
    /// <summary>A door with two magnets (e.g. a double door): the lock is built into the one that is chosen.</summary>
    public class TwoMagnetsTests
    {
        private static SentinelProject Project(out DoorSetDefinition def, out SetComponentRule first, out SetComponentRule second)
        {
            var p = TestData.ProjectWithMappedFamilies();
            var contact = TestData.Component(p, ComponentCategory.DoorContact);
            var lockDef = TestData.Component(p, ComponentCategory.ElectricLock);
            lockDef.Modelling = ComponentModelling.BuiltIntoOtherComponent;
            lockDef.CarrierComponentId = contact.Id;
            lockDef.CarrierParameterLeft = "Lukk vasakul";
            lockDef.CarrierParameterRight = "Lukk paremal";

            def = TestData.Ds01(p);
            first = def.Components.Single(c => c.Label == "Door Contact");
            first.Label = "Magnet latch";
            second = new SetComponentRule
            {
                ComponentDefinitionId = contact.Id,
                Label = "Magnet hinge",
                Rule = first.Rule.Clone()
            };
            second.Rule.Reference = PlacementReference.HingeJamb;
            def.Components.Add(second);
            return p;
        }

        private static CalculatedPlacement Lock(PlacementPlan plan) => plan.Placements.Single(x => x.Label == "Lock");

        [Fact]
        public void WithoutAChoice_TheFirstMagnetCarriesTheLock()
        {
            DoorSetDefinition def; SetComponentRule first, second;
            var p = Project(out def, out first, out second);
            var plan = DoorSetPlacementCalculator.Calculate(TestData.Instance(p, def), def, TestData.StraightDoor(), p);
            Assert.Equal(first.Id, Lock(plan).CarrierSlotKey);
        }

        [Fact]
        public void TheSetTypeCanChooseTheOtherMagnet()
        {
            DoorSetDefinition def; SetComponentRule first, second;
            var p = Project(out def, out first, out second);
            var inst = TestData.Instance(p, def);
            var before = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p);

            def.Components.Single(c => c.Label == "Lock").Rule.CarrierRuleId = second.Id;
            var after = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p);

            Assert.Equal(second.Id, Lock(after).CarrierSlotKey);
            Assert.Equal("Magnet hinge", Lock(after).CarrierLabel);
            Assert.NotEqual(before.ConfigurationHash, after.ConfigurationHash); // placed doors show Modified → Update switches it
        }

        [Fact]
        public void OneDoorCanChooseDifferently()
        {
            DoorSetDefinition def; SetComponentRule first, second;
            var p = Project(out def, out first, out second);
            var inst = TestData.Instance(p, def);
            var lockRule = def.Components.Single(c => c.Label == "Lock");
            DoorSetInstanceOperations.OverrideRule(inst, new ComponentRuleOverride { RuleId = lockRule.Id, CarrierRuleId = second.Id });

            Assert.Equal(second.Id, Lock(DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p)).CarrierSlotKey);
            Assert.Equal(first.Id, Lock(DoorSetPlacementCalculator.Calculate(TestData.Instance(p, def), def, TestData.StraightDoor(), p)).CarrierSlotKey);
        }

        [Fact]
        public void AChosenMagnetRemovedFromTheDoor_FallsBackToTheOtherOne_AndSaysSo()
        {
            DoorSetDefinition def; SetComponentRule first, second;
            var p = Project(out def, out first, out second);
            def.Components.Single(c => c.Label == "Lock").Rule.CarrierRuleId = second.Id;
            var inst = TestData.Instance(p, def);
            DoorSetInstanceOperations.RemoveComponent(inst, second.Id);

            var lk = Lock(DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p));
            Assert.Equal(first.Id, lk.CarrierSlotKey);
            Assert.Contains(lk.Issues, i => i.Message.Contains("not on this door"));
        }
    }
}
