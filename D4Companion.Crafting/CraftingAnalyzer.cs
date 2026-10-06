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
            : "Unknown affix";
    }

    public sealed record PrismHint(string AffixName, IReadOnlyList<string> Prisms);

    /// <summary>
    /// Choices the player makes about what the advisor may suggest.
    /// </summary>
    public sealed record AdvisorOptions
    {
        /// <summary>
        /// Greater affixes are valuable, so they are never suggested for replacement unless the player opts in.
        /// </summary>
        public bool IncludeGreaterAffixes { get; init; }
    }

    public sealed record CraftingRecommendation
    {
        public RecommendationKind Kind { get; init; }
        /// <summary>One short action, for example "Reroll Willpower".</summary>
        public string Headline { get; init; } = string.Empty;
        /// <summary>The build stat this step is aiming for, if any.</summary>
        public string TargetStat { get; init; } = string.Empty;
        /// <summary>One short sentence explaining the action.</summary>
        public string Summary { get; init; } = string.Empty;
        /// <summary>Greater affixes that were left out of suggestions because they are protected.</summary>
        public IReadOnlyList<string> Protected { get; init; } = Array.Empty<string>();
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

        public static CraftingAnalysis Analyze(GearSnapshot snapshot, BuildTarget? build, AdvisorOptions? options = null)
        {
            options ??= new AdvisorOptions();

            if (!snapshot.IsConfirmed)
            {
                return new CraftingAnalysis
                {
                    Verdict = CraftingVerdict.NotConfirmed,
                    Recommendation = new CraftingRecommendation
                    {
                        Kind = RecommendationKind.ConfirmFirst,
                        Headline = "Confirm the item first",
                        Summary = "Check the item against the game, then confirm it to get advice."
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
                        Summary = "It has every affix your build wants for this slot. No need to spend more materials on it.",
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
                Recommendation = BuildNextStep(comparisons, options)
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

        private static CraftingRecommendation BuildNextStep(IReadOnlyList<AffixComparison> comparisons, AdvisorOptions options)
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
            var protectedGreater = offTarget.Where(c => c.Scanned!.Kind == AffixKind.Greater).ToList();
            var candidate = offTarget.FirstOrDefault(c => c.Scanned!.Kind == AffixKind.Normal)
                ?? (options.IncludeGreaterAffixes ? protectedGreater.FirstOrDefault() : null);

            var checks = new List<string>
            {
                "Confirm in-game which affixes the operation can change on this item.",
                "Check that the item can still be modified, and check the material cost in-game."
            };

            string candidateNote = string.Empty;
            if (missing.Count > 0)
            {
                if (candidate?.Scanned?.Kind == AffixKind.Greater)
                {
                    candidateNote = "This is a greater affix. You chose to include greater affixes, but replacing it loses the greater bonus.";
                }
                else if (candidate == null && protectedGreater.Count > 0)
                {
                    candidateNote = "Your only off-target affixes are greater, so they are protected. Tick \"Include greater affixes\" if you still want to consider them.";
                }
                else if (candidate == null)
                {
                    candidateNote = offTarget.Count == 0 && comparisons.Any(c => c.IsOffTarget)
                        ? "Every off-target affix is marked Keep, tempered or implicit, so no replacement is suggested."
                        : "Every affix on this item is in your build. Replacing one would lose a build stat.";
                }
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
            string targetStat = string.Empty;
            string summary;
            if (missing.Count > 0 && candidate != null)
            {
                kind = RecommendationKind.ReviewReroll;
                headline = $"Reroll {candidate.Name}";
                targetStat = missing[0].Name;
                summary = $"{candidate.Name} is not in your build. Aim for {missing[0].Name} instead.";
            }
            else if (missing.Count == 0 && improve.Count > 0)
            {
                kind = RecommendationKind.ImproveValues;
                headline = $"Improve {improve[0]}";
                targetStat = improve[0];
                summary = "All build affixes are present. Some are below your minimum or need to be greater.";
            }
            else if (missing.Count > 0)
            {
                kind = RecommendationKind.ReviewItem;
                headline = "No safe reroll";
                targetStat = missing[0].Name;
                summary = protectedGreater.Count > 0
                    ? $"The item is missing {missing[0].Name}, but the only affix to replace is a protected greater affix."
                    : $"The item is missing {missing[0].Name}, and nothing can be replaced without losing a build stat.";
            }
            else
            {
                kind = RecommendationKind.ReviewItem;
                headline = "Review the item";
                summary = "Some affixes need a closer look before you decide.";
            }

            return new CraftingRecommendation
            {
                Kind = kind,
                Headline = headline,
                TargetStat = targetStat,
                Summary = summary,
                Keep = keep,
                Protected = candidate == null || candidate.Scanned?.Kind != AffixKind.Greater
                    ? protectedGreater.Select(c => c.Name).ToList()
                    : Array.Empty<string>(),
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
