using System.Linq;
using Newtonsoft.Json;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Xunit;

namespace Sentinel.Core.Tests
{
    /// <summary>A lock that can be built into several magnet contact types, all switched with the same parameters.</summary>
    public class SeveralCarrierTypesTests
    {
        private static SentinelProject Project(out ComponentDefinition contact, out ComponentDefinition otherContact)
        {
            var p = TestData.ProjectWithMappedFamilies();
            contact = TestData.Component(p, ComponentCategory.DoorContact);
            otherContact = contact.Clone();
            otherContact.Id = Ids.New();
            otherContact.Name = "Magnet contact, double door";
            p.ComponentDefinitions.Add(otherContact);

            var lockDef = TestData.Component(p, ComponentCategory.ElectricLock);
            lockDef.Modelling = ComponentModelling.BuiltIntoOtherComponent;
            lockDef.CarrierComponentIds = new System.Collections.Generic.List<string> { contact.Id, otherContact.Id };
            lockDef.CarrierParameterLeft = "Lukk vasakul";
            lockDef.CarrierParameterRight = "Lukk paremal";
            return p;
        }

        private static CalculatedPlacement Lock(PlacementPlan plan) => plan.Placements.Single(x => x.Label == "Lock");

        [Fact]
        public void ASetWithTheSecondCarrierType_BuildsTheLockIntoIt()
        {
            ComponentDefinition contact, other;
            var p = Project(out contact, out other);
            var def = TestData.Ds01(p);
            var magnet = def.Components.Single(c => c.ComponentDefinitionId == contact.Id);
            magnet.ComponentDefinitionId = other.Id;

            var lk = Lock(DoorSetPlacementCalculator.Calculate(TestData.Instance(p, def), def, TestData.StraightDoor(), p));
            Assert.Equal(magnet.Id, lk.CarrierSlotKey);
            Assert.True(lk.IsBuiltIn);
            Assert.DoesNotContain(lk.Issues, i => i.Severity == IssueSeverity.Error);
        }

        [Fact]
        public void WithBothTypesOnADoor_TheChoiceSelectsEither()
        {
            ComponentDefinition contact, other;
            var p = Project(out contact, out other);
            var def = TestData.Ds01(p);
            var first = def.Components.Single(c => c.ComponentDefinitionId == contact.Id);
            var second = new SetComponentRule { ComponentDefinitionId = other.Id, Label = "Magnet hinge", Rule = first.Rule.Clone() };
            second.Rule.Reference = PlacementReference.HingeJamb;
            def.Components.Add(second);

            Assert.Equal(first.Id, Lock(DoorSetPlacementCalculator.Calculate(TestData.Instance(p, def), def, TestData.StraightDoor(), p)).CarrierSlotKey);
            def.Components.Single(c => c.Label == "Lock").Rule.CarrierRuleId = second.Id;
            Assert.Equal(second.Id, Lock(DoorSetPlacementCalculator.Calculate(TestData.Instance(p, def), def, TestData.StraightDoor(), p)).CarrierSlotKey);
        }

        [Fact]
        public void WithNoneOfTheCarriers_TheMessageNamesThemAll()
        {
            ComponentDefinition contact, other;
            var p = Project(out contact, out other);
            var def = TestData.Ds01(p);
            def.Components.RemoveAll(c => c.ComponentDefinitionId == contact.Id);

            var lk = Lock(DoorSetPlacementCalculator.Calculate(TestData.Instance(p, def), def, TestData.StraightDoor(), p));
            Assert.Contains(lk.Issues, i => i.Message.Contains(contact.Name + " or " + other.Name));
        }

        [Fact]
        public void OlderFilesWithOneCarrier_AreReadIntoTheList_AndOnlyTheListIsWritten()
        {
            var read = JsonConvert.DeserializeObject<ComponentDefinition>("{\"Name\":\"Lock\",\"CarrierComponentId\":\"magnet-1\"}");
            Assert.Equal(new[] { "magnet-1" }, read.Carriers);

            var json = JsonConvert.SerializeObject(read);
            Assert.DoesNotContain("\"CarrierComponentId\"", json);
            Assert.Equal(new[] { "magnet-1" }, JsonConvert.DeserializeObject<ComponentDefinition>(json).Carriers);
        }

        [Fact]
        public void CloneHasItsOwnCarrierList()
        {
            var c = new ComponentDefinition { CarrierComponentId = "a" };
            var copy = c.Clone();
            copy.CarrierComponentIds.Add("b");
            Assert.Equal(new[] { "a" }, c.Carriers);
        }
    }
}
