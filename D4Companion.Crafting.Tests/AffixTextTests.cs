using D4Companion.Crafting;
using System.Globalization;

namespace D4Companion.Crafting.Tests
{
    public class AffixTextTests
    {
        private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

        [TestCase("+#% Critical Strike Chance", "Critical Strike Chance")]
        [TestCase("# Willpower", "Willpower")]
        [TestCase("+# Maximum Life", "Maximum Life")]
        [TestCase("While Injured, Your Potion Also Grants #% Maximum Life as Barrier", "While Injured, Your Potion Also Grants Maximum Life as Barrier")]
        [TestCase("+[{VALUE}] Maximum Life", "Maximum Life")]
        [TestCase("Rampage: +#% Willpower per Kill Streak Tier", "Rampage: Willpower per Kill Streak Tier")]
        [TestCase("+# to Frenzy", "Ranks to Frenzy")]
        [TestCase("", "")]
        public void ReadableName_RemovesPlaceholders(string description, string expected)
        {
            Assert.That(AffixText.ReadableName(description), Is.EqualTo(expected));
        }

        [Test]
        public void IsPercent_DetectsPercentAffixes()
        {
            Assert.That(AffixText.IsPercent("+#% Critical Strike Chance"), Is.True);
            Assert.That(AffixText.IsPercent("# Willpower"), Is.False);
        }

        [Test]
        public void NameWithValue_FormatsPercentAndFlatValues()
        {
            Assert.That(AffixText.NameWithValue("Critical Strike Chance", 6.3, true, English), Is.EqualTo("Critical Strike Chance · 6.3%"));
            Assert.That(AffixText.NameWithValue("Willpower", 151, false, English), Is.EqualTo("Willpower · 151"));
            Assert.That(AffixText.NameWithValue("Maximum Life", 1813, false, English), Is.EqualTo("Maximum Life · 1,813"));
            Assert.That(AffixText.NameWithValue("Thorns", null, false, English), Is.EqualTo("Thorns"));
        }

        [Test]
        public void PrismNames_AreReadable()
        {
            Assert.That(PrismNames.Name("TuningStone_1"), Is.EqualTo("Aggressive Tuning Prism"));
            Assert.That(PrismNames.Name("TuningStone_6"), Is.EqualTo("Adept's Tuning Prism"));
            Assert.That(PrismNames.Name("TuningStone_9"), Is.EqualTo("Tuning Prism 9"));
        }
    }
}
