using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Persistence;
using Sentinel.Core.Placement;
using Sentinel.Core.Rules;
using Sentinel.Core.Status;
using Xunit;

namespace Sentinel.Core.Tests
{
    public class NoAccessControlTests
    {
        private static SetStatus Status(DoorSetInstance inst) =>
            DoorSetStatusEvaluator.Evaluate(new DoorSetStatusContext { Instance = inst, SourceState = SourceState.Ok }).Status;

        [Fact]
        public void MarkedDoor_HasItsOwnStatus_AndNoSet()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));
            inst.Overrides.RemovedRuleIds.Add("x");

            DoorSetInstanceOperations.MarkNoAccessControl(inst);

            Assert.Null(inst.DefinitionId);
            Assert.True(inst.IsNoAccessControl);
            Assert.Empty(inst.Overrides.RemovedRuleIds);
            Assert.Equal(SetStatus.NoAccessControl, Status(inst));
            Assert.Equal("No access control", DoorSetStatusEvaluator.StatusText(SetStatus.NoAccessControl));
        }

        [Fact]
        public void AssigningASet_OrIgnoring_ReplacesTheMark()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));
            DoorSetInstanceOperations.MarkNoAccessControl(inst);

            DoorSetInstanceOperations.AssignDefinition(inst, TestData.Ds01(p));
            Assert.False(inst.NoAccessControl);
            Assert.NotEqual(SetStatus.NoAccessControl, Status(inst));

            DoorSetInstanceOperations.MarkNoAccessControl(inst);
            inst.IsIgnored = true; // an ignored door is not a decision about access control
            Assert.Equal(SetStatus.Ignored, Status(inst));
        }

        [Fact]
        public void PlacedComponents_AreKeptUntilDeleted_AndFlagged()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));
            inst.Components.Add(new PlacedComponentInstance { SlotKey = "r", ElementUniqueId = "elem-1", State = ComponentState.Placed });

            DoorSetInstanceOperations.MarkNoAccessControl(inst);

            Assert.Single(inst.Components); // the records still point at the elements in the model
            var r = DoorSetStatusEvaluator.Evaluate(new DoorSetStatusContext { Instance = inst });
            Assert.Equal(SetStatus.NoAccessControl, r.Status);
            Assert.Contains(r.Issues, i => i.Code == IssueCodes.NoAccessControlHasComponents);
        }

        [Fact]
        public void Mark_SurvivesSaveAndLoad()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));
            DoorSetInstanceOperations.MarkNoAccessControl(inst);
            p.DoorSetInstances.Add(inst);

            var back = SentinelProjectSerializer.FromPayload(SentinelProjectSerializer.ToPayload(p, "test", "1.0")).Project;
            Assert.True(back.DoorSetInstances.Single().IsNoAccessControl);
        }

        [Fact]
        public void RuleCanTarget_NoAccessControl()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var rule = new AssignmentRule
            {
                Name = "Toilets",
                Priority = 10,
                DefinitionId = NoAccessControlChoice.Id,
                Conditions = { new RuleCondition { Field = DoorFacts.Mark, Operator = RuleOperator.StartsWith, Value = "D1" } }
            };
            var s = AssignmentRuleEvaluator.Suggest(new[] { rule }, DoorFacts.FromSource(TestData.Source()));

            Assert.NotNull(s);
            Assert.Same(NoAccessControlChoice.Definition, p.FindSetChoice(s.DefinitionId));
            Assert.Null(p.FindDoorSet(s.DefinitionId)); // never a real set type
            Assert.Equal(TestData.Ds02(p), p.FindSetChoice(TestData.Ds02(p).Id));
        }
    }
}
