using Sentinel.Core.Models;
using Sentinel.Core.Persistence;
using Xunit;

namespace Sentinel.Core.Tests
{
    public class ZoomLevelsTests
    {
        [Fact]
        public void DefaultsAre100_AndSurviveSaveAndLoad()
        {
            var p = TestData.ProjectWithMappedFamilies();
            Assert.Equal(100, p.Settings.PlanZoomPercent);
            Assert.Equal(100, p.Settings.View3DZoomPercent);
            p.Settings.PlanZoomPercent = 200;
            p.Settings.View3DZoomPercent = 50;
            var back = SentinelProjectSerializer.FromPayload(SentinelProjectSerializer.ToPayload(p, "test", "1.0")).Project;
            Assert.Equal(200, back.Settings.PlanZoomPercent);
            Assert.Equal(50, back.Settings.View3DZoomPercent);
        }

        [Fact]
        public void Steps_MoveThroughTheList_AndStopAtTheEnds()
        {
            Assert.Equal(125, ZoomLevels.Step(100, +1));
            Assert.Equal(75, ZoomLevels.Step(100, -1));
            Assert.Equal(400, ZoomLevels.Step(400, +1));
            Assert.Equal(25, ZoomLevels.Step(25, -1));
            Assert.Equal(150, ZoomLevels.Step(130, +1)); // an odd stored value snaps to the next step
        }

        [Fact]
        public void AreaFactor_IsTheInverseZoom_AndBadValuesFallBackTo100()
        {
            Assert.Equal(1, ZoomLevels.AreaFactor(100), 6);
            Assert.Equal(0.5, ZoomLevels.AreaFactor(200), 6);
            Assert.Equal(2, ZoomLevels.AreaFactor(50), 6);
            Assert.Equal(1, ZoomLevels.AreaFactor(0), 6);   // missing in older data
            Assert.Equal(0.25, ZoomLevels.AreaFactor(1000), 6); // clamped to 400 %
        }
    }
}
