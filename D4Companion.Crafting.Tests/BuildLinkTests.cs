using D4Companion.Crafting;

namespace D4Companion.Crafting.Tests
{
    public class BuildLinkTests
    {
        [TestCase("https://maxroll.gg/d4/planner/ab12cd34", "ab12cd34")]
        [TestCase("https://maxroll.gg/d4/planner/ab12cd34#2", "ab12cd34")]
        [TestCase("  maxroll.gg/d4/planner/ab12cd34?x=1 ", "ab12cd34")]
        public void Maxroll_PlannerLink_GivesId(string link, string id)
        {
            var info = BuildLink.Detect(link);
            Assert.That(info.Site, Is.EqualTo(BuildSite.Maxroll));
            Assert.That(info.Key, Is.EqualTo(id));
            Assert.That(info.IsValid, Is.True);
        }

        [Test]
        public void Maxroll_GuideLink_ExplainsPlannerIsNeeded()
        {
            var info = BuildLink.Detect("https://maxroll.gg/d4/build-guides/blessed-hammer-paladin-guide");
            Assert.That(info.Site, Is.EqualTo(BuildSite.Maxroll));
            Assert.That(info.IsValid, Is.False);
            Assert.That(info.Problem, Does.Contain("planner"));
        }

        [Test]
        public void Mobalytics_BuildLink_IsValid()
        {
            var info = BuildLink.Detect("https://mobalytics.gg/diablo-4/builds/barbarian-hota");
            Assert.That(info.Site, Is.EqualTo(BuildSite.Mobalytics));
            Assert.That(info.IsValid, Is.True);
        }

        [Test]
        public void Mobalytics_ProfileBuildLink_DropsQuery()
        {
            var info = BuildLink.Detect("https://mobalytics.gg/diablo-4/profile/someone/builds/abc-123?ws-ngf5-1=x");
            Assert.That(info.Key, Is.EqualTo("https://mobalytics.gg/diablo-4/profile/someone/builds/abc-123"));
        }

        [Test]
        public void Mobalytics_ProfilePage_IsRejected()
        {
            var info = BuildLink.Detect("https://mobalytics.gg/diablo-4/profile/someone");
            Assert.That(info.IsValid, Is.False);
        }

        [Test]
        public void D4Builds_Link_GivesId()
        {
            var info = BuildLink.Detect("https://d4builds.gg/builds/0f8fad5b-d9cb-469f-a165-70867728950e/?var=1");
            Assert.That(info.Site, Is.EqualTo(BuildSite.D4Builds));
            Assert.That(info.Key, Is.EqualTo("0f8fad5b-d9cb-469f-a165-70867728950e"));
        }

        [Test]
        public void D2Core_Link_GivesFourCharacterCode()
        {
            var info = BuildLink.Detect("https://www.d2core.com/d4/planner?bd=1Xyz&lang=en");
            Assert.That(info.Site, Is.EqualTo(BuildSite.D2Core));
            Assert.That(info.Key, Is.EqualTo("1Xyz"));
        }

        [Test]
        public void InfinityBuilds_Link_KeepsWholeLink()
        {
            var info = BuildLink.Detect("https://infinitybuilds.gg/build/abc");
            Assert.That(info.Site, Is.EqualTo(BuildSite.InfinityBuilds));
            Assert.That(info.Key, Is.EqualTo("https://infinitybuilds.gg/build/abc"));
        }

        [Test]
        public void UnknownLink_ExplainsSupportedSites()
        {
            var info = BuildLink.Detect("https://example.com/build");
            Assert.That(info.Site, Is.EqualTo(BuildSite.None));
            Assert.That(info.Problem, Does.Contain("Maxroll"));
        }

        [Test]
        public void EmptyLink_HasNoProblem()
        {
            var info = BuildLink.Detect("   ");
            Assert.That(info.IsValid, Is.False);
            Assert.That(info.Problem, Is.Empty);
        }
    }
}
