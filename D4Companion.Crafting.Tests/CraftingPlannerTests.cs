using D4Companion.Crafting;

namespace D4Companion.Crafting.Tests
{
    public class CraftingPlannerTests
    {
        private static ScannedAffix Affix(string id, AffixKind kind = AffixKind.Normal, double? value = 10, params string[] prisms) => new()
        {
            AffixId = id,
            DisplayName = id,
            Kind = kind,
            Value = value,
            TuningPrisms = prisms
        };

        private static TargetAffix Target(string id, bool tempered = false, double? minimum = null, params string[] prisms) => new()
        {
            AffixId = id,
            DisplayName = id,
            ItemType = "ring",
            IsTempered = tempered,
            MinimumValue = minimum,
            TuningPrisms = prisms
        };

        private static GearSnapshot Ring(params ScannedAffix[] affixes) => new()
        {
            ItemType = "ring",
            ItemPower = 800,
            Rarity = "Legendary",
            Affixes = affixes,
            IsConfirmed = true
        };

        private static BuildTarget Build(params TargetAffix[] targets) => new() { Name = "Test", Affixes = targets };

        [Test]
        public void FirstStep_IsTheSameAsTheRecommendation()
        {
            var item = Ring(Affix("Thorns"), Affix("CritChance"), Affix("Life"), Affix("Armor"));
            var build = Build(Target("CritChance"), Target("Life"), Target("Armor"), Target("AttackSpeed"));

            var plan = CraftingPlanner.Plan(item, build);
            var recommendation = CraftingAnalyzer.Analyze(item, build).Recommendation;

            Assert.That(plan.Steps[0].Instruction.Line, Is.EqualTo(recommendation.Instruction!.Line));
        }

        [Test]
        public void ChainsStepsUntilTheItemMatches()
        {
            // Missing Attack Speed (replace Thorns) and a tempered Life (temper afterwards).
            var item = Ring(Affix("Thorns"), Affix("CritChance"), Affix("Life"), Affix("Armor"));
            var build = Build(Target("CritChance"), Target("Life"), Target("Armor"), Target("AttackSpeed"), Target("Life", tempered: true));

            var plan = CraftingPlanner.Plan(item, build);

            Assert.That(plan.Steps.Select(s => s.Instruction.Operation), Is.EqualTo(new[] { "Enchant", "Temper" }));
            Assert.That(plan.Steps[0].IfItWorks, Is.EqualTo("Matches 4 of 5 build affixes."));
            Assert.That(plan.Steps[1].IfItWorks, Is.EqualTo("Matches 5 of 5 build affixes."));
            Assert.That(plan.EndNote, Does.Contain("Keep it"));
        }

        [Test]
        public void OnlyOneEnchantIsPlanned()
        {
            // Two off-target affixes in different prism groups: the first can be enchanted, the second can't.
            var item = Ring(Affix("Thorns", prisms: "TuningStone_2"), Affix("Willpower", prisms: "TuningStone_6"), Affix("Life"), Affix("Armor"));
            var build = Build(Target("Life"), Target("Armor"), Target("AttackSpeed", prisms: "TuningStone_1"), Target("CritChance", prisms: "TuningStone_1"));

            var plan = CraftingPlanner.Plan(item, build);

            Assert.That(plan.Steps.Count(s => s.Instruction.Operation == "Enchant"), Is.EqualTo(1));
            Assert.That(plan.EndNote, Does.Contain("No safe step left"));
        }

        [Test]
        public void ItemAlreadyDone_HasNoSteps()
        {
            var item = Ring(Affix("CritChance"));
            var plan = CraftingPlanner.Plan(item, Build(Target("CritChance")));

            Assert.That(plan.HasSteps, Is.False);
            Assert.That(plan.EndNote, Does.Contain("already has everything"));
        }

        [Test]
        public void ProtectedItem_HasNoStepsAndSaysWhy()
        {
            var item = Ring(Affix("Willpower", AffixKind.Greater), Affix("CritChance"), Affix("Life"), Affix("Armor"));
            var plan = CraftingPlanner.Plan(item, Build(Target("CritChance"), Target("Life"), Target("Armor"), Target("AttackSpeed")));

            Assert.That(plan.HasSteps, Is.False);
            Assert.That(plan.EndNote, Does.Contain("protected"));
        }

        [Test]
        public void PlanNeverRunsAway()
        {
            var item = Ring();
            var targets = Enumerable.Range(0, 20).Select(i => Target("Stat" + i)).ToArray();

            var plan = CraftingPlanner.Plan(item, Build(targets));

            Assert.That(plan.Steps.Count, Is.LessThanOrEqualTo(CraftingPlanner.MaxSteps));
        }
    }
}
