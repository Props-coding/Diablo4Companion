using System.Globalization;
using System.Text;

namespace D4Companion.Crafting
{
    public enum TrialOutcome
    {
        /// <summary>The goal affix is now on the item, or the value now meets the minimum.</summary>
        Worked,
        /// <summary>The aimed-at affix changed, but not into the goal.</summary>
        DifferentResult,
        /// <summary>Nothing changed on the aimed-at affix, for example the original was kept at the Occultist.</summary>
        KeptOriginal,
        /// <summary>Something other than the aimed-at affix changed.</summary>
        OtherAffixChanged,
        /// <summary>The step wasn't possible in-game.</summary>
        Blocked
    }

    /// <summary>
    /// Records what a crafting step was meant to do and what actually happened, so test runs on
    /// spare items can be reviewed afterwards. Judged only from the before and after scans.
    /// </summary>
    public static class CraftTrial
    {
        public static string Attempt(CraftingInstruction step) =>
            step.DesiredAffix.Length > 0 ? $"{step.Line}, aiming for {step.DesiredAffix}" : step.Line;

        public static TrialOutcome Judge(CraftingInstruction step, GearSnapshot before, GearSnapshot after)
        {
            string goalId = step.Goal?.AffixId ?? string.Empty;

            if (step.Operation == "Masterwork")
            {
                var now = Find(after, goalId);
                double? minimum = step.Goal?.MinimumValue;
                if (now?.Value is double v && minimum is double m && v >= m) return TrialOutcome.Worked;
                return Find(before, goalId)?.Value != now?.Value ? TrialOutcome.DifferentResult : TrialOutcome.OtherAffixChanged;
            }

            if (step.Operation == "Temper")
            {
                bool hadIt = before.Affixes.Any(a => Same(a.AffixId, goalId) && a.Kind == AffixKind.Tempered);
                bool hasIt = after.Affixes.Any(a => Same(a.AffixId, goalId) && a.Kind == AffixKind.Tempered);
                if (hasIt && !hadIt) return TrialOutcome.Worked;
                return after.Affixes.Count(a => a.Kind == AffixKind.Tempered) > before.Affixes.Count(a => a.Kind == AffixKind.Tempered)
                    ? TrialOutcome.DifferentResult
                    : TrialOutcome.OtherAffixChanged;
            }

            if (Find(after, goalId) != null && Find(before, goalId) == null) return TrialOutcome.Worked;

            if (step.Operation == "Add an Affix")
            {
                return after.Affixes.Count > before.Affixes.Count ? TrialOutcome.DifferentResult : TrialOutcome.OtherAffixChanged;
            }

            // Rerolls and enchants aim at one affix.
            bool affectedGone = step.AffectedAffixId.Length > 0 && Find(after, step.AffectedAffixId) == null;
            if (affectedGone) return TrialOutcome.DifferentResult;
            bool othersSame = before.Affixes.Count == after.Affixes.Count
                              && before.Affixes.All(b => after.Affixes.Any(a => Same(a.AffixId, b.AffixId) && a.Value == b.Value && a.Kind == b.Kind));
            return othersSame ? TrialOutcome.KeptOriginal : TrialOutcome.OtherAffixChanged;
        }

        public static string Describe(TrialOutcome outcome, CraftingInstruction step) => outcome switch
        {
            TrialOutcome.Worked => step.Operation == "Masterwork"
                ? $"Worked: {step.DesiredAffix} now meets your minimum."
                : $"Worked: {step.DesiredAffix} is now on the item.",
            TrialOutcome.DifferentResult => step.Operation switch
            {
                "Masterwork" => $"Raised, but {step.DesiredAffix} is still below your minimum.",
                "Temper" => "Didn't land: tempering gave a different affix.",
                "Add an Affix" => "Didn't land: a different affix was added.",
                _ => $"Didn't land: {step.AffectedAffix} changed, but not into {step.DesiredAffix}."
            },
            TrialOutcome.KeptOriginal => step.AffectedAffix.Length > 0
                ? $"No change kept: {step.AffectedAffix} is still on the item."
                : "No change kept.",
            TrialOutcome.OtherAffixChanged => "Unexpected: something other than the aimed-at affix changed.",
            _ => "Not possible in-game."
        };

        /// <summary>A plain-text log of every saved version of an item: what was tried and what happened.</summary>
        public static string FormatLog(GearRecord record, string itemName, Func<string, string> nameLookup)
        {
            var text = new StringBuilder();
            text.AppendLine($"Trial log: {itemName}");
            int number = 1;
            foreach (var revision in record.Revisions)
            {
                text.AppendLine();
                text.AppendLine($"{number++}. {revision.Label} · {revision.SavedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}");
                if (revision.Attempt.Length > 0) text.AppendLine($"   Tried: {revision.Attempt}");
                if (revision.Outcome.Length > 0) text.AppendLine($"   Result: {revision.Outcome}");
                var affixes = revision.Snapshot.Affixes.Select(a =>
                {
                    string name = nameLookup(a.AffixId);
                    if (string.IsNullOrWhiteSpace(name)) name = string.IsNullOrWhiteSpace(a.DisplayName) ? "Unknown affix" : a.DisplayName;
                    string value = a.Value?.ToString("0.###", CultureInfo.CurrentCulture) ?? "?";
                    string kind = a.Kind is AffixKind.Normal ? string.Empty : $" ({a.Kind.ToString().ToLowerInvariant()})";
                    return $"{name} {value}{kind}";
                });
                text.AppendLine($"   Affixes: {string.Join(", ", affixes)}");
            }
            return text.ToString();
        }

        private static ScannedAffix? Find(GearSnapshot snapshot, string affixId) =>
            affixId.Length == 0 ? null : snapshot.Affixes.FirstOrDefault(a => Same(a.AffixId, affixId));

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
