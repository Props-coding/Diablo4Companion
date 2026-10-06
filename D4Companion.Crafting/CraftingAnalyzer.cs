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
        /// <summary>True when the affix is kept out of suggestions because it is a greater affix.</summary>
        public bool IsProtected { get; init; }
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

    /// <summary>
    /// A concrete thing to do in the game: where, which operation, which prism, on which affix and why.
    /// It never claims an operation is allowed on this item, and never gives odds or costs.
    /// </summary>
    public sealed record CraftingInstruction
    {
        /// <summary>Where it is done, for example "Horadric Cube".</summary>
        public string Station { get; init; } = string.Empty;
        /// <summary>The operation as the game names it, for example "Focused Reroll".</summary>
        public string Operation { get; init; } = string.Empty;
        /// <summary>Tuning prism id to use, if one applies.</summary>
        public string PrismId { get; init; } = string.Empty;
        /// <summary>The affix the operation is aimed at, if any.</summary>
        public string AffectedAffix { get; init; } = string.Empty;
        /// <summary>The build stat you are hoping to end up with.</summary>
        public string DesiredAffix { get; init; } = string.Empty;
        /// <summary>Why this operation and this prism.</summary>
        public string Reason { get; init; } = string.Empty;
        /// <summary>What to confirm in the game before spending anything.</summary>
        public string Caveat { get; init; } = string.Empty;

        public string PrismName => PrismId.Length == 0 ? string.Empty : PrismNames.Name(PrismId);

        /// <summary>One line a player can follow, for example "Horadric Cube: Chaotic Reroll on Willpower with an Aggressive Tuning Prism".</summary>
        public string Line
        {
            get
            {
                string text = Station.Length > 0 && Operation.Length > 0 ? $"{Station}: {Operation}" : Station + Operation;
                if (AffectedAffix.Length > 0) text += $" on {AffectedAffix}";
                if (PrismId.Length > 0) text += $" with {PrismName}";
                return text;
            }
        }
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
        /// <summary>The exact operation to perform, when one can be named.</summary>
        public CraftingInstruction? Instruction { get; init; }
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
            // A tempered copy of an affix next to a regular one is legitimate, so tempered counts separately.
            var seen = new HashSet<(string, bool, bool)>();
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

                var key = (affix.AffixId.ToLowerInvariant(), affix.Kind == AffixKind.Implicit, affix.Kind == AffixKind.Tempered);
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
                    IsProtected = affix.Kind == AffixKind.Greater && !affix.IsKeep && !options.IncludeGreaterAffixes,
                    Explanation = affix.Kind == AffixKind.Implicit
                        ? "Implicit affix that is not part of your build target."
                        : affix.Kind == AffixKind.Greater && !affix.IsKeep && !options.IncludeGreaterAffixes
                            ? "Not in your build, but protected because it is a greater affix."
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

        /// <summary>
        /// Finds the affix that best fits this build line. Each scanned affix can only satisfy one line,
        /// so a build that wants the same stat twice (for example regular and tempered) needs two affixes on the item.
        /// </summary>
        private static int FindScanned(IReadOnlyList<ScannedAffix> affixes, bool[] used, TargetAffix target)
        {
            int best = -1;
            int bestScore = 0;
            for (int i = 0; i < affixes.Count; i++)
            {
                if (used[i]) continue;
                var affix = affixes[i];
                if (!string.Equals(affix.AffixId, target.AffixId, StringComparison.OrdinalIgnoreCase)) continue;
                if ((affix.Kind == AffixKind.Implicit) != target.IsImplicit) continue;

                int score = FitScore(target, affix);
                if (score > bestScore)
                {
                    best = i;
                    bestScore = score;
                }
            }
            return best;
        }

        // Higher is a better fit. A tempered build line prefers a tempered affix, a greater line prefers a greater affix.
        private static int FitScore(TargetAffix target, ScannedAffix affix)
        {
            if (target.IsTempered) return affix.Kind == AffixKind.Tempered ? 3 : 1;
            if (target.RequireGreater) return affix.Kind == AffixKind.Greater ? 3 : 1;
            return affix.Kind == AffixKind.Tempered ? 1 : 2;
        }

        /// <summary>"regular", "tempered", "greater" or "implicit": how the build asks for this stat.</summary>
        public static string TargetKind(TargetAffix target)
        {
            if (target.IsImplicit) return "implicit";
            if (target.IsTempered) return "tempered";
            if (target.RequireGreater) return "greater";
            return "regular";
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

            if (target.IsTempered && scanned.Kind != AffixKind.Tempered)
            {
                string has = scanned.Kind == AffixKind.Greater ? "a greater affix" : "a regular affix";
                return (AffixStatus.Review, $"Your build wants this as a tempered affix. The item has it as {has}. Tempering is done at the Blacksmith, not at the cube.");
            }

            return (AffixStatus.Match, $"Covers the {TargetKind(target)} {target.DisplayName} line in your build.");
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
            var missingRegular = missing.Where(c => c.Target is { IsTempered: false, IsImplicit: false }).ToList();
            var missingTempered = missing.Where(c => c.Target is { IsTempered: true }).ToList();
            var improveRows = comparisons.Where(c => c.Status is AffixStatus.BelowMinimum or AffixStatus.GreaterNeeded).ToList();
            var improve = improveRows.Select(c => c.Name).ToList();

            var offTarget = comparisons.Where(c => c.IsOffTarget && c.Scanned != null && !c.Scanned.IsKeep).ToList();
            var protectedGreater = offTarget.Where(c => c.Scanned!.Kind == AffixKind.Greater).ToList();
            var replaceable = offTarget.Where(c => c.Scanned!.Kind == AffixKind.Normal).ToList();
            if (options.IncludeGreaterAffixes) replaceable.AddRange(protectedGreater);

            var checks = new List<string>
            {
                "Confirm in-game that this operation is available for this item and which affixes it can change.",
                "Check the material cost in-game before you spend anything."
            };

            var prismHints = missing
                .Where(c => c.Target != null && c.Target.TuningPrisms.Count > 0)
                .Select(c => new PrismHint(c.Name, c.Target!.TuningPrisms))
                .ToList();
            if (prismHints.Count > 0) checks.Add(PrismDisclaimer);
            if (missing.Count > 1)
            {
                checks.Add($"This item is missing {missing.Count} build affixes. Work on one at a time and check the result after each step.");
            }

            // Pick the off-target affix to work on, and the missing stat to aim for.
            AffixComparison? goal = missingRegular.FirstOrDefault();
            AffixComparison? candidate = goal == null ? null : BestCandidate(replaceable, goal.Target!);

            CraftingInstruction? instruction = null;
            RecommendationKind kind;
            string headline;
            string targetStat = string.Empty;
            string summary;
            string candidateNote = string.Empty;
            int regularCount = comparisons.Count(c => c.Scanned != null && !c.IsDuplicate && c.Scanned.Kind is AffixKind.Normal or AffixKind.Greater);

            if (goal != null && candidate != null)
            {
                instruction = RerollInstruction(candidate, goal, comparisons);
                kind = RecommendationKind.ReviewReroll;
                headline = $"{instruction.Operation} {candidate.Name}";
                targetStat = goal.Name;
                summary = $"{candidate.Name} is not in your build. Aim for {goal.Name} instead.";
                if (candidate.Scanned!.Kind == AffixKind.Greater)
                {
                    candidateNote = "This is a greater affix. You chose to include greater affixes, but replacing it loses the greater bonus.";
                }
            }
            else if (goal != null && regularCount < StandardAffixSlots)
            {
                string prism = goal.Target!.TuningPrisms.FirstOrDefault() ?? string.Empty;
                instruction = new CraftingInstruction
                {
                    Station = "Horadric Cube",
                    Operation = "Add an Affix",
                    PrismId = prism,
                    DesiredAffix = goal.Name,
                    Reason = prism.Length > 0
                        ? $"The item has {regularCount} regular affixes, so there may be room for one more. The {PrismNames.Name(prism)} narrows the new affix to the group that includes {goal.Name}."
                        : $"The item has {regularCount} regular affixes, so there may be room for one more without losing anything.",
                    Caveat = "Check in-game that this item can take another affix. The new affix is not guaranteed to be the one you want."
                };
                kind = RecommendationKind.ReviewReroll;
                headline = "Add an affix";
                targetStat = goal.Name;
                summary = $"The item is missing {goal.Name} and seems to have a free affix slot.";
            }
            else if (goal != null)
            {
                kind = RecommendationKind.ReviewItem;
                targetStat = goal.Name;
                if (protectedGreater.Count > 0)
                {
                    headline = "Replacement candidates are protected";
                    summary = $"The item is missing {goal.Name}. The only affixes not in your build are greater, so they are protected.";
                    candidateNote = "Tick \"Include greater affixes in suggestions\" if you still want to consider replacing them.";
                }
                else
                {
                    headline = "Every affix is in use";
                    summary = $"The item is missing {goal.Name}, but replacing any affix would lose a build stat or one you marked Keep.";
                    candidateNote = offTarget.Count == 0 && comparisons.Any(c => c.IsOffTarget)
                        ? "Every off-target affix is marked Keep, tempered or implicit, so no replacement is suggested."
                        : string.Empty;
                }
            }
            else if (missingTempered.Count > 0)
            {
                var tempered = missingTempered[0];
                instruction = new CraftingInstruction
                {
                    Station = "Blacksmith",
                    Operation = "Temper",
                    DesiredAffix = tempered.Name,
                    Reason = $"Your build wants {tempered.Name} as a tempered affix. Tempering adds it without changing your other affixes.",
                    Caveat = $"Check that you have a tempering recipe that can give {tempered.Name}, and that the item has tempers left."
                };
                kind = RecommendationKind.ReviewReroll;
                headline = $"Temper for {tempered.Name}";
                targetStat = tempered.Name;
                summary = "All regular build affixes are present. Only a tempered one is missing.";
            }
            else if (improveRows.Count > 0)
            {
                var first = improveRows[0];
                kind = RecommendationKind.ImproveValues;
                targetStat = first.Name;
                if (first.Status == AffixStatus.BelowMinimum)
                {
                    instruction = new CraftingInstruction
                    {
                        Station = "Blacksmith",
                        Operation = "Masterwork",
                        DesiredAffix = first.Name,
                        Reason = $"{first.Name} is on the item but below your minimum. Masterworking raises affix values without replacing them.",
                        Caveat = "Check in-game how far this item can still be masterworked."
                    };
                    headline = $"Masterwork for {first.Name}";
                    summary = "All build affixes are present. Some values are below your minimum.";
                }
                else
                {
                    headline = $"Look for a greater {first.Name}";
                    summary = $"Your build wants a greater {first.Name}. Crafting cannot make an existing affix greater, so this usually comes from a better drop.";
                }
            }
            else
            {
                kind = RecommendationKind.ReviewItem;
                headline = "Review the item";
                summary = "Some affixes need a closer look before you decide.";
            }

            if (comparisons.Any(c => c.IsOffTarget && c.Scanned?.Kind == AffixKind.Tempered))
            {
                checks.Add("Tempered affixes are changed by tempering at the Blacksmith, not at the cube.");
            }

            return new CraftingRecommendation
            {
                Kind = kind,
                Headline = headline,
                TargetStat = targetStat,
                Summary = summary,
                Instruction = instruction,
                Keep = keep,
                Protected = candidate?.Scanned?.Kind == AffixKind.Greater
                    ? Array.Empty<string>()
                    : protectedGreater.Select(c => c.Name).ToList(),
                ReplaceCandidate = candidate?.Scanned,
                ReplaceCandidateNote = candidateNote,
                MissingTargets = missing.Select(c => c.Target is { IsTempered: true } ? $"{c.Name} (tempered)" : c.Name).ToList(),
                ImproveTargets = improve,
                PrismHints = prismHints,
                InGameChecks = checks
            };
        }

        /// <summary>Standard gear carries up to this many regular affixes.</summary>
        public const int StandardAffixSlots = 4;

        // Prefer a regular affix that shares a prism group with the goal, so a Focused Reroll can be used.
        private static AffixComparison? BestCandidate(IReadOnlyList<AffixComparison> replaceable, TargetAffix goal)
        {
            return replaceable
                .OrderBy(c => c.Scanned!.Kind == AffixKind.Greater ? 1 : 0)
                .ThenBy(c => SharedPrism(c.Scanned!, goal).Length > 0 ? 0 : 1)
                .FirstOrDefault();
        }

        private static string SharedPrism(ScannedAffix affix, TargetAffix goal) =>
            goal.TuningPrisms.FirstOrDefault(p => affix.TuningPrisms.Contains(p, StringComparer.OrdinalIgnoreCase)) ?? string.Empty;

        private static CraftingInstruction RerollInstruction(AffixComparison candidate, AffixComparison goal, IReadOnlyList<AffixComparison> comparisons)
        {
            var scanned = candidate.Scanned!;
            var target = goal.Target!;
            string shared = SharedPrism(scanned, target);
            string goalPrism = target.TuningPrisms.FirstOrDefault() ?? string.Empty;

            if (shared.Length > 0)
            {
                // Other affixes in the same prism group could be the ones that change.
                var atRisk = comparisons
                    .Where(c => c.Scanned != null && !ReferenceEquals(c, candidate) && !c.IsDuplicate
                                && c.Scanned.Kind is AffixKind.Normal or AffixKind.Greater
                                && c.Scanned.TuningPrisms.Contains(shared, StringComparer.OrdinalIgnoreCase))
                    .Select(c => c.Name)
                    .Distinct()
                    .ToList();

                if (atRisk.Count == 0)
                {
                    return new CraftingInstruction
                    {
                        Station = "Horadric Cube",
                        Operation = "Focused Reroll",
                        PrismId = shared,
                        AffectedAffix = candidate.Name,
                        DesiredAffix = goal.Name,
                        Reason = $"{candidate.Name} and {goal.Name} are in the same prism group, and {candidate.Name} is the only affix on this item in that group. A Focused Reroll with this prism keeps the result in that group.",
                        Caveat = "The result is not guaranteed to be " + goal.Name + ". Confirm in-game which affix the reroll will change."
                    };
                }

                return new CraftingInstruction
                {
                    Station = "Occultist",
                    Operation = "Enchant",
                    AffectedAffix = candidate.Name,
                    DesiredAffix = goal.Name,
                    Reason = $"{candidate.Name} is not in your build. A cube reroll in this prism group could also hit {JoinNames(atRisk)}, so enchanting is safer: it changes only the affix you pick, and you can keep the original if the options are worse.",
                    Caveat = "Only one affix per item can be enchanted. If another affix was already enchanted, use that one or choose another step."
                };
            }

            return new CraftingInstruction
            {
                Station = "Occultist",
                Operation = "Enchant",
                AffectedAffix = candidate.Name,
                DesiredAffix = goal.Name,
                Reason = goalPrism.Length > 0
                    ? $"{candidate.Name} is not in your build and is in a different prism group from {goal.Name}. Enchanting changes only the affix you pick, and you can keep the original if the options are worse."
                    : $"{candidate.Name} is not in your build. Enchanting changes only the affix you pick, and you can keep the original if the options are worse.",
                Caveat = $"{goal.Name} may not be among the options. Only one affix per item can be enchanted."
            };
        }

        private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1]
        };
    }
}
