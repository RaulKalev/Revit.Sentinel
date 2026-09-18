using System.Linq;
using Newtonsoft.Json.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Library;
using Sentinel.Core.Models;
using Sentinel.Core.Persistence;
using Sentinel.Core.Placement;
using Sentinel.Core.Rules;
using Xunit;

namespace Sentinel.Core.Tests
{
    public class PersistenceTests
    {
        private static SentinelProject RichProject()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var inst = TestData.Instance(p, def);
            inst.AccessDirection = AccessDirection.SideBToSideA;
            inst.HingeSideOverride = HingeSide.PositiveWidthAxis;
            inst.Source.LastKnownParameters["FireRating"] = "EI60";
            inst.Source.SideARoom = "Corridor";
            inst.Source.SideBRoom = "Server Room";
            DoorSetInstanceOperations.AddComponent(inst, TestData.Component(p, ComponentCategory.EmergencyRelease));
            DoorSetInstanceOperations.OverrideRule(inst, new ComponentRuleOverride
            {
                RuleId = def.Components[0].Id,
                AlongWallOffsetMm = 350
            });
            inst.Components.Add(new PlacedComponentInstance
            {
                SlotKey = def.Components[0].Id,
                Label = "Reader",
                ElementUniqueId = "abc-123",
                State = ComponentState.Placed,
                CalculatedPosition = new Vec3(1, 2, 3),
                PlacedPosition = new Vec3(1, 2, 3.5)
            });
            p.DoorSetInstances.Add(inst);
            p.AssignmentRules.Add(new AssignmentRule
            {
                Name = "Technical doors",
                DefinitionId = def.Id,
                Conditions = { new RuleCondition { Field = "FireRating", Operator = RuleOperator.Contains, Value = "EI60" } }
            });
            p.Settings.CapturedParameterNames.Add("SecurityClass");
            return p;
        }

        [Fact]
        public void RoundTrip_PreservesEverything()
        {
            var p = RichProject();

            var payload = SentinelProjectSerializer.ToPayload(p, "tester", "1.0.0");
            var loaded = SentinelProjectSerializer.FromPayload(payload);

            Assert.Empty(loaded.Warnings);
            Assert.False(loaded.IsNewerThanSupported);
            var q = loaded.Project;
            Assert.Equal(p.ProjectId, q.ProjectId);
            Assert.Equal(p.ComponentDefinitions.Count, q.ComponentDefinitions.Count);
            Assert.Equal(p.DoorSetDefinitions.Count, q.DoorSetDefinitions.Count);
            Assert.Single(q.AssignmentRules);

            var a = p.DoorSetInstances[0];
            var b = q.DoorSetInstances[0];
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(AccessDirection.SideBToSideA, b.AccessDirection);
            Assert.Equal(HingeSide.PositiveWidthAxis, b.HingeSideOverride);
            Assert.Equal("EI60", b.Source.LastKnownParameters["firerating"]); // case-insensitive after load
            Assert.Equal("Server Room", b.Source.SideBRoom);
            Assert.Equal(a.Source.IfcGlobalId, b.Source.IfcGlobalId);
            Assert.Equal(a.Source.LastKnownGeometry.Origin, b.Source.LastKnownGeometry.Origin);
            Assert.Single(b.Overrides.AddedComponents);
            Assert.Equal(350, b.Overrides.RuleOverrides.Single().AlongWallOffsetMm);
            Assert.Null(b.Overrides.RuleOverrides.Single().MountingHeightMm);
            var c = b.Components.Single();
            Assert.Equal("abc-123", c.ElementUniqueId);
            Assert.Equal(new Vec3(1, 2, 3.5), c.PlacedPosition.Value);
            Assert.Null(c.ActualPosition);
        }

        [Fact]
        public void Settings_DefaultListsAreNotDuplicatedOnLoad()
        {
            var p = RichProject();
            var count = p.Settings.CapturedParameterNames.Count;

            var q = SentinelProjectSerializer.FromPayload(SentinelProjectSerializer.ToPayload(p, null, null)).Project;

            Assert.Equal(count, q.Settings.CapturedParameterNames.Count);
        }

        [Fact]
        public void Enums_AreStoredByName()
        {
            var payload = SentinelProjectSerializer.ToPayload(RichProject(), null, null);
            var inst = payload.Items.Select(JObject.Parse).First(o => (string)o["Type"] == SentinelProjectSerializer.TypeDoorSetInstance);
            Assert.Equal("SideBToSideA", (string)inst["Data"]["AccessDirection"]);
            Assert.Equal(SentinelProjectSerializer.CurrentDataVersion, payload.DataVersion);
            Assert.Equal(1, payload.DataVersion);
        }

        [Fact]
        public void CorruptAndUnknownItems_ArePreservedVerbatim()
        {
            var payload = SentinelProjectSerializer.ToPayload(RichProject(), null, null);
            const string corrupt = "{\"Type\":\"DoorSetInstance\",\"Id\":\"x\",\"Data\":{\"AccessDirection\":\"Sideways\"}}";
            const string future = "{\"Type\":\"RoomSetInstance\",\"Id\":\"r1\",\"Data\":{\"Room\":\"101\"}}";
            payload.Items.Add(corrupt);
            payload.Items.Add(future);

            var loaded = SentinelProjectSerializer.FromPayload(payload);

            Assert.Equal(2, loaded.Warnings.Count);
            Assert.Single(loaded.Project.DoorSetInstances); // the valid one still loads
            Assert.Contains(corrupt, loaded.Project.PreservedRawItems);
            Assert.Contains(future, loaded.Project.PreservedRawItems);

            var resaved = SentinelProjectSerializer.ToPayload(loaded.Project, null, null);
            Assert.Contains(corrupt, resaved.Items);
            Assert.Contains(future, resaved.Items);
        }

        [Fact]
        public void NewerDataVersion_IsFlaggedReadOnly()
        {
            var payload = SentinelProjectSerializer.ToPayload(RichProject(), null, null);
            payload.DataVersion = SentinelProjectSerializer.CurrentDataVersion + 1;

            var loaded = SentinelProjectSerializer.FromPayload(payload);

            Assert.True(loaded.IsNewerThanSupported);
            Assert.Contains(loaded.Warnings, w => w.Contains("read-only"));
        }

        [Fact]
        public void CorruptHeader_ResetsSettingsButKeepsItems()
        {
            var payload = SentinelProjectSerializer.ToPayload(RichProject(), null, null);
            payload.Header = "{not json";

            var loaded = SentinelProjectSerializer.FromPayload(payload);

            Assert.Contains(loaded.Warnings, w => w.Contains("settings"));
            Assert.Single(loaded.Project.DoorSetInstances);
            Assert.NotNull(loaded.Project.Settings);
        }

        [Fact]
        public void LibraryFile_RoundTripsAndMerges()
        {
            var source = RichProject();
            var json = LibraryFile.FromProject(source, "me").ToJson();

            var target = new SentinelProject();
            var summary = LibraryFile.Parse(json).MergeInto(target);

            Assert.Equal(source.ComponentDefinitions.Count, target.ComponentDefinitions.Count);
            Assert.Equal(source.DoorSetDefinitions.Count, target.DoorSetDefinitions.Count);
            Assert.Single(target.AssignmentRules);
            Assert.Empty(target.DoorSetInstances); // instances are project data, never exported
            Assert.Contains("added", summary);

            var again = LibraryFile.Parse(json).MergeInto(target);
            Assert.Equal(source.ComponentDefinitions.Count, target.ComponentDefinitions.Count);
            Assert.Contains("updated", again);
        }

        [Fact]
        public void DefaultLibrary_HasExampleSetsWithoutFamilyMappings()
        {
            var p = DefaultLibrary.CreateProject();
            Assert.Contains(p.DoorSetDefinitions, d => d.Code == "DS-01" && d.Components.Count == 3);
            Assert.Contains(p.DoorSetDefinitions, d => d.Code == "DS-02" && d.Components.Count == 4);
            Assert.All(p.ComponentDefinitions, c => Assert.False(c.IsFamilyConfigured));
            Assert.Equal("DS-03", p.NextDoorSetCode());
        }
    }
}
