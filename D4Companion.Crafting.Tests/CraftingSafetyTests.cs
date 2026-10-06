using D4Companion.Crafting;

namespace D4Companion.Crafting.Tests
{
    /// <summary>
    /// Blocked operations, failed rolls, and what each step says it checked.
    /// </summary>
    public class CraftingSafetyTests
    {
        private static ScannedAffix Affix(string id, AffixKind kind = AffixKind.Normal, double? value = 10, params string[] prisms) => new()
        {
            AffixId = id,
            DisplayName = id,
            Kind = kind,
            Value = value,
            TuningPrisms = prisms
        };

        private static TargetAffix Target(string id, bool greater = false, bool tempered = false, double? minimum = null, bool? canTemper = null, params string[] prisms) => new()
        {
            AffixId = id,
            DisplayName = id,
            ItemType = "ring",
            RequireGreater = greater,
            IsTempered = tempered,
            MinimumValue = minimum,
            CanBeTempered = canTemper,
            TuningPrisms = prisms
        };

        private static GearSnapshot Ring(ItemCraftState? state, params ScannedAffix[] affixes) => new()
        {
            ItemType = "ring",
            ItemPower = 800,
            Rarity = "Legendary",
            Affixes = affixes,
            IsConfirmed = true,
            CraftState = state ?? new ItemCraftState()
        };

        private static BuildTarget Build(params TargetAffix[] targets) => new() { Name = "Test", Affixes = targets };

        // Ring missing Attack Speed, Thorns is the off-target affix.
        private static GearSnapshot ThornsRing(ItemCraftState? state = null) =>
            Ring(state, Affix("Thorns", prisms: "TuningStone_2"), Affix("CritChance", prisms: "TuningStone_1"), Affix("Life", prisms: "TuningStone_2"), Affix("Armor", prisms: "TuningStone_2"));

        private static BuildTarget AttackSpeedBuild() =>
            Build(Target("CritChance"), Target("Life"), Target("Armor"), Target("AttackSpeed", prisms: "TuningStone_1"));

        // Blocked operations

        [Test]
        public void UnmodifiableItem_GetsNoCraftingStep()
        {
            var result = CraftingAnalyzer.Analyze(ThornsRing(new ItemCraftState { CannotBeModified = true }), AttackSpeedBuild());

            Assert.That(result.Recommendation.Instruction, Is.Null);
            Assert.That(result.Recommendation.Headline, Is.EqualTo("This item can't be changed"));
            Assert.That(CraftingPlanner.Plan(ThornsRing(new ItemCraftState { CannotBeModified = true }), AttackSpeedBuild()).HasSteps, Is.False);
        }

        [Test]
        public void EnchantUsedOnAnotherAffix_NoEnchantIsSuggested()
        {
            // Thorns is not in Attack Speed's prism group, so only the Occultist could aim for it.
            var item = ThornsRing(new ItemCraftState { EnchantedAffixId = "Life" });

            var result = CraftingAnalyzer.Analyze(item, AttackSpeedBuild());

            Assert.That(result.Recommendation.Instruction, Is.Null);
            Assert.That(result.Recommendation.Headline, Is.EqualTo("No safe step left"));
        }

        [Test]
        public void EnchantedAffixOffTarget_IsTheOneToEnchant()
        {
            var item = Ring(new ItemCraftState { EnchantedAffixId = "Willpower" },
                Affix("Thorns"), Affix("Willpower"), Affix("CritChance"), Affix("Life"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("Life"), Target("AttackSpeed")));

            Assert.That(result.Recommendation.Instruction?.Operation, Is.EqualTo("Enchant"));
            Assert.That(result.Recommendation.Instruction?.AffectedAffix, Is.EqualTo("Willpower"));
            Assert.That(result.Recommendation.Instruction?.Checked, Has.Some.Contains("marked as enchanted"));
        }

        [Test]
        public void NoTempersLeft_BlocksTempering()
        {
            var item = Ring(new ItemCraftState { NoTempersLeft = true }, Affix("CritChance"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("Life", tempered: true)));

            Assert.That(result.Recommendation.Instruction, Is.Null);
            Assert.That(result.Recommendation.Headline, Is.EqualTo("No tempers left"));
        }

        [Test]
        public void StatThatCantBeTempered_IsNotSuggestedForTempering()
        {
            var item = Ring(null, Affix("CritChance"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("Willpower", tempered: true, canTemper: false)));

            Assert.That(result.Recommendation.Instruction, Is.Null);
            Assert.That(result.Recommendation.Headline, Is.EqualTo("Not a tempering affix"));
        }

        [Test]
        public void FullyMasterworked_BlocksMasterworking()
        {
            var item = Ring(new ItemCraftState { FullyMasterworked = true }, Affix("CritChance", value: 3));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance", minimum: 5)));

            Assert.That(result.Recommendation.Instruction, Is.Null);
        }

        // Failed rolls

        [Test]
        public void FailedFocusedReroll_PlanIsRebuiltFromTheRealResult()
        {
            // Before: Thorns is alone in the Protector's group with the goal Armor.
            var before = Ring(null, Affix("Thorns", prisms: "TuningStone_2"), Affix("CritChance", prisms: "TuningStone_1"),
                Affix("Willpower", prisms: "TuningStone_6"), Affix("Life", prisms: "TuningStone_9"));
            var build = Build(Target("CritChance"), Target("Willpower"), Target("Life"), Target("Armor", prisms: "TuningStone_2"));
            Assert.That(CraftingAnalyzer.Analyze(before, build).Recommendation.Instruction?.Operation, Is.EqualTo("Focused Reroll"));

            // The roll gave Damage Reduction instead of Armor. Same group, still not in the build.
            var after = before with { Affixes = before.Affixes.Select(a => a.AffixId == "Thorns" ? Affix("DamageReduction", prisms: "TuningStone_2") : a).ToList() };
            var result = CraftingAnalyzer.Analyze(after, build);

            Assert.That(result.Recommendation.Instruction?.Operation, Is.EqualTo("Focused Reroll"));
            Assert.That(result.Recommendation.Instruction?.AffectedAffix, Is.EqualTo("DamageReduction"));
            Assert.That(result.MatchCount, Is.EqualTo(3));
        }

        [Test]
        public void FailedEnchant_NextPlanOnlyEnchantsTheSameAffix()
        {
            // The enchant on Thorns gave Movement Speed. The enchant now belongs to Movement Speed.
            var after = Ring(new ItemCraftState { EnchantedAffixId = "MoveSpeed" },
                Affix("MoveSpeed", prisms: "TuningStone_4"), Affix("Willpower", prisms: "TuningStone_6"), Affix("Life"), Affix("Armor"));
            var build = Build(Target("Life"), Target("Armor"), Target("AttackSpeed", prisms: "TuningStone_1"), Target("CritChance", prisms: "TuningStone_1"));

            var plan = CraftingPlanner.Plan(after, build);

            Assert.That(plan.Steps[0].Instruction.Operation, Is.EqualTo("Enchant"));
            Assert.That(plan.Steps[0].Instruction.AffectedAffix, Is.EqualTo("MoveSpeed"));
            Assert.That(plan.Steps.Count(s => s.Instruction.Operation == "Enchant"), Is.EqualTo(1));
        }

        // What each step says it checked

        [Test]
        public void FocusedReroll_ListsDataChecksAndInGameChecksSeparately()
        {
            var item = Ring(null, Affix("Thorns", prisms: "TuningStone_2"), Affix("CritChance", prisms: "TuningStone_1"),
                Affix("Willpower", prisms: "TuningStone_6"), Affix("Life", prisms: "TuningStone_9"));
            var step = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("Willpower"), Target("Life"), Target("Armor", prisms: "TuningStone_2")))
                .Recommendation.Instruction!;

            Assert.That(step.Checked, Has.Some.Contains("(game data)"));
            Assert.That(step.Checked, Has.Some.Contains("(your scan)"));
            Assert.That(step.ConfirmInGame, Has.Some.Contains("can roll on a ring"));
            Assert.That(step.ConfirmInGame, Has.Some.Contains("offers Focused Reroll"));
            Assert.That(step.Caveat, Does.Contain("doesn't guarantee"));
        }

        [Test]
        public void EveryStepHasSomethingToConfirmInGame()
        {
            var cases = new (GearSnapshot Item, BuildTarget Build)[]
            {
                (ThornsRing(), AttackSpeedBuild()),
                (Ring(null, Affix("CritChance")), Build(Target("CritChance"), Target("AttackSpeed"))),
                (Ring(null, Affix("CritChance")), Build(Target("CritChance"), Target("Life", tempered: true))),
                (Ring(null, Affix("CritChance", value: 3)), Build(Target("CritChance", minimum: 5)))
            };

            foreach (var (item, build) in cases)
            {
                var step = CraftingAnalyzer.Analyze(item, build).Recommendation.Instruction;
                Assert.That(step, Is.Not.Null);
                Assert.That(step!.ConfirmInGame, Is.Not.Empty, step.Operation);
            }
        }

        // Completion wording

        [Test]
        public void Completion_SeparatesAffixesValuesAndGreater()
        {
            var item = Ring(null, Affix("CritChance", AffixKind.Normal, value: 3), Affix("Life", AffixKind.Greater));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance", greater: true, minimum: 5), Target("Life", greater: true)));

            Assert.That(result.AffixesStatement, Is.EqualTo("All 2 target affixes are on the item."));
            Assert.That(result.ValuesStatement, Is.EqualTo("Minimum values: 0 of 1 met."));
            Assert.That(result.GreaterStatement, Is.EqualTo("Greater affixes: 1 of 2 required are greater."));
            Assert.That(result.MatchesBuild, Is.False);
        }

        [Test]
        public void KeepItem_NeverClaimsMoreThanItChecked()
        {
            var result = CraftingAnalyzer.Analyze(Ring(null, Affix("CritChance"), Affix("Life")), Build(Target("CritChance"), Target("Life")));

            Assert.That(result.Recommendation.Summary, Does.StartWith("All 2 target affixes matched."));
            Assert.That(result.Recommendation.Summary, Does.Not.Contain("everything").IgnoreCase);
            Assert.That(result.ValuesStatement, Is.EqualTo("Minimum values: none set in your build."));
        }
    }
}
