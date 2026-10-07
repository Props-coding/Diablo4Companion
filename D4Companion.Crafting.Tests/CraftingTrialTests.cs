using D4Companion.Crafting;

namespace D4Companion.Crafting.Tests
{
    /// <summary>
    /// Unknown limits versus checked ones, steps ruled out in-game, and what a trial records.
    /// </summary>
    public class CraftingTrialTests
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

        // Thorns and Armor share a prism group; Thorns is the only affix in it.
        private static GearSnapshot ThornsRing(ItemCraftState? state = null) =>
            Ring(state, Affix("Thorns", prisms: "TuningStone_2"), Affix("CritChance", prisms: "TuningStone_1"),
                Affix("Willpower", prisms: "TuningStone_6"), Affix("Life", prisms: "TuningStone_9"));

        private static BuildTarget ArmorBuild() =>
            Build(Target("CritChance"), Target("Willpower"), Target("Life"), Target("Armor", prisms: "TuningStone_2"));

        private static readonly ItemCraftState AllChecked = new()
        {
            CannotBeModified = false, NoTempersLeft = false, FullyMasterworked = false, EnchantedAffixId = string.Empty
        };

        // Unknown is not the same as allowed

        [Test]
        public void UncheckedItem_AsksWhetherItCanStillBeChanged()
        {
            var step = CraftingAnalyzer.Analyze(ThornsRing(), ArmorBuild()).Recommendation.Instruction!;

            Assert.That(step.ConfirmInGame, Does.Contain(CraftingAnalyzer.CheckCanChange));
            Assert.That(step.Checked, Has.None.Contains("can still be changed"));
        }

        [Test]
        public void CheckedItem_MovesTheLimitToChecked()
        {
            var step = CraftingAnalyzer.Analyze(ThornsRing(AllChecked), ArmorBuild()).Recommendation.Instruction!;

            Assert.That(step.ConfirmInGame, Does.Not.Contain(CraftingAnalyzer.CheckCanChange));
            Assert.That(step.Checked, Has.Some.Contains("You checked that the item can still be changed"));
        }

        [Test]
        public void UncheckedEnchant_IsAskedNotAssumed()
        {
            var item = Ring(null, Affix("Thorns"), Affix("CritChance"), Affix("Willpower"), Affix("Life"));
            var step = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("Willpower"), Target("Life"), Target("Armor"))).Recommendation.Instruction!;

            Assert.That(step.Operation, Is.EqualTo("Enchant"));
            Assert.That(step.ConfirmInGame, Does.Contain(CraftingAnalyzer.CheckNoEnchantYet));

            var checkedItem = item with { CraftState = new ItemCraftState { EnchantedAffixId = string.Empty } };
            var checkedStep = CraftingAnalyzer.Analyze(checkedItem, Build(Target("CritChance"), Target("Willpower"), Target("Life"), Target("Armor"))).Recommendation.Instruction!;
            Assert.That(checkedStep.ConfirmInGame, Does.Not.Contain(CraftingAnalyzer.CheckNoEnchantYet));
            Assert.That(checkedStep.Checked, Has.Some.Contains("no affix on this item is enchanted yet"));
        }

        [Test]
        public void UncheckedTempers_AreAskedNotAssumed()
        {
            var build = Build(Target("CritChance"), Target("Life", tempered: true));

            var unknown = CraftingAnalyzer.Analyze(Ring(null, Affix("CritChance")), build).Recommendation.Instruction!;
            Assert.That(unknown.ConfirmInGame, Does.Contain(CraftingAnalyzer.CheckTempersLeft));

            var known = CraftingAnalyzer.Analyze(Ring(AllChecked, Affix("CritChance")), build).Recommendation.Instruction!;
            Assert.That(known.ConfirmInGame, Does.Not.Contain(CraftingAnalyzer.CheckTempersLeft));
            Assert.That(known.Checked, Has.Some.Contains("tempers left"));
        }

        [Test]
        public void SlotCheck_IsAlwaysLeftForTheGame()
        {
            var step = CraftingAnalyzer.Analyze(ThornsRing(AllChecked), ArmorBuild()).Recommendation.Instruction!;

            Assert.That(step.GoalCheck, Is.EqualTo("Armor can roll on a ring."));
            Assert.That(step.ConfirmInGame, Does.Contain(step.GoalCheck));
        }

        // Ruled out in-game

        [Test]
        public void RuledOutFocusedReroll_FallsBackToEnchant()
        {
            var first = CraftingAnalyzer.Analyze(ThornsRing(), ArmorBuild()).Recommendation.Instruction!;
            Assert.That(first.Operation, Is.EqualTo("Focused Reroll"));

            var state = new ItemCraftState { RuledOut = new[] { new RuledOutStep(first.Key, "Focused Reroll on Thorns") } };
            var next = CraftingAnalyzer.Analyze(ThornsRing(state), ArmorBuild()).Recommendation.Instruction!;

            Assert.That(next.Operation, Is.EqualTo("Enchant"));
            Assert.That(next.AffectedAffix, Is.EqualTo("Thorns"));
        }

        [Test]
        public void BothStepsRuledOut_NoStepIsSuggested()
        {
            var state = new ItemCraftState
            {
                RuledOut = new[]
                {
                    new RuledOutStep(RuledOutStep.StepKey("Focused Reroll", "Thorns", "Armor"), "a"),
                    new RuledOutStep(RuledOutStep.StepKey("Enchant", "Thorns", "Armor"), "b")
                }
            };

            var result = CraftingAnalyzer.Analyze(ThornsRing(state), ArmorBuild());

            Assert.That(result.Recommendation.Instruction, Is.Null);
            Assert.That(result.Recommendation.Headline, Is.EqualTo("No safe step left"));
            Assert.That(result.Recommendation.Summary, Does.Contain("ruled out in-game"));
        }

        [Test]
        public void GoalThatCantRoll_StopsSpendingOnTheItem()
        {
            var state = new ItemCraftState { RuledOut = new[] { new RuledOutStep(RuledOutStep.GoalKey("Armor"), "Armor can't roll on this ring") } };

            var result = CraftingAnalyzer.Analyze(ThornsRing(state), ArmorBuild());

            Assert.That(result.Recommendation.Instruction, Is.Null);
            Assert.That(result.Recommendation.Headline, Is.EqualTo("This item can't reach your build"));
            Assert.That(CraftingPlanner.Plan(ThornsRing(state), ArmorBuild()).HasSteps, Is.False);
        }

        [Test]
        public void RuledOutTemper_IsNotSuggestedAgain()
        {
            var state = new ItemCraftState { RuledOut = new[] { new RuledOutStep(RuledOutStep.StepKey("Temper", string.Empty, "Life"), "t") } };

            var result = CraftingAnalyzer.Analyze(Ring(state, Affix("CritChance")), Build(Target("CritChance"), Target("Life", tempered: true)));

            Assert.That(result.Recommendation.Instruction, Is.Null);
            Assert.That(result.Recommendation.Headline, Is.EqualTo("Tempering ruled out"));
        }

        [Test]
        public void RuledOutSteps_SurviveSavingAndCleaning()
        {
            var state = new ItemCraftState { RuledOut = new[] { new RuledOutStep(RuledOutStep.GoalKey("Armor"), "Armor can't roll"), new RuledOutStep(RuledOutStep.GoalKey("armor"), "dup") } };

            var clean = GearValidator.Sanitize(ThornsRing(state));

            Assert.That(clean.CraftState.RuledOut, Has.Count.EqualTo(1));
            Assert.That(clean.CraftState.EnchantedAffixId, Is.Null, "Not checked must stay not checked.");
        }

        // What a trial records

        [Test]
        public void Judge_RerollThatLanded_Worked()
        {
            var step = CraftingAnalyzer.Analyze(ThornsRing(), ArmorBuild()).Recommendation.Instruction!;
            var after = ThornsRing() with { Affixes = ThornsRing().Affixes.Select(a => a.AffixId == "Thorns" ? Affix("Armor", prisms: "TuningStone_2") : a).ToList() };

            var outcome = CraftTrial.Judge(step, ThornsRing(), after);

            Assert.That(outcome, Is.EqualTo(TrialOutcome.Worked));
            Assert.That(CraftTrial.Describe(outcome, step), Is.EqualTo("Worked: Armor is now on the item."));
        }

        [Test]
        public void Judge_RerollThatMissed_DidntLand()
        {
            var step = CraftingAnalyzer.Analyze(ThornsRing(), ArmorBuild()).Recommendation.Instruction!;
            var after = ThornsRing() with { Affixes = ThornsRing().Affixes.Select(a => a.AffixId == "Thorns" ? Affix("DamageReduction", prisms: "TuningStone_2") : a).ToList() };

            var outcome = CraftTrial.Judge(step, ThornsRing(), after);

            Assert.That(outcome, Is.EqualTo(TrialOutcome.DifferentResult));
            Assert.That(CraftTrial.Describe(outcome, step), Does.Contain("Thorns changed, but not into Armor"));
        }

        [Test]
        public void Judge_WrongAffixChanged_IsFlagged()
        {
            var step = CraftingAnalyzer.Analyze(ThornsRing(), ArmorBuild()).Recommendation.Instruction!;
            var after = ThornsRing() with { Affixes = ThornsRing().Affixes.Select(a => a.AffixId == "Willpower" ? Affix("Strength") : a).ToList() };

            Assert.That(CraftTrial.Judge(step, ThornsRing(), after), Is.EqualTo(TrialOutcome.OtherAffixChanged));
        }

        [Test]
        public void Judge_EnchantKeptOriginalWithNewValue_IsNotCalledWorked()
        {
            var item = Ring(AllChecked, Affix("Thorns"), Affix("CritChance"), Affix("Willpower"), Affix("Life"));
            var build = Build(Target("CritChance"), Target("Willpower"), Target("Life"), Target("Armor"));
            var step = CraftingAnalyzer.Analyze(item, build).Recommendation.Instruction!;
            Assert.That(step.Operation, Is.EqualTo("Enchant"));

            Assert.That(CraftTrial.Judge(step, item, item with { CapturedAtUtc = DateTime.UtcNow }), Is.EqualTo(TrialOutcome.KeptOriginal));
        }

        [Test]
        public void Judge_MasterworkBelowMinimum_StillShort()
        {
            var item = Ring(null, Affix("CritChance", value: 3));
            var build = Build(Target("CritChance", minimum: 5));
            var step = CraftingAnalyzer.Analyze(item, build).Recommendation.Instruction!;

            var raised = item with { Affixes = new[] { Affix("CritChance", value: 4) } };
            var enough = item with { Affixes = new[] { Affix("CritChance", value: 5.5) } };

            Assert.That(CraftTrial.Judge(step, item, raised), Is.EqualTo(TrialOutcome.DifferentResult));
            Assert.That(CraftTrial.Judge(step, item, enough), Is.EqualTo(TrialOutcome.Worked));
        }

        [Test]
        public void TrialLog_ListsWhatWasTriedAndWhatHappened()
        {
            var store = new GearStore(Path.Combine(Path.GetTempPath(), $"trial-{Guid.NewGuid():N}.json"));
            var added = store.AddItem(ThornsRing(), "Scanned").Record!;
            var after = ThornsRing() with { Affixes = ThornsRing().Affixes.Select(a => a.AffixId == "Thorns" ? Affix("DamageReduction") : a).ToList() };
            var record = store.AddRevision(added.Id, after, "After crafting", "Horadric Cube: Focused Reroll on Thorns, aiming for Armor", "Didn't land: Thorns changed, but not into Armor.").Record!;

            string log = CraftTrial.FormatLog(record, "Ring", id => id);

            Assert.That(log, Does.Contain("1. Scanned"));
            Assert.That(log, Does.Contain("2. After crafting"));
            Assert.That(log, Does.Contain("Tried: Horadric Cube: Focused Reroll on Thorns, aiming for Armor"));
            Assert.That(log, Does.Contain("Result: Didn't land"));
            Assert.That(log, Does.Contain("DamageReduction 10"));

            var fresh = new GearStore(store.FilePath);
            fresh.Load();
            var reloaded = fresh.Find(record.Id)!;
            Assert.That(reloaded.Revisions[^1].Outcome, Does.StartWith("Didn't land"));
        }
    }
}
