using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.Core.Status;
using Xunit;

namespace Sentinel.Core.Tests
{
    /// <summary>A lock built into the magnet contact shares the contact's element and follows it.</summary>
    public class BuiltInFollowsCarrierTests
    {
        private static DoorSetInstance MagnetWithLock(out PlacedComponentInstance magnet, out PlacedComponentInstance lck)
        {
            var inst = new DoorSetInstance { DefinitionId = "ds" };
            magnet = new PlacedComponentInstance
            {
                SlotKey = "contact", Label = "Door Contact", ElementUniqueId = "elem-1", PlacedHosting = "Unhosted",
                State = ComponentState.Placed, PlacedPosition = new Vec3(0, 0, 2150), ActualPosition = new Vec3(0, 0, 2150)
            };
            lck = new PlacedComponentInstance
            {
                SlotKey = "lock", Label = "Lock", ElementUniqueId = "elem-1", PlacedHosting = BuiltInHosting.For("Lukk vasakul"),
                State = ComponentState.Placed, PlacedPosition = new Vec3(0, 0, 2150), ActualPosition = new Vec3(0, 0, 2150)
            };
            inst.Components.Add(magnet);
            inst.Components.Add(lck);
            return inst;
        }

        [Fact]
        public void MovingTheMagnet_MovesTheLock_AndOnlyTheMagnetIsReported()
        {
            PlacedComponentInstance magnet, lck;
            var inst = MagnetWithLock(out magnet, out lck);
            magnet.ActualPosition = new Vec3(120, 0, 2150);
            magnet.State = ComponentState.ManuallyModified;

            Assert.Equal(1, DoorSetInstanceOperations.FollowCarriers(inst));
            Assert.Equal(ComponentState.ManuallyModified, lck.State);
            Assert.Equal(magnet.ActualPosition, lck.ActualPosition);

            var r = DoorSetStatusEvaluator.Evaluate(new DoorSetStatusContext { Instance = inst, Definition = new DoorSetDefinition { Id = "ds" } });
            Assert.Equal(SetStatus.Modified, r.Status);
            Assert.Single(r.Issues, i => i.Code == IssueCodes.ManuallyModified);
            Assert.Contains("Door Contact", r.Issues.First(i => i.Code == IssueCodes.ManuallyModified).Message);
        }

        [Fact]
        public void AcceptingTheMagnet_AcceptsTheLock()
        {
            PlacedComponentInstance magnet, lck;
            var inst = MagnetWithLock(out magnet, out lck);
            magnet.ActualPosition = new Vec3(120, 0, 2150);
            magnet.State = ComponentState.ManuallyModified;
            DoorSetInstanceOperations.FollowCarriers(inst);

            DoorSetInstanceOperations.AcceptManualPosition(magnet);
            DoorSetInstanceOperations.FollowCarriers(inst);

            Assert.Equal(ComponentState.Placed, lck.State);
            Assert.True(lck.ManualPositionAccepted);
            string detail;
            Assert.False(ComponentDriftDetector.IsDrifted(lck, new Vec3(120, 0, 2150), 0, new SentinelSettings(), out detail));
            var r = DoorSetStatusEvaluator.Evaluate(new DoorSetStatusContext { Instance = inst, Definition = new DoorSetDefinition { Id = "ds" } });
            Assert.Equal(SetStatus.Placed, r.Status);
        }

        [Fact]
        public void DeletingTheMagnet_MakesTheLockMissingToo_AndOwnFamilyLocksAreLeftAlone()
        {
            PlacedComponentInstance magnet, lck;
            var inst = MagnetWithLock(out magnet, out lck);
            var ownLock = new PlacedComponentInstance
            {
                SlotKey = "lock2", ElementUniqueId = "elem-2", PlacedHosting = "Unhosted", State = ComponentState.ManuallyModified
            };
            inst.Components.Add(ownLock);
            magnet.State = ComponentState.Missing;

            DoorSetInstanceOperations.FollowCarriers(inst);
            Assert.Equal(ComponentState.Missing, lck.State);
            Assert.Equal(ComponentState.ManuallyModified, ownLock.State);
        }
    }
}
