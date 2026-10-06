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
        /// <summary>Id of the affix the operation is aimed at, if any.</summary>
        public string AffectedAffixId { get; init; } = string.Empty;
        /// <summary>The build stat you are hoping to end up with.</summary>
        public string DesiredAffix { get; init; } = string.Empty;
        /// <summary>Why this operation and this prism.</summary>
        public string Reason { get; init; } = string.Empty;
        /// <summary>What to confirm in the game before spending anything.</summary>
        public string Caveat { get; init; } = string.Empty;
        /// <summary>The build line this step works towards. Used by the planner.</summary>
        public TargetAffix? Goal { get; init; }
        /// <summary>What the app checked, and where the answer came from (game data or your scan).</summary>
        public IReadOnlyList<string> Checked { get; init; } = Array.Empty<string>();
        /// <summary>What only the game can tell you. Check these before spending anything.</summary>
        public IReadOnlyList<string> ConfirmInGame { get; init; } = Array.Empty<string>();
        /// <summary>The check in ConfirmInGame about whether the goal can exist on this item at all. A "no" rules out the goal, not just this step.</summary>
        public string GoalCheck { get; init; } = string.Empty;

        /// <summary>Identifies this step, so the player can rule it out after checking in-game.</summary>
        public string Key => RuledOutStep.StepKey(Operation, AffectedAffixId, Goal?.AffixId ?? string.Empty);

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
        /// <summary>"4 of 4 target affixes on the item". Counts presence only.</summary>
        public string AffixesStatement { get; init; } = string.Empty;
        /// <summary>Whether the minimum values your build sets are met.</summary>
        public string ValuesStatement { get; init; } = string.Empty;
        /// <summary>Whether the greater affixes your build asks for are present.</summary>
        public string GreaterStatement { get; init; } = string.Empty;
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
            var completion = Completion(comparisons, targets.Count);

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
                    BuildMatchStatement = $"{AllTargets(targets.Count)} matched.",
                    AffixesStatement = completion.Affixes,
                    ValuesStatement = completion.Values,
                    GreaterStatement = completion.Greater,
                    Recommendation = new CraftingRecommendation
                    {
                        Kind = RecommendationKind.KeepItem,
                        Headline = "Keep this item",
                        Summary = $"{AllTargets(targets.Count)} matched. No crafting step is needed for your build.",
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
                AffixesStatement = completion.Affixes,
                ValuesStatement = completion.Values,
                GreaterStatement = completion.Greater,
                Recommendation = BuildNextStep(comparisons, options, snapshot)
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

        private static CraftingRecommendation BuildNextStep(IReadOnlyList<AffixComparison> comparisons, AdvisorOptions options, GearSnapshot snapshot)
        {
            var state = snapshot.CraftState ?? new ItemCraftState();
            string slot = SlotWord(snapshot.ItemType);

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
            if (comparisons.Any(c => c.IsOffTarget && c.Scanned?.Kind == AffixKind.Tempered))
            {
                checks.Add("Tempered affixes are changed by tempering at the Blacksmith, not at the cube.");
            }

            // Goals the player found can't roll on this item are left out. If any are, the item can't reach the build.
            var blockedGoals = missingRegular.Where(c => state.IsRuledOut(RuledOutStep.GoalKey(c.Target!.AffixId))).ToList();
            AffixComparison? goal = missingRegular.Except(blockedGoals).FirstOrDefault();
            AffixComparison? candidate = goal == null ? null : BestCandidate(replaceable, goal.Target!, state);

            CraftingInstruction? instruction = null;
            RecommendationKind kind = RecommendationKind.ReviewItem;
            string headline;
            string targetStat = string.Empty;
            string summary;
            string candidateNote = string.Empty;
            int regularCount = comparisons.Count(c => c.Scanned != null && !c.IsDuplicate && c.Scanned.Kind is AffixKind.Normal or AffixKind.Greater);
            var firstImprove = improveRows.FirstOrDefault();

            if (state.CannotBeModified == true)
            {
                targetStat = (goal ?? missingTempered.FirstOrDefault() ?? firstImprove)?.Name ?? string.Empty;
                headline = "This item can't be changed";
                summary = "You marked this item as no longer modifiable, so no crafting step is suggested. Look for another base item.";
            }
            else if (goal != null && candidate != null && RerollInstruction(candidate, goal, comparisons, state, slot) is CraftingInstruction reroll)
            {
                instruction = reroll;
                kind = RecommendationKind.ReviewReroll;
                headline = $"{reroll.Operation} {candidate.Name}";
                targetStat = goal.Name;
                summary = $"{candidate.Name} is not in your build. Aim for {goal.Name} instead.";
                if (candidate.Scanned!.Kind == AffixKind.Greater)
                {
                    candidateNote = "This is a greater affix. You chose to include greater affixes, but replacing it loses the greater bonus.";
                }
            }
            else if (blockedGoals.Count > 0)
            {
                targetStat = blockedGoals[0].Name;
                headline = "This item can't reach your build";
                summary = $"You found in-game that {JoinNames(blockedGoals.Select(c => c.Name).ToList())} can't be added to this {slot}. Look for another base item before spending more on this one.";
            }
            else if (goal != null && regularCount < StandardAffixSlots
                     && !state.IsRuledOut(RuledOutStep.StepKey("Add an Affix", string.Empty, goal.Target!.AffixId)))
            {
                string prism = goal.Target!.TuningPrisms.FirstOrDefault() ?? string.Empty;
                var verified = new List<string> { $"Your scan shows {regularCount} regular affixes on this {slot}." };
                if (prism.Length > 0) verified.Add($"{goal.Name} is in the {PrismNames.Name(prism)} group (game data).");
                instruction = new CraftingInstruction
                {
                    Station = "Horadric Cube",
                    Operation = "Add an Affix",
                    PrismId = prism,
                    DesiredAffix = goal.Name,
                    Goal = goal.Target,
                    Reason = prism.Length > 0
                        ? $"The item has {regularCount} regular affixes, so there may be room for one more. The {PrismNames.Name(prism)} narrows the new affix to the group that includes {goal.Name}."
                        : $"The item has {regularCount} regular affixes, so there may be room for one more without losing anything.",
                    Caveat = "The new affix is not guaranteed to be " + goal.Name + ".",
                    Checked = verified,
                    ConfirmInGame = new[]
                    {
                        "The cube offers Add an Affix for this item.",
                        $"{goal.Name} can roll on a {slot}."
                    },
                    GoalCheck = $"{goal.Name} can roll on a {slot}."
                };
                kind = RecommendationKind.ReviewReroll;
                headline = "Add an affix";
                targetStat = goal.Name;
                summary = $"The item is missing {goal.Name} and seems to have a free affix slot.";
            }
            else if (goal != null)
            {
                targetStat = goal.Name;
                if (candidate != null)
                {
                    headline = "No safe step left";
                    summary = state.RuledOut.Count > 0
                        ? $"The steps that could change {candidate.Name} into {goal.Name} were ruled out in-game or aren't safe. Decide in-game, or look for another base item."
                        : $"Changing {candidate.Name} into {goal.Name} would need the Occultist, but this item's enchant is already used on another affix. Decide in-game.";
                }
                else if (protectedGreater.Count > 0)
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
                targetStat = tempered.Name;
                if (state.NoTempersLeft == true)
                {
                    headline = "No tempers left";
                    summary = $"Your build wants {tempered.Name} as a tempered affix, but you marked this item as having no tempers left.";
                }
                else if (state.IsRuledOut(RuledOutStep.StepKey("Temper", string.Empty, tempered.Target!.AffixId)))
                {
                    headline = "Tempering ruled out";
                    summary = $"You found in-game that tempering can't give {tempered.Name} on this item. Look for another base item, or check the build guide.";
                }
                else if (tempered.Target!.CanBeTempered == false)
                {
                    headline = "Not a tempering affix";
                    summary = $"Your build lists {tempered.Name} as tempered, but the game data doesn't list it as a tempering affix. Check the build guide.";
                }
                else
                {
                    var verified = new List<string>();
                    if (tempered.Target.CanBeTempered == true) verified.Add($"{tempered.Name} is a tempering affix (game data).");
                    var confirm = new List<string> { $"You have a tempering recipe that can give {tempered.Name}." };
                    if (state.NoTempersLeft == false) verified.Add("You checked that the item still has tempers left.");
                    else confirm.Add(CheckTempersLeft);
                    instruction = new CraftingInstruction
                    {
                        Station = "Blacksmith",
                        Operation = "Temper",
                        DesiredAffix = tempered.Name,
                        Goal = tempered.Target,
                        Reason = $"Your build wants {tempered.Name} as a tempered affix. Tempering adds it without changing your other affixes.",
                        Caveat = $"A tempering recipe can give several results, so {tempered.Name} is not guaranteed.",
                        Checked = verified,
                        ConfirmInGame = confirm
                    };
                    kind = RecommendationKind.ReviewReroll;
                    headline = $"Temper for {tempered.Name}";
                    summary = "All regular build affixes are present. Only a tempered one is missing.";
                }
            }
            else if (firstImprove != null)
            {
                kind = RecommendationKind.ImproveValues;
                targetStat = firstImprove.Name;
                if (firstImprove.Status == AffixStatus.GreaterNeeded)
                {
                    headline = $"Look for a greater {firstImprove.Name}";
                    summary = $"Your build wants a greater {firstImprove.Name}. Crafting cannot make an existing affix greater, so this usually comes from a better drop.";
                }
                else if (state.FullyMasterworked == true || state.IsRuledOut(RuledOutStep.StepKey("Masterwork", string.Empty, firstImprove.Target?.AffixId ?? string.Empty)))
                {
                    headline = $"{firstImprove.Name} is below your minimum";
                    summary = state.FullyMasterworked == true
                        ? "You marked this item as fully masterworked, so its values can't be raised further here."
                        : "You found in-game that masterworking isn't possible on this item, so its values can't be raised further here.";
                }
                else
                {
                    instruction = new CraftingInstruction
                    {
                        Station = "Blacksmith",
                        Operation = "Masterwork",
                        DesiredAffix = firstImprove.Name,
                        Goal = firstImprove.Target,
                        Reason = $"{firstImprove.Name} is on the item but below your minimum. Masterworking raises affix values without replacing them.",
                        Caveat = "Masterworking may not raise this value enough to reach your minimum.",
                        Checked = state.FullyMasterworked == false
                            ? new[] { $"Your scan shows {firstImprove.Name} below the minimum your build sets.", "You checked that the item has masterwork ranks left." }
                            : new[] { $"Your scan shows {firstImprove.Name} below the minimum your build sets." },
                        ConfirmInGame = state.FullyMasterworked == false ? Array.Empty<string>() : new[] { CheckMasterworkLeft }
                    };
                    headline = $"Masterwork for {firstImprove.Name}";
                    summary = "All build affixes are present. Some values are below your minimum.";
                }
            }
            else
            {
                headline = "Review the item";
                summary = "Some affixes need a closer look before you decide.";
            }

            // Whether the item can still be changed at all applies to every step.
            if (instruction != null)
            {
                instruction = state.CannotBeModified == false
                    ? instruction with { Checked = instruction.Checked.Append("You checked that the item can still be changed.").ToList() }
                    : instruction with { ConfirmInGame = instruction.ConfirmInGame.Prepend(CheckCanChange).ToList() };
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
                ReplaceCandidate = instruction == null ? null : candidate?.Scanned,
                ReplaceCandidateNote = candidateNote,
                MissingTargets = missing.Select(c => c.Target is { IsTempered: true } ? $"{c.Name} (tempered)" : c.Name).ToList(),
                ImproveTargets = improve,
                PrismHints = prismHints,
                InGameChecks = checks
            };
        }

        // In-game checks about the item's crafting limits. A "no" answer sets the matching limit.
        public const string CheckCanChange = "The item can still be changed.";
        public const string CheckTempersLeft = "The item still has tempers left.";
        public const string CheckMasterworkLeft = "The item has masterwork ranks left.";
        public const string CheckNoEnchantYet = "No other affix on this item has been enchanted yet.";

        /// <summary>Standard gear carries up to this many regular affixes.</summary>
        public const int StandardAffixSlots = 4;

        private sealed record CompletionStatements(string Affixes, string Values, string Greater);

        // Kept separate on purpose: having the right affixes is not the same as meeting every requirement.
        private static CompletionStatements Completion(IReadOnlyList<AffixComparison> comparisons, int targetCount)
        {
            var targeted = comparisons.Where(c => c.Target != null).ToList();
            int present = targeted.Count(c => c.Scanned != null);

            var withMinimum = targeted.Where(c => c.Target!.MinimumValue is > 0).ToList();
            int valuesMet = withMinimum.Count(c => c.Scanned != null && c.Status != AffixStatus.BelowMinimum
                                                   && c.Scanned.Value is double v && v >= c.Target!.MinimumValue!.Value);
            var withGreater = targeted.Where(c => c.Target!.RequireGreater).ToList();
            int greaterMet = withGreater.Count(c => c.Scanned?.Kind == AffixKind.Greater);

            string affixes = present == targetCount
                ? (targetCount == 1 ? "The target affix is on the item." : $"All {targetCount} target affixes are on the item.")
                : $"{present} of {targetCount} target affixes are on the item.";
            string values = withMinimum.Count == 0
                ? "Minimum values: none set in your build."
                : valuesMet == withMinimum.Count
                    ? $"Minimum values: all {withMinimum.Count} met."
                    : $"Minimum values: {valuesMet} of {withMinimum.Count} met.";
            string greater = withGreater.Count == 0
                ? "Greater affixes: none required by your build."
                : greaterMet == withGreater.Count
                    ? $"Greater affixes: all {withGreater.Count} required are greater."
                    : $"Greater affixes: {greaterMet} of {withGreater.Count} required are greater.";
            return new CompletionStatements(affixes, values, greater);
        }

        /// <summary>"All 4 target affixes", or "The target affix" when there is only one.</summary>
        public static string AllTargets(int count) => count == 1 ? "The target affix" : $"All {count} target affixes";

        private static string SlotWord(string itemType) => string.IsNullOrWhiteSpace(itemType) ? "item" : itemType.Trim().ToLowerInvariant();

        // The enchanted affix comes first: it is the only one the Occultist can still change.
        // Then regular affixes before greater ones, and ones that share a prism group with the goal.
        private static AffixComparison? BestCandidate(IReadOnlyList<AffixComparison> replaceable, TargetAffix goal, ItemCraftState state)
        {
            return replaceable
                .OrderBy(c => state.HasEnchant && IsEnchanted(c.Scanned!, state) ? 0 : 1)
                .ThenBy(c => c.Scanned!.Kind == AffixKind.Greater ? 1 : 0)
                .ThenBy(c => SharedPrism(c.Scanned!, goal).Length > 0 ? 0 : 1)
                .FirstOrDefault();
        }

        private static bool IsEnchanted(ScannedAffix affix, ItemCraftState state) =>
            string.Equals(affix.AffixId, state.EnchantedAffixId, StringComparison.OrdinalIgnoreCase);

        private static string SharedPrism(ScannedAffix affix, TargetAffix goal) =>
            goal.TuningPrisms.FirstOrDefault(p => affix.TuningPrisms.Contains(p, StringComparer.OrdinalIgnoreCase)) ?? string.Empty;

        private static CraftingInstruction? RerollInstruction(AffixComparison candidate, AffixComparison goal, IReadOnlyList<AffixComparison> comparisons,
            ItemCraftState state, string slot)
        {
            var scanned = candidate.Scanned!;
            var target = goal.Target!;
            string shared = SharedPrism(scanned, target);
            bool enchantAllowed = (!state.HasEnchant || IsEnchanted(scanned, state))
                                  && !state.IsRuledOut(RuledOutStep.StepKey("Enchant", scanned.AffixId, target.AffixId));
            bool focusedAllowed = !state.IsRuledOut(RuledOutStep.StepKey("Focused Reroll", scanned.AffixId, target.AffixId));
            if (state.IsRuledOut(RuledOutStep.GoalKey(target.AffixId))) return null;

            // Other affixes in the same prism group could be the ones that change.
            var atRisk = shared.Length == 0
                ? new List<string>()
                : comparisons
                    .Where(c => c.Scanned != null && !ReferenceEquals(c, candidate) && !c.IsDuplicate
                                && c.Scanned.Kind is AffixKind.Normal or AffixKind.Greater
                                && c.Scanned.TuningPrisms.Contains(shared, StringComparer.OrdinalIgnoreCase))
                    .Select(c => c.Name)
                    .Distinct()
                    .ToList();

            string slotCheck = $"{goal.Name} can roll on a {slot}.";

            if (shared.Length > 0 && focusedAllowed && (atRisk.Count == 0 || !enchantAllowed))
            {
                var verified = new List<string>
                {
                    $"{goal.Name} is in the {PrismNames.Name(shared)} group (game data).",
                    $"{candidate.Name} is in the same group (game data)."
                };
                if (atRisk.Count == 0) verified.Add($"No other affix on this {slot} is in that group (your scan).");

                return new CraftingInstruction
                {
                    Station = "Horadric Cube",
                    Operation = "Focused Reroll",
                    PrismId = shared,
                    AffectedAffix = candidate.Name,
                    DesiredAffix = goal.Name,
                    Goal = target,
                    AffectedAffixId = scanned.AffixId,
                    GoalCheck = slotCheck,
                    Reason = atRisk.Count == 0
                        ? $"{candidate.Name} and {goal.Name} are in the same prism group, and {candidate.Name} is the only affix on this item in that group. A Focused Reroll with this prism keeps the result in that group."
                        : $"The Occultist can't be used for this (its enchant is used elsewhere or was ruled out), so the cube is the remaining option. {candidate.Name} and {goal.Name} share this prism group.",
                    Caveat = atRisk.Count == 0
                        ? $"Sharing a group doesn't guarantee {goal.Name}. The reroll can land on any affix in the group."
                        : $"Risky: the reroll could change {JoinNames(atRisk)} instead of {candidate.Name}.",
                    Checked = verified,
                    ConfirmInGame = new[]
                    {
                        "The cube offers Focused Reroll for this item.",
                        $"The reroll will change {candidate.Name}, not another affix.",
                        slotCheck
                    }
                };
            }

            if (!enchantAllowed) return null;

            var enchantChecked = new List<string>();
            var enchantConfirm = new List<string> { $"The Occultist lets you enchant {candidate.Name} on this item." };
            if (state.HasEnchant) enchantChecked.Add($"{candidate.Name} is the affix you marked as enchanted, so the Occultist can still change it.");
            else if (state.EnchantChecked) enchantChecked.Add("You checked that no affix on this item is enchanted yet.");
            else enchantConfirm.Add(CheckNoEnchantYet);
            enchantConfirm.Add(slotCheck);
            if (shared.Length == 0 && target.TuningPrisms.Count > 0)
            {
                enchantChecked.Add($"{candidate.Name} is not in the {PrismNames.Name(target.TuningPrisms[0])} group, so a Focused Reroll can't aim for {goal.Name} (game data).");
            }

            return new CraftingInstruction
            {
                Station = "Occultist",
                Operation = "Enchant",
                AffectedAffix = candidate.Name,
                DesiredAffix = goal.Name,
                Goal = target,
                AffectedAffixId = scanned.AffixId,
                GoalCheck = slotCheck,
                Reason = atRisk.Count > 0
                    ? $"{candidate.Name} is not in your build. A cube reroll in this prism group could also hit {JoinNames(atRisk)}, so enchanting is safer: it changes only the affix you pick, and you can keep the original if the options are worse."
                    : $"{candidate.Name} is not in your build. Enchanting changes only the affix you pick, and you can keep the original if the options are worse.",
                Caveat = $"{goal.Name} may not be among the options. If it isn't, keep the original. Only one affix per item can be enchanted.",
                Checked = enchantChecked,
                ConfirmInGame = enchantConfirm
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
