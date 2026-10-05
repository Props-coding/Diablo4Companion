namespace D4Companion.Crafting
{
    public enum AffixChange
    {
        Unchanged,
        Added,
        Removed,
        ValueChanged,
        KindChanged
    }

    public sealed record AffixDiff(string Name, AffixChange Change, ScannedAffix? Before, ScannedAffix? After)
    {
        public string Description => Change switch
        {
            AffixChange.Added => "Added",
            AffixChange.Removed => "Removed",
            AffixChange.ValueChanged => $"{Format(Before?.Value)} to {Format(After?.Value)}",
            AffixChange.KindChanged => $"{Before?.Kind} to {After?.Kind}",
            _ => "No change"
        };

        private static string Format(double? value) => value.HasValue ? value.Value.ToString("0.##") : "?";
    }

    /// <summary>
    /// Shows what changed between two versions of the same item.
    /// </summary>
    public static class SnapshotComparer
    {
        public static IReadOnlyList<AffixDiff> Compare(GearSnapshot before, GearSnapshot after)
        {
            var result = new List<AffixDiff>();
            var remaining = after.Affixes.ToList();

            foreach (var old in before.Affixes)
            {
                int index = remaining.FindIndex(a => SameAffix(a, old));
                if (index < 0)
                {
                    result.Add(new AffixDiff(NameOf(old), AffixChange.Removed, old, null));
                    continue;
                }

                var current = remaining[index];
                remaining.RemoveAt(index);

                AffixChange change =
                    current.Kind != old.Kind ? AffixChange.KindChanged :
                    !Nullable.Equals(current.Value, old.Value) ? AffixChange.ValueChanged :
                    AffixChange.Unchanged;
                result.Add(new AffixDiff(NameOf(current), change, old, current));
            }

            foreach (var added in remaining)
            {
                result.Add(new AffixDiff(NameOf(added), AffixChange.Added, null, added));
            }

            return result;
        }

        private static bool SameAffix(ScannedAffix a, ScannedAffix b)
        {
            if (string.IsNullOrWhiteSpace(a.AffixId) || string.IsNullOrWhiteSpace(b.AffixId)) return false;
            return string.Equals(a.AffixId, b.AffixId, StringComparison.OrdinalIgnoreCase)
                && (a.Kind == AffixKind.Implicit) == (b.Kind == AffixKind.Implicit);
        }

        private static string NameOf(ScannedAffix affix) =>
            !string.IsNullOrWhiteSpace(affix.DisplayName) ? affix.DisplayName :
            !string.IsNullOrWhiteSpace(affix.OcrText) ? affix.OcrText :
            string.IsNullOrWhiteSpace(affix.AffixId) ? "Unknown affix" : affix.AffixId;
    }
}
