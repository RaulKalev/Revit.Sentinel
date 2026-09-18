using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.Core.Status;
using Xunit;

namespace Sentinel.Core.Tests
{
    public class StatusEvaluatorTests
    {
        /// <summary>Simulates a successful placement of every planned component.</summary>
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
                PlacedRotationDeg = pl.InstanceRotationDeg,
                PlacedFamilyName = pl.FamilyName,
                PlacedTypeName = pl.TypeName
            }).ToList();
            inst.PlacedConfigurationHash = plan.ConfigurationHash;
        }

        private static SetStatusResult Eval(SentinelProject p, DoorSetInstance inst, SourceState src = SourceState.Ok,
            SourceComparison cmp = null, bool preview = false)
        {
            var def = p.FindDoorSet(inst?.DefinitionId);
            var plan = inst != null ? DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p) : null;
            return DoorSetStatusEvaluator.Evaluate(new DoorSetStatusContext
            {
                Instance = inst,
                Definition = def,
                Plan = plan,
                SourceState = src,
                SourceComparison = cmp,
                IsPreviewing = preview
            });
        }

        private static (SentinelProject, DoorSetInstance) PlacedSet()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var inst = TestData.Instance(p, def);
            MarkPlaced(inst, DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p));
            return (p, inst);
        }

        [Fact]
        public void NoInstance_IsUnassigned() =>
            Assert.Equal(SetStatus.Unassigned, Eval(TestData.ProjectWithMappedFamilies(), null).Status);

        [Fact]
        public void AssignedNotPlaced_IsReady()
        {
            var p = TestData.ProjectWithMappedFamilies();
            Assert.Equal(SetStatus.Ready, Eval(p, TestData.Instance(p, TestData.Ds02(p))).Status);
        }

        [Fact]
        public void UnconfiguredFamily_BeforePlacement_IsErrorWithReason()
        {
            var p = TestData.ProjectWithMappedFamilies();
            TestData.Component(p, ComponentCategory.CardReader).FamilyName = "";
            var r = Eval(p, TestData.Instance(p, TestData.Ds02(p)));
            Assert.Equal(SetStatus.Error, r.Status);
            Assert.Contains(r.Issues, i => i.Message == "Card Reader family not configured (Components page).");
        }

        [Fact]
        public void AllPlaced_IsPlaced()
        {
            var (p, inst) = PlacedSet();
            var r = Eval(p, inst);
            Assert.Equal(SetStatus.Placed, r.Status);
            Assert.DoesNotContain(r.Issues, i => i.Severity == IssueSeverity.Error);
        }

        [Fact]
        public void DeletedReader_IsMissingComponent_AndStaysRemembered()
        {
            var (p, inst) = PlacedSet();
            var reader = inst.Components.Single(c => c.Label == "Reader");
            reader.State = ComponentState.Missing;

            var r = Eval(p, inst);

            Assert.Equal(SetStatus.MissingComponent, r.Status);
            Assert.Contains(r.Issues, i => i.Code == IssueCodes.ComponentMissing && i.Message.StartsWith("Reader missing"));
            Assert.Equal(4, inst.Components.Count); // the set still knows the reader should exist
        }

        [Fact]
        public void FlipAfterPlacement_IsModified()
        {
            var (p, inst) = PlacedSet();
            DoorSetInstanceOperations.Flip(inst);
            var r = Eval(p, inst);
            Assert.Equal(SetStatus.Modified, r.Status);
            Assert.Contains(r.Issues, i => i.Code == IssueCodes.ConfigurationChanged);
        }

        [Fact]
        public void AddedComponentAfterPlacement_IsModified()
        {
            var (p, inst) = PlacedSet();
            DoorSetInstanceOperations.AddComponent(inst, TestData.Component(p, ComponentCategory.EmergencyRelease));
            var r = Eval(p, inst);
            Assert.Equal(SetStatus.Modified, r.Status);
            Assert.Contains(r.Issues, i => i.Message.Contains("1 component(s) to add"));
        }

        [Fact]
        public void ManuallyMovedComponent_IsModified_UntilAccepted()
        {
            var (p, inst) = PlacedSet();
            var reader = inst.Components.Single(c => c.Label == "Reader");
            reader.State = ComponentState.ManuallyModified;
            reader.ActualPosition = new Vec3(900, 100, 1000);
            Assert.Equal(SetStatus.Modified, Eval(p, inst).Status);

            DoorSetInstanceOperations.AcceptManualPosition(reader);
            Assert.Equal(SetStatus.Placed, Eval(p, inst).Status);
        }

        [Fact]
        public void SourceChanges_AreReported()
        {
            var (p, inst) = PlacedSet();
            var cmp = new SourceComparison { Moved = true, Details = new List<string> { "Door moved 150 mm" } };
            var r = Eval(p, inst, SourceState.Changed, cmp);
            Assert.Equal(SetStatus.SourceChanged, r.Status);
            Assert.Contains(r.Issues, i => i.Message.Contains("Door moved 150 mm"));
        }

        [Fact]
        public void MissingSourceDoor_IsOrphaned()
        {
            var (p, inst) = PlacedSet();
            var r = Eval(p, inst, SourceState.Missing);
            Assert.Equal(SetStatus.Orphaned, r.Status);
            Assert.Contains(r.Issues, i => i.Message == "Source door no longer available in the linked model.");
        }

        [Fact]
        public void UnloadedLink_IsErrorWithReason()
        {
            var (p, inst) = PlacedSet();
            var r = Eval(p, inst, SourceState.LinkUnavailable);
            Assert.Equal(SetStatus.Error, r.Status);
            Assert.Contains(r.Issues, i => i.Code == IssueCodes.LinkUnavailable);
        }

        [Fact]
        public void Ignored_And_Preview()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var inst = TestData.Instance(p, TestData.Ds02(p));
            Assert.Equal(SetStatus.Preview, Eval(p, inst, preview: true).Status);
            inst.IsIgnored = true;
            Assert.Equal(SetStatus.Ignored, Eval(p, inst).Status);
        }

        [Fact]
        public void FailedComponent_IsErrorWithReason()
        {
            var (p, inst) = PlacedSet();
            var reader = inst.Components.Single(c => c.Label == "Reader");
            reader.State = ComponentState.Failed;
            reader.ElementUniqueId = null;
            reader.LastError = "Wall/reference could not be determined.";
            var r = Eval(p, inst);
            Assert.Equal(SetStatus.Error, r.Status);
            Assert.Contains(r.Issues, i => i.Message == "Reader: Wall/reference could not be determined.");
        }
    }
}
