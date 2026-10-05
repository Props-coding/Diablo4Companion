using D4Companion.Crafting;

namespace D4Companion.Crafting.Tests
{
    public class CraftingAnalyzerTests
    {
        private static ScannedAffix Affix(string id, AffixKind kind = AffixKind.Normal, double? value = 10, bool keep = false) => new()
        {
            AffixId = id,
            DisplayName = id,
            Kind = kind,
            Value = value,
            IsKeep = keep
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
            Assert.That(result.Recommendation.Summary, Does.Contain("no need to spend"));
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
            var item = Ring(Affix("CritChance"), Affix("DamageOverTime", keep: true));

            var result = CraftingAnalyzer.Analyze(item, Build(Target("CritChance"), Target("AttackSpeed")));

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
        public void TemperedTarget_AcceptsTemperedOrGreaterAffix()
        {
            Assert.That(CraftingAnalyzer.Analyze(Ring(Affix("Thorns", AffixKind.Tempered)), Build(Target("Thorns", tempered: true))).Verdict,
                Is.EqualTo(CraftingVerdict.MeetsTarget));
            Assert.That(CraftingAnalyzer.Analyze(Ring(Affix("Thorns", AffixKind.Greater)), Build(Target("Thorns", tempered: true))).Verdict,
                Is.EqualTo(CraftingVerdict.MeetsTarget));
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
    }
}
