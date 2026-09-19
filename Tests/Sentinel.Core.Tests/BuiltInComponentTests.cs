using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Xunit;

namespace Sentinel.Core.Tests
{
    /// <summary>A lock modelled inside the magnet contact family, switched on by "Lukk vasakul" / "Lukk paremal".</summary>
    public class BuiltInComponentTests
    {
        private static SentinelProject ProjectWithBuiltInLock(bool swap = false)
        {
            var p = TestData.ProjectWithMappedFamilies();
            var contact = TestData.Component(p, ComponentCategory.DoorContact);
            var lockDef = TestData.Component(p, ComponentCategory.ElectricLock);
            lockDef.Modelling = ComponentModelling.BuiltIntoOtherComponent;
            lockDef.CarrierComponentId = contact.Id;
            lockDef.CarrierParameterLeft = "Lukk vasakul";
            lockDef.CarrierParameterRight = "Lukk paremal";
            lockDef.SwapCarrierSides = swap;
            return p;
        }

        private static CalculatedPlacement Lock(PlacementPlan plan) => plan.Placements.Single(x => x.Label == "Lock");

        private static PlacementPlan Plan(SentinelProject p, HingeSide hinge = HingeSide.NegativeWidthAxis)
        {
            var def = TestData.Ds01(p);
            return DoorSetPlacementCalculator.Calculate(TestData.Instance(p, def), def, TestData.StraightDoor(hinge), p);
        }

        [Fact]
        public void LockIsBoundToTheContact_AndSwitchesTheRightHandParameter()
        {
            // Latch at +X; the contact is on side B facing -Y, so +X is on the right seen from its front.
            var plan = Plan(ProjectWithBuiltInLock());
            var lk = Lock(plan);
            var contact = plan.Placements.Single(x => x.Label == "Door Contact");

            Assert.True(lk.IsBuiltIn);
            Assert.Equal(contact.SlotKey, lk.CarrierSlotKey);
            Assert.Equal("Lukk paremal", lk.CarrierParameter);
            Assert.Equal("Lukk vasakul", lk.CarrierOtherParameter);
            Assert.Null(lk.FamilyName);
            Assert.False(plan.HasBlockingErrors);
            Assert.Contains("built into Door Contact", lk.Explanation);
        }

        [Fact]
        public void SwappedHinge_PutsTheLockOnTheLeft()
        {
            var lk = Lock(Plan(ProjectWithBuiltInLock(), HingeSide.PositiveWidthAxis));
            Assert.Equal("Lukk vasakul", lk.CarrierParameter);
        }

        [Fact]
        public void SwapOption_InvertsLeftAndRight()
        {
            var lk = Lock(Plan(ProjectWithBuiltInLock(swap: true)));
            Assert.Equal("Lukk vasakul", lk.CarrierParameter);
        }

        [Fact]
        public void WithoutAContactInTheSet_TheOwnFamilyIsTheBackup()
        {
            var p = ProjectWithBuiltInLock();
            var ds01 = TestData.Ds01(p);
            ds01.Components.RemoveAll(c => c.Label == "Door Contact");
            var lk = Lock(Plan(p));

            Assert.False(lk.IsBuiltIn);
            Assert.Equal("Sec_ElectricLock", lk.FamilyName);
            Assert.Contains(lk.Issues, i => i.Code == IssueCodes.BuiltInFallback && i.Severity == IssueSeverity.Info);
            Assert.False(lk.HasErrors);
        }

        [Fact]
        public void WithoutAContactAndWithoutABackupFamily_ItIsAnError()
        {
            var p = ProjectWithBuiltInLock();
            TestData.Ds01(p).Components.RemoveAll(c => c.Label == "Door Contact");
            var lockDef = TestData.Component(p, ComponentCategory.ElectricLock);
            lockDef.FamilyName = null;
            lockDef.TypeName = null;
            var lk = Lock(Plan(p));

            Assert.True(lk.HasErrors);
            Assert.Contains(lk.Issues, i => i.Code == IssueCodes.BuiltInUnavailable);
        }

        [Fact]
        public void BuiltInLock_DoesNotNeedItsOwnFamily()
        {
            var p = ProjectWithBuiltInLock();
            var lockDef = TestData.Component(p, ComponentCategory.ElectricLock);
            lockDef.FamilyName = null;
            lockDef.TypeName = null;
            Assert.True(lockDef.IsModelled);
            Assert.False(Lock(Plan(p)).HasErrors);
        }

        [Fact]
        public void MissingParameterNames_FallBackWithAReason()
        {
            var p = ProjectWithBuiltInLock();
            TestData.Component(p, ComponentCategory.ElectricLock).CarrierParameterRight = " ";
            var lk = Lock(Plan(p));
            Assert.False(lk.IsBuiltIn);
            Assert.Contains(lk.Issues, i => i.Code == IssueCodes.BuiltInFallback && i.Message.Contains("left/right parameters"));
        }

        [Fact]
        public void Diff_SwitchingFromOwnFamilyToBuiltIn_ReplacesTheLock()
        {
            var p = ProjectWithBuiltInLock();
            var inst = TestData.Instance(p, TestData.Ds01(p));
            var plan = Plan(p);
            var lk = Lock(plan);
            inst.Components.Add(new PlacedComponentInstance
            {
                SlotKey = lk.SlotKey, ComponentDefinitionId = lk.ComponentDefinitionId, ElementUniqueId = "old-lock",
                State = ComponentState.Placed, CalculatedPosition = lk.Position, CalculatedRotationDeg = lk.InstanceRotationDeg,
                PlacedFamilyName = "Sec_ElectricLock", PlacedTypeName = "Standard", PlacedHosting = "Unhosted"
            });

            var item = PlacementDiff.Compute(inst, plan, false).Single(d => d.SlotKey == lk.SlotKey);
            Assert.Equal(DiffAction.Replace, item.Action);
        }

        [Fact]
        public void Diff_OtherSideParameter_ReappliesTheBuiltInLock()
        {
            var p = ProjectWithBuiltInLock();
            var inst = TestData.Instance(p, TestData.Ds01(p));
            var plan = Plan(p);
            var lk = Lock(plan);
            inst.Components.Add(new PlacedComponentInstance
            {
                SlotKey = lk.SlotKey, ComponentDefinitionId = lk.ComponentDefinitionId, ElementUniqueId = "contact",
                State = ComponentState.Placed, CalculatedPosition = lk.Position, CalculatedRotationDeg = lk.InstanceRotationDeg,
                PlacedHosting = BuiltInHosting.For("Lukk vasakul")
            });

            var item = PlacementDiff.Compute(inst, plan, false).Single(d => d.SlotKey == lk.SlotKey);
            Assert.Equal(DiffAction.Move, item.Action);

            inst.Components[0].PlacedHosting = BuiltInHosting.For("Lukk paremal");
            Assert.Equal(DiffAction.Unchanged, PlacementDiff.Compute(inst, plan, false).Single(d => d.SlotKey == lk.SlotKey).Action);
        }

        [Fact]
        public void OwnFamilyPlacementHash_IsUnaffectedByTheNewFields()
        {
            // Hash of plans without built-in components must equal the previous formula (placed sets would turn "Modified").
            var plan = Plan(TestData.ProjectWithMappedFamilies());
            Assert.Equal(LegacyHash(plan.Placements), plan.ConfigurationHash);

            var builtIn = Plan(ProjectWithBuiltInLock());
            Assert.NotEqual(LegacyHash(builtIn.Placements), builtIn.ConfigurationHash);
        }

        /// <summary>The configuration hash as computed before built-in components existed.</summary>
        private static string LegacyHash(System.Collections.Generic.IEnumerable<CalculatedPlacement> placements)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            foreach (var p in placements.OrderBy(x => x.SlotKey, System.StringComparer.Ordinal))
            {
                sb.Append(p.SlotKey).Append('|').Append(p.ComponentDefinitionId).Append('|')
                  .Append(p.FamilyName).Append('|').Append(p.TypeName).Append('|').Append(p.HostBehavior).Append('|')
                  .Append(System.Math.Round(p.Position.X).ToString("0", inv)).Append(',')
                  .Append(System.Math.Round(p.Position.Y).ToString("0", inv)).Append(',')
                  .Append(System.Math.Round(p.Position.Z).ToString("0", inv)).Append('|')
                  .Append(System.Math.Round(p.InstanceRotationDeg, 1).ToString("0.0", inv)).Append(';');
            }
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return System.BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16);
        }

        [Fact]
        public void BuiltInSettings_SurviveSaveAndLoad()
        {
            var p = ProjectWithBuiltInLock(swap: true);
            var back = Sentinel.Core.Persistence.SentinelProjectSerializer.FromPayload(
                Sentinel.Core.Persistence.SentinelProjectSerializer.ToPayload(p, "test", "1.0")).Project;
            var lockDef = TestData.Component(back, ComponentCategory.ElectricLock);
            Assert.True(lockDef.IsBuiltIn);
            Assert.Equal("Lukk paremal", lockDef.CarrierParameterRight);
            Assert.True(lockDef.SwapCarrierSides);
            Assert.Equal(TestData.Component(back, ComponentCategory.DoorContact).Id, lockDef.CarrierComponentId);
        }
    }
}
