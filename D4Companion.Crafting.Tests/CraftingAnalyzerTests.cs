using D4Companion.Crafting;

namespace D4Companion.Crafting.Tests
{
    public class CraftingAnalyzerTests
    {
        private static ScannedAffix Affix(string id, AffixKind kind = AffixKind.Normal, double? value = 10, bool keep = false, params string[] prisms) => new()
        {
            AffixId = id,
            DisplayName = id,
            Kind = kind,
            Value = value,
            IsKeep = keep,
            TuningPrisms = prisms
        };

        private static TargetAffix Target(string id, bool greater = false, bool tempered = false, bool isImplicit = false, double? minimum = null, params string[] prisms) => new()
        {
            AffixId = id,
            DisplayName = id,
            ItemType = "ring",
            RequireGreater = greater,
            IsTempered = tempered,
            IsImplicit = isImplicit,
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
        public void UnconfirmedItem_ShowsNoRecommendation()
        {
            var item = Ring(Affix("CritChance")) with { IsConfirmed = false };

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance")));

            Assert.That(result.Verdict, Is.EqualTo(CraftingVerdict.NotConfirmed));
            Assert.That(result.Recommendation.Kind, Is.EqualTo(RecommendationKind.ConfirmFirst));
            Assert.That(result.Comparisons, Is.Empty);
        }

        [Test]
        public void AllTargetsMatch_KeepItemAndStop()
        {
            var item = Ring(Affix("CritChance"), Affix("MaxLife"), Affix("DamageOverTime"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("MaxLife")));

            Assert.That(result.Verdict, Is.EqualTo(CraftingVerdict.MeetsTarget));
            Assert.That(result.MatchesBuild, Is.True);
            Assert.That(result.Recommendation.Kind, Is.EqualTo(RecommendationKind.KeepItem));
            Assert.That(result.Recommendation.ReplaceCandidate, Is.Null);
            Assert.That(result.Recommendation.Summary, Does.Contain("target affixes matched"));
        }

        [Test]
        public void UnknownOcrAffix_RequiresCorrection()
        {
            var unknown = new ScannedAffix { OcrText = "+12.5% Cr1t Ch@nce", Value = 12.5, Kind = AffixKind.Normal };
            var item = Ring(Affix("MaxLife"), unknown);

            var result = CraftingAnalyzer.Analyze(item, Build(Target("MaxLife"), Target("CritChance")));

            Assert.That(result.Verdict, Is.EqualTo(CraftingVerdict.NeedsCorrection));
            Assert.That(result.Recommendation.Kind, Is.EqualTo(RecommendationKind.CorrectScan));
            Assert.That(result.Comparisons.Any(c => c.Status == AffixStatus.Unknown && c.ScannedIndex == 1), Is.True);
        }

        [Test]
        public void AffixWithUnknownKind_IsTreatedAsUnknown()
        {
            var item = Ring(Affix("MaxLife", AffixKind.Unknown));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("MaxLife")));

            Assert.That(result.Verdict, Is.EqualTo(CraftingVerdict.NeedsCorrection));
        }

        [Test]
        public void DuplicateAffix_IsFlaggedAndNotCountedTwice()
        {
            var item = Ring(Affix("CritChance"), Affix("CritChance"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance")));

            Assert.That(result.Verdict, Is.EqualTo(CraftingVerdict.NeedsCorrection));
            Assert.That(result.Comparisons.Count(c => c.IsDuplicate), Is.EqualTo(1));
            Assert.That(result.MatchCount, Is.EqualTo(1));
        }

        [Test]
        public void MissingTarget_SuggestsOffTargetCandidateAndPrisms()
        {
            var item = Ring(Affix("CritChance"), Affix("MaxLife"), Affix("DamageOverTime"));
            var build = Build(Target("CritChance"), Target("MaxLife"), Target("AttackSpeed", prisms: new[] { "Offensive" }));

            var result = CraftingAnalyzer.Analyze(item, build);

            Assert.That(result.Verdict, Is.EqualTo(CraftingVerdict.NeedsWork));
            Assert.That(result.Comparisons.Single(c => c.Target?.AffixId == "AttackSpeed").Status, Is.EqualTo(AffixStatus.Missing));
            Assert.That(result.Recommendation.Kind, Is.EqualTo(RecommendationKind.ReviewReroll));
            Assert.That(result.Recommendation.ReplaceCandidate?.AffixId, Is.EqualTo("DamageOverTime"));
            Assert.That(result.Recommendation.MissingTargets, Is.EqualTo(new[] { "AttackSpeed" }));
            Assert.That(result.Recommendation.Keep, Is.EquivalentTo(new[] { "CritChance", "MaxLife" }));
            Assert.That(result.Recommendation.PrismHints.Single().Prisms, Is.EqualTo(new[] { "Offensive" }));
            Assert.That(result.Recommendation.InGameChecks, Does.Contain(CraftingAnalyzer.PrismDisclaimer));
        }

        [Test]
        public void GreaterRequired_ReportsGreaterNeeded()
        {
            var item = Ring(Affix("CritChance", AffixKind.Normal));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance", greater: true)));

            Assert.That(result.Comparisons.Single().Status, Is.EqualTo(AffixStatus.GreaterNeeded));
            Assert.That(result.Recommendation.Kind, Is.EqualTo(RecommendationKind.ImproveValues));
        }

        [Test]
        public void GreaterRequired_SatisfiedByGreaterAffix()
        {
            var item = Ring(Affix("CritChance", AffixKind.Greater));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance", greater: true)));

            Assert.That(result.Verdict, Is.EqualTo(CraftingVerdict.MeetsTarget));
        }

        [Test]
        public void ValueBelowMinimum_ReportsBelowMinimum()
        {
            var item = Ring(Affix("MaxLife", value: 500));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("MaxLife", minimum: 900)));

            Assert.That(result.Comparisons.Single().Status, Is.EqualTo(AffixStatus.BelowMinimum));
        }

        [Test]
        public void MissingValueWithMinimum_RequiresReview()
        {
            var item = Ring(Affix("MaxLife", value: null));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("MaxLife", minimum: 900)));

            Assert.That(result.Comparisons.Single().Status, Is.EqualTo(AffixStatus.Review));
            Assert.That(result.MatchesBuild, Is.False);
        }

        [Test]
        public void ProtectedAffix_IsNeverSuggestedForReplacement()
        {
            var item = Ring(Affix("CritChance"), Affix("Life"), Affix("Armor"), Affix("DamageOverTime", keep: true));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("Life"), Target("Armor"), Target("AttackSpeed")));

            Assert.That(result.Recommendation.ReplaceCandidate, Is.Null);
            Assert.That(result.Recommendation.Keep, Does.Contain("DamageOverTime"));
            Assert.That(result.Recommendation.ReplaceCandidateNote, Does.Contain("Keep"));
        }

        [Test]
        public void ProtectedAffix_OtherCandidateStillChosen()
        {
            var item = Ring(Affix("CritChance"), Affix("DamageOverTime", keep: true), Affix("Thorns"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("AttackSpeed")));

            Assert.That(result.Recommendation.ReplaceCandidate?.AffixId, Is.EqualTo("Thorns"));
        }

        [Test]
        public void GreaterOffTargetAffix_IsProtectedByDefault()
        {
            var item = Ring(Affix("Willpower", AffixKind.Greater), Affix("CritChance"), Affix("Life"), Affix("Armor"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("Life"), Target("Armor"), Target("AttackSpeed")));

            Assert.That(result.Recommendation.ReplaceCandidate, Is.Null);
            Assert.That(result.Recommendation.Instruction, Is.Null);
            Assert.That(result.Recommendation.Protected, Is.EqualTo(new[] { "Willpower" }));
            Assert.That(result.Recommendation.Headline, Is.EqualTo("Replacement candidates are protected"));
            Assert.That(result.Recommendation.TargetStat, Is.EqualTo("AttackSpeed"));
            Assert.That(result.Comparisons.Single(c => c.Scanned?.AffixId == "Willpower").IsProtected, Is.True);
        }

        [Test]
        public void GreaterOffTargetAffix_SuggestedOnlyWhenPlayerOptsIn()
        {
            var item = Ring(Affix("Willpower", AffixKind.Greater), Affix("CritChance"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("AttackSpeed")),
                new AdvisorOptions { IncludeGreaterAffixes = true });

            Assert.That(result.Recommendation.ReplaceCandidate?.AffixId, Is.EqualTo("Willpower"));
            Assert.That(result.Recommendation.Headline, Is.EqualTo("Enchant Willpower"));
            Assert.That(result.Comparisons.Single(c => c.Scanned?.AffixId == "Willpower").IsProtected, Is.False);
            Assert.That(result.Recommendation.ReplaceCandidateNote, Does.Contain("greater"));
        }

        [Test]
        public void NormalOffTargetAffix_IsPreferredOverGreater()
        {
            var item = Ring(Affix("Willpower", AffixKind.Greater), Affix("Thorns"), Affix("CritChance"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("AttackSpeed")),
                new AdvisorOptions { IncludeGreaterAffixes = true });

            Assert.That(result.Recommendation.ReplaceCandidate?.AffixId, Is.EqualTo("Thorns"));
        }

        [Test]
        public void TemperedAndImplicitOffTarget_AreNotRerollCandidates()
        {
            var item = Ring(Affix("CritChance"), Affix("Thorns", AffixKind.Tempered), Affix("Resistance", AffixKind.Implicit));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("AttackSpeed")));

            Assert.That(result.Recommendation.ReplaceCandidate, Is.Null);
            Assert.That(result.Recommendation.InGameChecks.Any(c => c.Contains("tempering")), Is.True);
        }

        [Test]
        public void ImplicitTarget_OnlyMatchesImplicitAffix()
        {
            var item = Ring(Affix("Resistance", AffixKind.Normal));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("Resistance", isImplicit: true)));

            Assert.That(result.Comparisons.Single(c => c.Target != null).Status, Is.EqualTo(AffixStatus.Missing));
        }

        [Test]
        public void ImplicitTarget_MatchesImplicitAffix()
        {
            var item = Ring(Affix("Resistance", AffixKind.Implicit));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("Resistance", isImplicit: true)));

            Assert.That(result.Verdict, Is.EqualTo(CraftingVerdict.MeetsTarget));
        }

        [Test]
        public void TemperedTarget_NeedsATemperedAffix()
        {
            Assert.That(CraftingAnalyzer.Analyze(Ring(Affix("Thorns", AffixKind.Tempered)), Build(Target("Thorns", tempered: true))).Verdict,
                Is.EqualTo(CraftingVerdict.MeetsTarget));
            Assert.That(CraftingAnalyzer.Analyze(Ring(Affix("Thorns", AffixKind.Greater)), Build(Target("Thorns", tempered: true))).Comparisons.Single().Status,
                Is.EqualTo(AffixStatus.Review));
            Assert.That(CraftingAnalyzer.Analyze(Ring(Affix("Thorns", AffixKind.Normal)), Build(Target("Thorns", tempered: true))).Comparisons.Single().Status,
                Is.EqualTo(AffixStatus.Review));
        }

        [Test]
        public void NoTargetForSlot_GivesNoAdvice()
        {
            var item = Ring(Affix("CritChance")) with { ItemType = "helm" };

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance")));

            Assert.That(result.Verdict, Is.EqualTo(CraftingVerdict.NoTargetForSlot));
        }

        [Test]
        public void UniqueItem_IsNotAnalyzed()
        {
            var item = Ring(Affix("CritChance")) with { IsUnique = true };

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance")));

            Assert.That(result.Verdict, Is.EqualTo(CraftingVerdict.NotApplicable));
            Assert.That(result.Recommendation.Instruction, Is.Null);
        }

        [Test]
        public void UniqueInBuild_IsRecognisedAndItsAffixesCompared()
        {
            var item = Ring(Affix("CritChance"), Affix("Willpower")) with { IsUnique = true, UniqueId = "Ring_Unique_Generic_103", UniqueName = "Wendigo Brand" };
            var build = Build(Target("CritChance"), Target("AttackSpeed")) with { Uniques = new[] { new BuildUnique("Ring_Unique_Generic_103", "Wendigo Brand") } };

            var result = CraftingAnalyzer.Analyze(item, build);

            Assert.That(result.Recommendation.Headline, Is.EqualTo("Wendigo Brand is in your build"));
            Assert.That(result.Recommendation.Instruction, Is.Null);
            Assert.That(result.Comparisons.Single(c => c.Name == "CritChance").Status, Is.EqualTo(AffixStatus.Match));
            Assert.That(result.Comparisons.Single(c => c.Name == "Willpower").IsOffTarget, Is.True);
            Assert.That(result.Comparisons, Has.None.Matches<AffixComparison>(c => c.Status == AffixStatus.Missing),
                "The other ring's build affixes must not be reported as missing from the unique.");
            Assert.That(result.BuildMatchStatement, Is.EqualTo("1 of this unique's 2 affixes are in your build for this slot."));
        }

        private static TargetAffix RingTarget(string id, string variant) => Target(id) with { Variant = variant };

        // Ring 1 is Wendigo Brand (Willpower, Crit). Ring 2 is a legendary (Life, Armor, Attack Speed, Lucky Hit).
        private static BuildTarget TwoRingBuild() => Build(
                RingTarget("Willpower", "Ring 1"), RingTarget("CritChance", "Ring 1"),
                RingTarget("Life", "Ring 2"), RingTarget("Armor", "Ring 2"), RingTarget("AttackSpeed", "Ring 2"), RingTarget("LuckyHit", "Ring 2"))
            with { Uniques = new[] { new BuildUnique("Ring_Unique_Generic_103", "Wendigo Brand", "Ring 1") } };

        [Test]
        public void LegendaryRing_IsComparedWithTheNonUniqueRingOnly()
        {
            var item = Ring(Affix("Life"), Affix("Armor"), Affix("AttackSpeed"), Affix("Thorns"));

            var result = CraftingAnalyzer.Analyze(item, TwoRingBuild());

            Assert.That(result.ComparedWith, Is.EqualTo("Ring 2"));
            Assert.That(result.TargetCount, Is.EqualTo(4));
            Assert.That(result.Comparisons.Where(c => c.Status == AffixStatus.Missing).Select(c => c.Name), Is.EqualTo(new[] { "LuckyHit" }),
                "Wendigo Brand's affixes must not show as missing from the other ring.");
            Assert.That(result.BuildMatchStatement, Does.StartWith("Compared with Ring 2."));
        }

        [Test]
        public void UniqueRing_IsComparedWithItsOwnRing()
        {
            var item = Ring(Affix("Willpower"), Affix("CritChance")) with { IsUnique = true, UniqueId = "Ring_Unique_Generic_103", UniqueName = "Wendigo Brand" };

            var result = CraftingAnalyzer.Analyze(item, TwoRingBuild());

            Assert.That(result.ComparedWith, Is.EqualTo("Ring 1"));
            Assert.That(result.Recommendation.Headline, Is.EqualTo("Wendigo Brand is in your build"));
            Assert.That(result.MatchCount, Is.EqualTo(2));
        }

        [Test]
        public void TwoLegendaryRings_ItemGoesToTheBetterMatch()
        {
            var build = Build(RingTarget("Willpower", "Ring 1"), RingTarget("CritChance", "Ring 1"),
                              RingTarget("Life", "Ring 2"), RingTarget("Armor", "Ring 2"));

            Assert.That(CraftingAnalyzer.Analyze(Ring(Affix("Life"), Affix("Armor")), build).ComparedWith, Is.EqualTo("Ring 2"));
            Assert.That(CraftingAnalyzer.Analyze(Ring(Affix("CritChance")), build).ComparedWith, Is.EqualTo("Ring 1"));
        }

        [Test]
        public void BuildWithoutVariants_KeepsTheOldBehaviour()
        {
            var result = CraftingAnalyzer.Analyze(Ring(Affix("Life")), Build(Target("Life"), Target("Armor")));

            Assert.That(result.ComparedWith, Is.Empty);
            Assert.That(result.TargetCount, Is.EqualTo(2));
        }

        [Test]
        public void UniqueNotInBuild_SaysWhichUniquesTheBuildUses()
        {
            var item = Ring(Affix("CritChance")) with { IsUnique = true, UniqueId = "Ring_Other", UniqueName = "Other Ring" };
            var build = Build(Target("CritChance")) with { Uniques = new[] { new BuildUnique("Ring_Unique_Generic_103", "Wendigo Brand") } };

            var result = CraftingAnalyzer.Analyze(item, build);

            Assert.That(result.Recommendation.Headline, Is.EqualTo("Other Ring isn't in your build"));
            Assert.That(result.Recommendation.Summary, Does.Contain("Wendigo Brand"));
        }

        [Test]
        public void UnrecognisedUnique_SaysSo()
        {
            var item = Ring(Affix("CritChance")) with { IsUnique = true };

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance")));

            Assert.That(result.Recommendation.Headline, Is.EqualTo("Unique item"));
            Assert.That(result.Recommendation.Summary, Does.Contain("couldn't tell which unique"));
        }

        [Test]
        public void Recommendation_NeverMentionsChancesOrCosts()
        {
            var item = Ring(Affix("CritChance"), Affix("DamageOverTime"));
            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("AttackSpeed", prisms: new[] { "Offensive" })));

            var text = string.Join(" ", new[] { result.Recommendation.Headline, result.Recommendation.Summary }
                .Concat(result.Recommendation.InGameChecks));

            Assert.That(text, Does.Not.Contain("%"));
            Assert.That(text.ToLowerInvariant(), Does.Not.Contain("guaranteed to"));
            Assert.That(result.ItemQualityStatement, Is.Not.Empty);
            Assert.That(result.DamageStatement, Is.Not.Empty);
        }

        [Test]
        public void BuildTarget_IsCopiedNotShared()
        {
            var targets = new List<TargetAffix> { Target("CritChance") };
            var build = new BuildTarget { Name = "Test", Affixes = targets.ToList() };

            targets.Add(Target("AttackSpeed"));

            Assert.That(build.Affixes.Count, Is.EqualTo(1));
        }

        [Test]
        public void OneAffix_NeverCoversTwoBuildLines()
        {
            // Build wants Maximum Life as a regular and as a tempered affix. The item has one greater Maximum Life.
            var item = Ring(Affix("Life", AffixKind.Greater), Affix("CritChance"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("Life"), Target("Life", tempered: true), Target("CritChance")));

            var lifeRows = result.Comparisons.Where(c => c.Target?.AffixId == "Life").ToList();
            Assert.That(lifeRows.Single(c => !c.Target!.IsTempered).Status, Is.EqualTo(AffixStatus.Match));
            Assert.That(lifeRows.Single(c => c.Target!.IsTempered).Status, Is.EqualTo(AffixStatus.Missing));
            Assert.That(result.Recommendation.MissingTargets, Does.Contain("Life (tempered)"));
            Assert.That(result.MatchCount, Is.EqualTo(2));
        }

        [Test]
        public void RegularAndTemperedCopies_EachFindTheirOwnAffix()
        {
            var item = Ring(Affix("Life", AffixKind.Tempered), Affix("Life", AffixKind.Greater));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("Life", tempered: true), Target("Life")));

            Assert.That(result.Verdict, Is.EqualTo(CraftingVerdict.MeetsTarget));
        }

        [Test]
        public void SharedPrismGroup_SuggestsFocusedRerollWithThatPrism()
        {
            var item = Ring(Affix("Thorns", prisms: "TuningStone_2"), Affix("CritChance", prisms: "TuningStone_1"),
                Affix("Willpower", prisms: "TuningStone_6"), Affix("Life", prisms: "TuningStone_2X"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("Willpower"), Target("Life"),
                Target("Armor", prisms: "TuningStone_2")));

            var step = result.Recommendation.Instruction!;
            Assert.That(step.Station, Is.EqualTo("Horadric Cube"));
            Assert.That(step.Operation, Is.EqualTo("Focused Reroll"));
            Assert.That(step.PrismId, Is.EqualTo("TuningStone_2"));
            Assert.That(step.AffectedAffix, Is.EqualTo("Thorns"));
            Assert.That(step.DesiredAffix, Is.EqualTo("Armor"));
            Assert.That(step.Line, Is.EqualTo("Horadric Cube: Focused Reroll on Thorns with Protector's Tuning Prism"));
        }

        [Test]
        public void SharedPrismGroupWithOtherAffixes_SuggestsEnchantAndNamesTheRisk()
        {
            var item = Ring(Affix("Thorns", prisms: "TuningStone_2"), Affix("Life", prisms: "TuningStone_2"),
                Affix("CritChance", prisms: "TuningStone_1"), Affix("Willpower", prisms: "TuningStone_6"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("Life"), Target("CritChance"), Target("Willpower"),
                Target("Armor", prisms: "TuningStone_2")));

            var step = result.Recommendation.Instruction!;
            Assert.That(step.Station, Is.EqualTo("Occultist"));
            Assert.That(step.Operation, Is.EqualTo("Enchant"));
            Assert.That(step.AffectedAffix, Is.EqualTo("Thorns"));
            Assert.That(step.Reason, Does.Contain("Life"));
        }

        [Test]
        public void FreeAffixSlot_SuggestsAddAnAffixWithTheGoalPrism()
        {
            var item = Ring(Affix("CritChance"), Affix("Life"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("Life"), Target("AttackSpeed", prisms: "TuningStone_1")));

            var step = result.Recommendation.Instruction!;
            Assert.That(step.Operation, Is.EqualTo("Add an Affix"));
            Assert.That(step.PrismId, Is.EqualTo("TuningStone_1"));
            Assert.That(step.Caveat, Does.Contain("not guaranteed"));
        }

        [Test]
        public void OnlyTemperedMissing_SuggestsTempering()
        {
            var item = Ring(Affix("CritChance"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("Life", tempered: true)));

            Assert.That(result.Recommendation.Instruction?.Operation, Is.EqualTo("Temper"));
            Assert.That(result.Recommendation.Instruction?.Station, Is.EqualTo("Blacksmith"));
        }

        [Test]
        public void BelowMinimum_SuggestsMasterworking()
        {
            var item = Ring(Affix("CritChance", value: 3));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance", minimum: 5)));

            Assert.That(result.Recommendation.Instruction?.Operation, Is.EqualTo("Masterwork"));
        }

        [Test]
        public void GreaterNeeded_HasNoCraftingStep()
        {
            var item = Ring(Affix("CritChance"));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance", greater: true)));

            Assert.That(result.Recommendation.Instruction, Is.Null);
            Assert.That(result.Recommendation.Summary, Does.Contain("cannot make an existing affix greater"));
        }

        [Test]
        public void Instructions_NeverPromiseOddsOrCosts()
        {
            var item = Ring(Affix("Thorns", prisms: "TuningStone_2"), Affix("CritChance"));
            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("Armor", prisms: "TuningStone_2")));

            string text = string.Join(" ", result.Recommendation.Instruction!.Reason, result.Recommendation.Instruction.Caveat, result.Recommendation.Summary);
            Assert.That(text, Does.Not.Contain("%"));
            Assert.That(text, Does.Not.Contain("chance").IgnoreCase);
            Assert.That(text, Does.Not.Contain("gold").IgnoreCase);
        }
    }
}
