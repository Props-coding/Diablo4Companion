namespace D4Companion.Crafting
{
    public enum AffixStatus
    {
        /// <summary>The item has this build affix and it meets every requirement.</summary>
        Match,
        /// <summary>The build wants this affix and the item does not have it.</summary>
        Missing,
        /// <summary>The affix is present but its value is below the minimum you set.</summary>
        BelowMinimum,
        /// <summary>The build marks this affix as greater and the item has a normal one.</summary>
        GreaterNeeded,
        /// <summary>Something the user should look at: an off-target affix, a duplicate or a value that was not read.</summary>
        Review,
        /// <summary>The scan could not identify this line. It must be corrected.</summary>
        Unknown
    }

    public enum CraftingVerdict
    {
        NotConfirmed,
        NeedsCorrection,
        NoTargetForSlot,
        NotApplicable,
        MeetsTarget,
        NeedsWork
    }

    public enum RecommendationKind
    {
        ConfirmFirst,
        CorrectScan,
        NoAdvice,
        KeepItem,
        ReviewReroll,
        ImproveValues,
        ReviewItem
    }

    public sealed record AffixComparison
    {
        public TargetAffix? Target { get; init; }
        public ScannedAffix? Scanned { get; init; }
        public int? ScannedIndex { get; init; }
        public AffixStatus Status { get; init; }
        public bool IsOffTarget { get; init; }
        public bool IsDuplicate { get; init; }
        public string Explanation { get; init; } = string.Empty;

        public string Name => Scanned?.DisplayName is { Length: > 0 } scannedName ? scannedName
            : Target?.DisplayName is { Length: > 0 } targetName ? targetName
            : Scanned?.OcrText is { Length: > 0 } ocr ? ocr
            : Target?.AffixId ?? Scanned?.AffixId ?? string.Empty;
    }

    public sealed record PrismHint(string AffixName, IReadOnlyList<string> Prisms);

    public sealed record CraftingRecommendation
    {
        public RecommendationKind Kind { get; init; }
        public string Headline { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public IReadOnlyList<string> Keep { get; init; } = Array.Empty<string>();
        public ScannedAffix? ReplaceCandidate { get; init; }
        public string ReplaceCandidateNote { get; init; } = string.Empty;
        public IReadOnlyList<string> MissingTargets { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ImproveTargets { get; init; } = Array.Empty<string>();
        public IReadOnlyList<PrismHint> PrismHints { get; init; } = Array.Empty<PrismHint>();
        public IReadOnlyList<string> InGameChecks { get; init; } = Array.Empty<string>();
    }

    public sealed record CraftingAnalysis
    {
        public CraftingVerdict Verdict { get; init; }
        public IReadOnlyList<AffixComparison> Comparisons { get; init; } = Array.Empty<AffixComparison>();
        public CraftingRecommendation Recommendation { get; init; } = new();
        public int TargetCount { get; init; }
        public int MatchCount { get; init; }

        /// <summary>
        /// "Matches the build": only true when every target affix for this slot is a full match.
        /// </summary>
        public bool MatchesBuild => Verdict == CraftingVerdict.MeetsTarget;

        /// <summary>
        /// Plain statement about build match, kept separate from item quality and damage.
        /// </summary>
        public string BuildMatchStatement { get; init; } = string.Empty;
        public string ItemQualityStatement { get; init; } = CraftingAnalyzer.ItemQualityStatement;
        public string DamageStatement { get; init; } = CraftingAnalyzer.DamageStatement;
    }

    /// <summary>
    /// Compares a confirmed item with the selected build and suggests a conservative next step.
    /// It never estimates success chances or material costs, and never states that an operation is allowed.
    /// </summary>
    public static class CraftingAnalyzer
    {
        public const string ItemQualityStatement =
            "Whether this is a good item overall is not judged here. Only your selected build is checked.";
        public const string DamageStatement =
            "This app does not calculate damage. A closer build match does not guarantee more damage.";
        public const string PrismDisclaimer =
            "A prism narrows the possible results to a category. It does not guarantee a specific affix.";

        public static CraftingAnalysis Analyze(GearSnapshot snapshot, BuildTarget? build)
        {
            if (!snapshot.IsConfirmed)
            {
                return new CraftingAnalysis
                {
                    Verdict = CraftingVerdict.NotConfirmed,
                    Recommendation = new CraftingRecommendation
                    {
                        Kind = RecommendationKind.ConfirmFirst,
                        Headline = "Confirm the item first",
                        Summary = "Check every slot, value and affix type against the in-game tooltip, then confirm. Recommendations appear after that."
                    }
                };
            }

            if (snapshot.IsUnique)
            {
                return new CraftingAnalysis
                {
                    Verdict = CraftingVerdict.NotApplicable,
                    Recommendation = new CraftingRecommendation
                    {
                        Kind = RecommendationKind.NoAdvice,
                        Headline = "Unique item",
                        Summary = "Unique items are not compared with affix targets in this version."
                    }
                };
            }

            var targets = build?.ForItemType(snapshot.ItemType) ?? Array.Empty<TargetAffix>();
            var affixes = snapshot.Affixes ?? Array.Empty<ScannedAffix>();
            var comparisons = new List<AffixComparison>();
            var used = new bool[affixes.Count];
            var duplicate = new bool[affixes.Count];
            bool needsCorrection = false;

            // Unknown lines and duplicates first: they cannot be trusted for matching.
            var seen = new HashSet<(string, bool)>();
            for (int i = 0; i < affixes.Count; i++)
            {
                var affix = affixes[i];
                if (!affix.IsIdentified)
                {
                    needsCorrection = true;
                    used[i] = true;
                    comparisons.Add(new AffixComparison
                    {
                        Scanned = affix,
                        ScannedIndex = i,
                        Status = AffixStatus.Unknown,
                        Explanation = "The scan could not identify this line. Correct it before relying on the result."
                    });
                    continue;
                }

                var key = (affix.AffixId.ToLowerInvariant(), affix.Kind == AffixKind.Implicit);
                if (!seen.Add(key))
                {
                    needsCorrection = true;
                    duplicate[i] = true;
                    used[i] = true;
                    comparisons.Add(new AffixComparison
                    {
                        Scanned = affix,
                        ScannedIndex = i,
                        Status = AffixStatus.Review,
                        IsDuplicate = true,
                        Explanation = "This affix appears twice. That is usually a scan mistake."
                    });
                }
            }

            if (targets.Count == 0)
            {
                return new CraftingAnalysis
                {
                    Verdict = needsCorrection ? CraftingVerdict.NeedsCorrection : CraftingVerdict.NoTargetForSlot,
                    Comparisons = comparisons,
                    BuildMatchStatement = "Your selected build has no affix targets for this slot.",
                    Recommendation = needsCorrection ? CorrectScanRecommendation() : new CraftingRecommendation
                    {
                        Kind = RecommendationKind.NoAdvice,
                        Headline = "No target for this slot",
                        Summary = "Add affixes for this slot to your build on the Builds page, or choose another build."
                    }
                };
            }

            int matches = 0;
            foreach (var target in targets)
            {
                int index = FindScanned(affixes, used, target);
                if (index < 0)
                {
                    comparisons.Add(new AffixComparison
                    {
                        Target = target,
                        Status = AffixStatus.Missing,
                        Explanation = "Your build wants this affix and the item does not have it."
                    });
                    continue;
                }

                used[index] = true;
                var scanned = affixes[index];
                var (status, explanation) = Evaluate(target, scanned);
                if (status == AffixStatus.Match) matches++;
                comparisons.Add(new AffixComparison
                {
                    Target = target,
                    Scanned = scanned,
                    ScannedIndex = index,
                    Status = status,
                    Explanation = explanation
                });
            }

            for (int i = 0; i < affixes.Count; i++)
            {
                if (used[i]) continue;
                var affix = affixes[i];
                comparisons.Add(new AffixComparison
                {
                    Scanned = affix,
                    ScannedIndex = i,
                    Status = AffixStatus.Review,
                    IsOffTarget = true,
                    Explanation = affix.Kind == AffixKind.Implicit
                        ? "Implicit affix that is not part of your build target."
                        : "Not part of your build target for this slot."
                });
            }

            string matchStatement = $"Matches {matches} of {targets.Count} build affixes for this slot.";

            if (needsCorrection)
            {
                return new CraftingAnalysis
                {
                    Verdict = CraftingVerdict.NeedsCorrection,
                    Comparisons = comparisons,
                    TargetCount = targets.Count,
                    MatchCount = matches,
                    BuildMatchStatement = matchStatement,
                    Recommendation = CorrectScanRecommendation()
                };
            }

            if (matches == targets.Count)
            {
                return new CraftingAnalysis
                {
                    Verdict = CraftingVerdict.MeetsTarget,
                    Comparisons = comparisons,
                    TargetCount = targets.Count,
                    MatchCount = matches,
                    BuildMatchStatement = $"Matches all {targets.Count} build affixes for this slot.",
                    Recommendation = new CraftingRecommendation
                    {
                        Kind = RecommendationKind.KeepItem,
                        Headline = "Keep this item",
                        Summary = "It meets your selected target. There is no need to spend more materials on it.",
                        Keep = comparisons.Where(c => c.Status == AffixStatus.Match).Select(c => c.Name).ToList()
                    }
                };
            }

            return new CraftingAnalysis
            {
                Verdict = CraftingVerdict.NeedsWork,
                Comparisons = comparisons,
                TargetCount = targets.Count,
                MatchCount = matches,
                BuildMatchStatement = matchStatement,
                Recommendation = BuildNextStep(comparisons)
            };
        }

        private static int FindScanned(IReadOnlyList<ScannedAffix> affixes, bool[] used, TargetAffix target)
        {
            for (int i = 0; i < affixes.Count; i++)
            {
                if (used[i]) continue;
                var affix = affixes[i];
                if (!string.Equals(affix.AffixId, target.AffixId, StringComparison.OrdinalIgnoreCase)) continue;
                if ((affix.Kind == AffixKind.Implicit) != target.IsImplicit) continue;
                return i;
            }
            return -1;
        }

        private static (AffixStatus, string) Evaluate(TargetAffix target, ScannedAffix scanned)
        {
            if (target.RequireGreater && scanned.Kind != AffixKind.Greater)
            {
                return (AffixStatus.GreaterNeeded, "Your build marks this as a greater affix. The item has a regular one.");
            }

            if (target.MinimumValue is double minimum && minimum > 0)
            {
                if (scanned.Value is not double value)
                {
                    return (AffixStatus.Review, "The value was not read, so the minimum cannot be checked. Enter it manually.");
                }
                if (value < minimum)
                {
                    return (AffixStatus.BelowMinimum, $"The value {value:0.##} is below your minimum of {minimum:0.##}.");
                }
            }

            if (target.IsTempered && scanned.Kind != AffixKind.Tempered && scanned.Kind != AffixKind.Greater)
            {
                return (AffixStatus.Review, "Your build lists this as a tempered affix. The item has it as a regular affix. Check that this is what you want.");
            }

            return (AffixStatus.Match, "Present and meets your build target.");
        }

        private static CraftingRecommendation CorrectScanRecommendation() => new()
        {
            Kind = RecommendationKind.CorrectScan,
            Headline = "Correct the scan",
            Summary = "Some lines are unknown or duplicated. Fix them so the comparison can be trusted."
        };

        private static CraftingRecommendation BuildNextStep(IReadOnlyList<AffixComparison> comparisons)
        {
            var keep = comparisons
                .Where(c => c.Scanned != null && (c.Status == AffixStatus.Match || c.Scanned.IsKeep))
                .Select(c => c.Name)
                .Distinct()
                .ToList();

            var missing = comparisons.Where(c => c.Status == AffixStatus.Missing).ToList();
            var improve = comparisons
                .Where(c => c.Status is AffixStatus.BelowMinimum or AffixStatus.GreaterNeeded)
                .Select(c => c.Name)
                .ToList();

            var offTarget = comparisons.Where(c => c.IsOffTarget && c.Scanned != null && !c.Scanned.IsKeep).ToList();
            var candidate = offTarget.FirstOrDefault(c => c.Scanned!.Kind == AffixKind.Normal)
                ?? offTarget.FirstOrDefault(c => c.Scanned!.Kind == AffixKind.Greater);

            var checks = new List<string>
            {
                "Confirm in-game which affixes the operation can change on this item.",
                "Check that the item can still be modified, and check the material cost in-game."
            };

            string candidateNote = string.Empty;
            if (missing.Count == 0)
            {
                candidateNote = string.Empty;
            }
            else if (candidate?.Scanned?.Kind == AffixKind.Greater)
            {
                candidateNote = "This off-target affix is greater. Replacing it may lose the greater bonus.";
            }
            else if (candidate == null)
            {
                candidateNote = offTarget.Count == 0 && comparisons.Any(c => c.IsOffTarget)
                    ? "Every off-target affix is marked Keep, tempered or implicit, so no replacement is suggested."
                    : "There is no off-target affix to replace. Changing a matching affix would lose a build stat.";
            }

            if (comparisons.Any(c => c.IsOffTarget && c.Scanned?.Kind == AffixKind.Tempered))
            {
                checks.Add("Tempered affixes are changed by tempering, not by rerolling. Check your remaining tempers in-game.");
            }

            var prismHints = missing
                .Where(c => c.Target != null && c.Target.TuningPrisms.Count > 0)
                .Select(c => new PrismHint(c.Name, c.Target!.TuningPrisms))
                .ToList();
            if (prismHints.Count > 0)
            {
                checks.Add(PrismDisclaimer);
            }

            if (missing.Count > 1)
            {
                checks.Add($"This item is missing {missing.Count} build affixes. Check in-game which changes are still available before spending materials.");
            }

            RecommendationKind kind;
            string headline;
            string summary;
            if (missing.Count > 0 && candidate != null)
            {
                kind = RecommendationKind.ReviewReroll;
                headline = "Review a reroll";
                summary = $"Keep the matching affixes and check whether {candidate.Name} can be replaced with {missing[0].Name}.";
            }
            else if (missing.Count == 0 && improve.Count > 0)
            {
                kind = RecommendationKind.ImproveValues;
                headline = "Review value upgrades";
                summary = "All build affixes are present. Some are below your minimum or need to be greater.";
            }
            else
            {
                kind = RecommendationKind.ReviewItem;
                headline = "Review the item";
                summary = missing.Count > 0
                    ? $"The item is missing {missing[0].Name}, and no safe replacement was found."
                    : "Some affixes need a closer look before you decide.";
            }

            return new CraftingRecommendation
            {
                Kind = kind,
                Headline = headline,
                Summary = summary,
                Keep = keep,
                ReplaceCandidate = missing.Count > 0 ? candidate?.Scanned : null,
                ReplaceCandidateNote = candidateNote,
                MissingTargets = missing.Select(c => c.Name).ToList(),
                ImproveTargets = improve,
                PrismHints = prismHints,
                InGameChecks = checks
            };
        }
    }
}
