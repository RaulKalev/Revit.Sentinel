using System.Collections.Generic;
using System.Linq;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Xunit;

namespace Sentinel.Core.Tests
{
    public class MultiLinkDiscoveryTests
    {
        private static DiscoveredDoor Door(string link, string mark, double x, double y, double z = 0) => new DiscoveredDoor
        {
            Current = new SourceDoorReference
            {
                LinkInstanceUniqueId = link,
                LinkName = link + ".ifc",
                DoorUniqueId = link + "-" + mark,
                Mark = mark,
                LastKnownGeometry = new DoorGeometry { Origin = new Vec3(x, y, z), WidthMm = 900, HeightMm = 2100, WallThicknessMm = 150 }
            }
        };

        [Fact]
        public void SameOpeningInTwoLinks_IsMarkedOnBothDoors()
        {
            var ar = Door("AR", "VU-02", 10000, 5000);
            var sa = Door("SA", "SKU13", 10120, 5080);
            var marked = CrossLinkDuplicates.Mark(new List<DiscoveredDoor> { ar, sa });

            Assert.Equal(2, marked);
            Assert.Equal("SKU13 in SA.ifc", ar.PossibleDuplicateOf);
            Assert.Equal("VU-02 in AR.ifc", sa.PossibleDuplicateOf);
        }

        [Fact]
        public void DoorsOfTheSameLink_AreNeverDuplicates()
        {
            var a = Door("AR", "D1", 0, 0);
            var b = Door("AR", "D2", 50, 0); // double door leaves in one model
            Assert.Equal(0, CrossLinkDuplicates.Mark(new List<DiscoveredDoor> { a, b }));
            Assert.Null(a.PossibleDuplicateOf);
        }

        [Fact]
        public void DoorsApartInPlanOrOnAnotherFloor_AreNotDuplicates()
        {
            var ar = Door("AR", "VU-02", 0, 0);
            var farPlan = Door("SA", "SKU1", 400, 0);
            var otherFloor = Door("SA", "SKU2", 0, 0, 3600);
            Assert.Equal(0, CrossLinkDuplicates.Mark(new List<DiscoveredDoor> { ar, farPlan, otherFloor }));
        }

        [Fact]
        public void NearestDoorOfTheOtherLinkIsReported_AcrossGridCells()
        {
            var ar = Door("AR", "VU-02", 299, 299); // near a cell corner
            var near = Door("SA", "SKU-near", 310, 310);
            var further = Door("SA", "SKU-far", 520, 299);
            CrossLinkDuplicates.Mark(new List<DiscoveredDoor> { ar, near, further });
            Assert.Equal("SKU-near in SA.ifc", ar.PossibleDuplicateOf);
        }

        [Fact]
        public void Settings_FallBackToLegacySingleLink_AndWriteBoth()
        {
            var s = new SentinelSettings { DiscoveryLinkUniqueId = "legacy" };
            Assert.Equal(new[] { "legacy" }, s.GetDiscoveryLinks());

            s.SetDiscoveryLinks(new[] { "AR", "SA", "AR", "" });
            Assert.Equal(new[] { "AR", "SA" }, s.GetDiscoveryLinks());
            Assert.Equal("AR", s.DiscoveryLinkUniqueId);
            Assert.Equal(new[] { "AR", "SA" }, s.Clone().GetDiscoveryLinks());
        }

        [Fact]
        public void Settings_MultiLinkSurvivesSaveAndLoad()
        {
            var p = TestData.ProjectWithMappedFamilies();
            p.Settings.SetDiscoveryLinks(new[] { "AR", "SA" });
            p.Settings.DiscoverySkipWindowTypes = true;
            var back = Sentinel.Core.Persistence.SentinelProjectSerializer.FromPayload(
                Sentinel.Core.Persistence.SentinelProjectSerializer.ToPayload(p, "test", "1.0")).Project;
            Assert.Equal(new[] { "AR", "SA" }, back.Settings.GetDiscoveryLinks());
            Assert.True(back.Settings.DiscoverySkipWindowTypes);
        }
    }
}
