namespace D4Companion.Crafting
{
    /// <summary>One step of a crafting plan, and what the item would look like if it works.</summary>
    public sealed record PlanStep(int Number, CraftingInstruction Instruction, string IfItWorks);

    public sealed record CraftingPlan
    {
        public IReadOnlyList<PlanStep> Steps { get; init; } = Array.Empty<PlanStep>();
        /// <summary>What happens after the last step: the item is done, or why the plan stops.</summary>
        public string EndNote { get; init; } = string.Empty;
        public bool HasSteps => Steps.Count > 0;
    }

    /// <summary>
    /// Builds a full plan for one item by repeating the advisor's next step, each time assuming the previous step
    /// gave the stat you aimed for. Real results vary, so the player rescans after every step and the plan is redone.
    /// </summary>
    public static class CraftingPlanner
    {
        public const int MaxSteps = 8;

        public const string PlanDisclaimer =
            "Each step assumes the one before it worked. Rescan after every step; the plan updates from the real result.";

        public static CraftingPlan Plan(GearSnapshot snapshot, BuildTarget? build, AdvisorOptions? options = null)
        {
            options ??= new AdvisorOptions();
            var steps = new List<PlanStep>();
            var item = snapshot;
            CraftingAnalysis analysis = CraftingAnalyzer.Analyze(item, build, options);

            for (int i = 0; i < MaxSteps; i++)
            {
                var step = analysis.Recommendation.Instruction;
                if (step == null) break;

                var next = Apply(item, step, analysis);
                if (next == null) break;

                // The Occultist enchants one affix per item. After this step, only that affix can be enchanted again.
                if (step.Operation == "Enchant" && step.Goal != null)
                {
                    next = next with { CraftState = next.CraftState with { EnchantedAffixId = step.Goal.AffixId } };
                }
                item = next;
                analysis = CraftingAnalyzer.Analyze(item, build, options);
                steps.Add(new PlanStep(steps.Count + 1, step, $"Matches {analysis.MatchCount} of {analysis.TargetCount} build affixes."));
            }

            string end = analysis.Verdict switch
            {
                CraftingVerdict.MeetsTarget => steps.Count == 0
                    ? $"{CraftingAnalyzer.AllTargets(analysis.TargetCount)} already matched."
                    : $"If every step works: {CraftingAnalyzer.AllTargets(analysis.TargetCount).ToLowerInvariant()} matched.",
                CraftingVerdict.NeedsWork when steps.Count == 0 => analysis.Recommendation.Summary,
                CraftingVerdict.NeedsWork => $"After these steps: {analysis.Recommendation.Headline}. {analysis.Recommendation.Summary}",
                _ => string.Empty
            };

            return new CraftingPlan { Steps = steps, EndNote = end };
        }

        // The item as it would be if the step gave the stat it aimed for. Null when the step can't be pictured.
        private static GearSnapshot? Apply(GearSnapshot item, CraftingInstruction step, CraftingAnalysis analysis)
        {
            var goal = step.Goal;
            var affixes = item.Affixes.ToList();

            switch (step.Operation)
            {
                case "Focused Reroll":
                case "Enchant":
                {
                    var replaced = analysis.Recommendation.ReplaceCandidate;
                    int index = replaced == null ? -1 : affixes.FindIndex(a => ReferenceEquals(a, replaced));
                    if (index < 0 || goal == null) return null;
                    affixes[index] = NewAffix(goal, AffixKind.Normal);
                    break;
                }
                case "Add an Affix":
                    if (goal == null) return null;
                    affixes.Add(NewAffix(goal, AffixKind.Normal));
                    break;
                case "Temper":
                    if (goal == null) return null;
                    affixes.Add(NewAffix(goal, AffixKind.Tempered));
                    break;
                case "Masterwork":
                {
                    if (goal == null) return null;
                    var row = analysis.Comparisons.FirstOrDefault(c => ReferenceEquals(c.Target, goal) && c.ScannedIndex != null);
                    if (row?.ScannedIndex is not int index) return null;
                    affixes[index] = affixes[index] with { Value = goal.MinimumValue };
                    break;
                }
                default:
                    return null;
            }

            return item with { Affixes = affixes };
        }

        private static ScannedAffix NewAffix(TargetAffix goal, AffixKind kind) => new()
        {
            AffixId = goal.AffixId,
            DisplayName = goal.DisplayName,
            Kind = kind,
            Value = goal.MinimumValue,
            TuningPrisms = goal.TuningPrisms
        };
    }
}
