using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Persistence;
using Xunit;

namespace Sentinel.Core.Tests
{
    public class PlanKeywordsTests
    {
        private static readonly string[] Plans = { "1. korrus", "1. korrus - EL", "1. korrus - Security", "EL 1. korrus nõrkvool" };

        [Fact]
        public void NoKeywords_KeepsAllPlans()
        {
            Assert.Equal(Plans, PlanKeywords.Preferred(Plans, p => p, PlanKeywords.Parse("")));
            Assert.Equal(Plans, PlanKeywords.Preferred(Plans, p => p, PlanKeywords.Parse(" , ;")));
        }

        [Fact]
        public void PlansWithTheKeyword_AreChosenFirst_IgnoringCase()
        {
            Assert.Equal(new[] { "1. korrus - Security" }, PlanKeywords.Preferred(Plans, p => p, PlanKeywords.Parse("security")));
            Assert.Equal(new[] { "1. korrus - EL", "EL 1. korrus nõrkvool" }, PlanKeywords.Preferred(Plans, p => p, PlanKeywords.Parse("EL")));
        }

        [Fact]
        public void EarlierKeywordsWin_AndUnmatchedKeywordsFallBack()
        {
            Assert.Equal(new[] { "1. korrus - Security" }, PlanKeywords.Preferred(Plans, p => p, PlanKeywords.Parse("Security, EL")));
            Assert.Equal(2, PlanKeywords.Preferred(Plans, p => p, PlanKeywords.Parse("Fire; EL")).Count);
            Assert.Equal(Plans, PlanKeywords.Preferred(Plans, p => p, PlanKeywords.Parse("Fire")));   // nothing matches: no preference
        }

        [Fact]
        public void Keywords_SurviveSaveAndLoad()
        {
            var p = TestData.ProjectWithMappedFamilies();
            p.Settings.PlanNameKeywords = "Security, EL";
            var back = SentinelProjectSerializer.FromPayload(SentinelProjectSerializer.ToPayload(p, "test", "1.0")).Project;
            Assert.Equal("Security, EL", back.Settings.PlanNameKeywords);
        }
    }
}
