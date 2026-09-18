using System;
using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.Core.Rules;
using Sentinel.Core.Status;
using Xunit;

namespace Sentinel.Core.Tests
{
    public class AssignmentRuleTests
    {
        private static DoorFacts Facts() => DoorFacts.FromSource(new SourceDoorReference
        {
            Mark = "D102",
            SideARoom = "Corridor",
            SideBRoom = "Server Room",
            LastKnownGeometry = TestData.StraightDoor(),
            LastKnownParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Department", "Technical" },
                { "FireRating", "EI60-C" }
            }
        });

        private static AssignmentRule Rule(string def, int priority, params RuleCondition[] conditions) =>
            new AssignmentRule { Name = def, DefinitionId = def, Priority = priority, Conditions = conditions.ToList() };

        [Fact]
        public void SpecExample_DepartmentAndFireRating_SuggestsDs02()
        {
            var rule = Rule("DS-02", 10,
                new RuleCondition { Field = "Department", Operator = RuleOperator.Equals, Value = "technical" },
                new RuleCondition { Field = "FireRating", Operator = RuleOperator.Contains, Value = "EI60" });

            var s = AssignmentRuleEvaluator.Suggest(new[] { rule }, Facts());

            Assert.NotNull(s);
            Assert.Equal("DS-02", s.DefinitionId);
            Assert.Contains("FireRating contains \"EI60\"", s.Explanation);
        }

        [Fact]
        public void Priority_WinsOverListOrder()
        {
            var generic = Rule("DS-01", 100, new RuleCondition { Field = "Mark", Operator = RuleOperator.StartsWith, Value = "D" });
            var specific = Rule("DS-02", 1, new RuleCondition { Field = "SideBRoom", Operator = RuleOperator.Contains, Value = "server" });

            Assert.Equal("DS-02", AssignmentRuleEvaluator.Suggest(new[] { generic, specific }, Facts()).DefinitionId);
        }

        [Theory]
        [InlineData("WidthMm", RuleOperator.GreaterThan, "900", true)]
        [InlineData("WidthMm", RuleOperator.LessThan, "900 mm", false)]
        [InlineData("WidthMm", RuleOperator.Equals, "1000,0", true)]
        [InlineData("Mark", RuleOperator.NotEquals, "D101", true)]
        [InlineData("Mark", RuleOperator.NotContains, "10", false)]
        [InlineData("Unknown", RuleOperator.IsEmpty, "", true)]
        [InlineData("Mark", RuleOperator.IsNotEmpty, "", true)]
        [InlineData("AnyRoom", RuleOperator.Contains, "corridor", true)]
        public void Operators(string field, RuleOperator op, string value, bool expected)
        {
            Assert.Equal(expected, AssignmentRuleEvaluator.Evaluate(new RuleCondition { Field = field, Operator = op, Value = value }, Facts()));
        }

        [Fact]
        public void AnyMode_EmptyRules_AndDisabledRules()
        {
            var any = Rule("DS-01", 1,
                new RuleCondition { Field = "Mark", Operator = RuleOperator.Equals, Value = "nope" },
                new RuleCondition { Field = "Mark", Operator = RuleOperator.Equals, Value = "D102" });
            any.MatchMode = RuleMatchMode.Any;
            Assert.NotNull(AssignmentRuleEvaluator.Suggest(new[] { any }, Facts()));

            var empty = Rule("DS-01", 1);
            Assert.Null(AssignmentRuleEvaluator.Suggest(new[] { empty }, Facts()));

            any.Enabled = false;
            Assert.Null(AssignmentRuleEvaluator.Suggest(new[] { any }, Facts()));
        }
    }

    public class GeometryTests
    {
        [Fact]
        public void Footprint_OfRotatedDoorLeaf_RecoversWidthThicknessAndAxes()
        {
            // 1000 x 100 rectangle rotated by 30° around (500, 500), plus interior noise points.
            var angle = 30.0;
            var pts = new List<Vec3>();
            foreach (var x in new[] { -500.0, 0, 500 })
                foreach (var y in new[] { -50.0, 50 })
                    pts.Add(new Vec3(x, y, 0).RotateAboutZ(angle) + new Vec3(500, 500, 0));
            pts.Add(new Vec3(500, 500, 0));

            var r = FootprintAnalyzer.MinimumAreaRectangle(pts);

            Assert.NotNull(r);
            Assert.Equal(1000, r.LongLength, 3);
            Assert.Equal(100, r.ShortLength, 3);
            Assert.Equal(500, r.CenterX, 3);
            Assert.Equal(500, r.CenterY, 3);
            var expectedLong = Vec3.UnitX.RotateAboutZ(angle);
            Assert.True(Math.Abs(Math.Abs(r.LongAxis.Dot(expectedLong)) - 1) < 1e-6);
            Assert.True(Math.Abs(r.LongAxis.Dot(r.ShortAxis)) < 1e-9);
        }

        [Fact]
        public void Footprint_IsDeterministicForReversedPointOrder()
        {
            var pts = new List<Vec3> { new Vec3(0, 0, 0), new Vec3(900, 0, 0), new Vec3(900, 120, 0), new Vec3(0, 120, 0) };
            var a = FootprintAnalyzer.MinimumAreaRectangle(pts);
            pts.Reverse();
            var b = FootprintAnalyzer.MinimumAreaRectangle(pts);
            Assert.Equal(a.ShortAxis, b.ShortAxis);
            Assert.Equal(a.LongAxis, b.LongAxis);
        }

        [Fact]
        public void Angles_Difference_WrapsAround()
        {
            Assert.Equal(2, Angles.Difference(359, 1), 9);
            Assert.Equal(180, Angles.Difference(90, 270), 9);
            Assert.Equal(0, Angles.Normalize360(360), 9);
        }
    }

    public class ChangeDetectionTests
    {
        [Fact]
        public void SourceChangeDetector_ReportsMoveRotationSizeAndParameters()
        {
            var stored = TestData.Source();
            stored.LastKnownParameters["FireRating"] = "EI30";
            var current = TestData.StraightDoor();
            current.Origin = new Vec3(150, 0, 0);
            current.Facing = -Vec3.UnitY;
            current.WidthAxis = -Vec3.UnitX;
            current.WidthMm = 1200;
            var now = new Dictionary<string, string> { { "FireRating", "EI60" } };

            var cmp = SourceChangeDetector.Compare(stored, current, now, new SentinelSettings());

            Assert.True(cmp.Moved && cmp.Rotated && cmp.Resized && cmp.ParametersChanged);
            Assert.Contains("Door moved 150 mm", cmp.Details);
            Assert.Contains(cmp.Details, d => d.Contains("facing flipped"));
            Assert.Contains("Width 1000 → 1200 mm", cmp.Details);
            Assert.Contains("FireRating: \"EI30\" → \"EI60\"", cmp.Details);
        }

        [Fact]
        public void SourceChangeDetector_IgnoresChangesWithinTolerance()
        {
            var stored = TestData.Source();
            var current = TestData.StraightDoor();
            current.Origin = new Vec3(5, 5, 0);
            Assert.False(SourceChangeDetector.Compare(stored, current, null, new SentinelSettings()).HasChanges);
        }

        [Fact]
        public void DriftDetector_UsesToleranceFromSettings()
        {
            var c = new PlacedComponentInstance { PlacedPosition = new Vec3(0, 0, 0), PlacedRotationDeg = 90 };
            string detail;
            Assert.False(ComponentDriftDetector.IsDrifted(c, new Vec3(5, 0, 0), 90.5, new SentinelSettings(), out detail));
            Assert.True(ComponentDriftDetector.IsDrifted(c, new Vec3(250, 0, 0), 90, new SentinelSettings(), out detail));
            Assert.Equal("moved 250 mm", detail);
            Assert.True(ComponentDriftDetector.IsDrifted(c, new Vec3(0, 0, 0), 180, new SentinelSettings(), out detail));
        }
    }

    public class DiffAndMatchingTests
    {
        private static (SentinelProject p, DoorSetInstance inst, PlacementPlan plan) Placed()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var def = TestData.Ds02(p);
            var inst = TestData.Instance(p, def);
            var plan = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p);
            inst.Components = plan.Placements.Select(pl => new PlacedComponentInstance
            {
                SlotKey = pl.SlotKey,
                ComponentDefinitionId = pl.ComponentDefinitionId,
                Label = pl.Label,
                ElementUniqueId = "e-" + pl.SlotKey,
                State = ComponentState.Placed,
                CalculatedPosition = pl.Position,
                CalculatedRotationDeg = pl.InstanceRotationDeg,
                PlacedPosition = pl.Position,
                PlacedFamilyName = pl.FamilyName,
                PlacedTypeName = pl.TypeName
            }).ToList();
            return (p, inst, plan);
        }

        [Fact]
        public void Diff_UnchangedSet_HasNoWork()
        {
            var (p, inst, plan) = Placed();
            var diff = PlacementDiff.Compute(inst, plan, false);
            Assert.All(diff, d => Assert.Equal(DiffAction.Unchanged, d.Action));
            Assert.Equal("No changes.", PlacementDiff.Summarize(diff));
        }

        [Fact]
        public void Diff_AfterFlipAddAndRemove()
        {
            var (p, inst, _) = Placed();
            var def = TestData.Ds02(p);
            DoorSetInstanceOperations.Flip(inst);
            DoorSetInstanceOperations.AddComponent(inst, TestData.Component(p, ComponentCategory.EmergencyRelease));
            DoorSetInstanceOperations.RemoveComponent(inst, def.Components.First(c => c.Label == "Lock").Id);
            inst.Components.Single(c => c.Label == "Door Contact").State = ComponentState.Missing;

            var plan = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p);
            var diff = PlacementDiff.Compute(inst, plan, false);

            Assert.Equal(DiffAction.Move, diff.Single(d => d.Label == "Reader").Action);
            Assert.Equal(DiffAction.Move, diff.Single(d => d.Label == "REX").Action);
            Assert.Equal(DiffAction.Create, diff.Single(d => d.Label == "Door Contact").Action);
            Assert.Equal(DiffAction.Create, diff.Single(d => d.Label == "Emergency Release").Action);
            Assert.Equal(DiffAction.Remove, diff.Single(d => d.Label == "Lock").Action);
        }

        [Fact]
        public void Diff_KeepsManualChangesUnlessOverwriteIsRequested()
        {
            var (p, inst, _) = Placed();
            var def = TestData.Ds02(p);
            inst.Components.Single(c => c.Label == "Reader").State = ComponentState.ManuallyModified;
            DoorSetInstanceOperations.Flip(inst);
            var plan = DoorSetPlacementCalculator.Calculate(inst, def, TestData.StraightDoor(), p);

            Assert.Equal(DiffAction.KeepManual, PlacementDiff.Compute(inst, plan, false).Single(d => d.Label == "Reader").Action);
            Assert.Equal(DiffAction.Move, PlacementDiff.Compute(inst, plan, true).Single(d => d.Label == "Reader").Action);
        }

        [Fact]
        public void Diff_RemappedFamily_IsReplace()
        {
            var (p, inst, _) = Placed();
            TestData.Component(p, ComponentCategory.CardReader).TypeName = "Other";
            var plan = DoorSetPlacementCalculator.Calculate(inst, TestData.Ds02(p), TestData.StraightDoor(), p);
            Assert.Equal(DiffAction.Replace, PlacementDiff.Compute(inst, plan, false).Single(d => d.Label == "Reader").Action);
        }

        [Fact]
        public void Matcher_UsesUniqueIdThenIfcGlobalId()
        {
            var p = TestData.ProjectWithMappedFamilies();
            var a = TestData.Instance(p, TestData.Ds02(p));
            a.Source = TestData.Source("door-A", "link-1", "GUID-A");
            var b = TestData.Instance(p, TestData.Ds02(p));
            b.Source = TestData.Source("door-B-old", "link-1", "GUID-B");
            var c = TestData.Instance(p, TestData.Ds02(p));
            c.Source = TestData.Source("door-C", "link-1", "GUID-C");

            var doors = new List<DiscoveredDoor>
            {
                new DiscoveredDoor { Current = TestData.Source("door-A", "link-1", "GUID-A") },
                new DiscoveredDoor { Current = TestData.Source("door-B-new", "link-1", "GUID-B") }, // UniqueId changed after IFC reload
                new DiscoveredDoor { Current = TestData.Source("door-D", "link-1", "GUID-D") }
            };

            var m = DoorMatcher.Match(doors, new[] { a, b, c });

            Assert.Same(a, m.Single(x => x.Door?.Key == doors[0].Key).Instance);
            var bMatch = m.Single(x => x.Door?.Key == doors[1].Key);
            Assert.Same(b, bMatch.Instance);
            Assert.True(bMatch.MatchedByIfcGlobalId);
            Assert.Null(m.Single(x => x.Door?.Key == doors[2].Key).Instance);
            Assert.Contains(m, x => x.Door == null && x.Instance == c); // outside scope / missing
        }
    }
}
