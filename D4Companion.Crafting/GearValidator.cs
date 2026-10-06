namespace D4Companion.Crafting
{
    public enum ValidationSeverity
    {
        /// <summary>
        /// The item cannot be confirmed until this is fixed, but it can be saved for later.
        /// </summary>
        NeedsCorrection,
        /// <summary>
        /// The data is invalid and cannot be saved.
        /// </summary>
        Invalid
    }

    public sealed record ValidationIssue(ValidationSeverity Severity, string Message, int? AffixIndex = null);

    public sealed class ValidationResult
    {
        public ValidationResult(IReadOnlyList<ValidationIssue> issues)
        {
            Issues = issues;
        }

        public IReadOnlyList<ValidationIssue> Issues { get; }
        public bool CanSave => Issues.All(i => i.Severity != ValidationSeverity.Invalid);
        public bool CanConfirm => Issues.Count == 0;
    }

    /// <summary>
    /// Checks OCR data before it is saved or confirmed.
    /// </summary>
    public static class GearValidator
    {
        public const int MaxAffixes = 12;
        public const int MaxItemPower = 10000;
        public const int MaxTextLength = 300;

        /// <summary>
        /// Affix ids from the game data join every internal id of an affix with ';' and can be very long.
        /// They must never be shortened, or they no longer match the build or the affix list.
        /// </summary>
        public const int MaxIdLength = 200_000;

        /// <summary>
        /// Returns a cleaned copy: trimmed and length-limited text, and no NaN or infinite numbers.
        /// </summary>
        public static GearSnapshot Sanitize(GearSnapshot snapshot)
        {
            return snapshot with
            {
                ItemType = Clean(snapshot.ItemType).ToLowerInvariant(),
                Rarity = Clean(snapshot.Rarity),
                Note = Clean(snapshot.Note),
                CraftState = (snapshot.CraftState ?? new ItemCraftState()) with
                {
                    EnchantedAffixId = Clean(snapshot.CraftState?.EnchantedAffixId, MaxIdLength)
                },
                Affixes = (snapshot.Affixes ?? Array.Empty<ScannedAffix>())
                    .Where(a => a != null)
                    .Select(a => a with
                    {
                        AffixId = Clean(a.AffixId, MaxIdLength),
                        DisplayName = Clean(a.DisplayName),
                        OcrText = Clean(a.OcrText),
                        Value = a.Value.HasValue && double.IsFinite(a.Value.Value) ? a.Value : null,
                        Kind = Enum.IsDefined(a.Kind) ? a.Kind : AffixKind.Unknown,
                        TuningPrisms = (a.TuningPrisms ?? Array.Empty<string>())
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .Select(p => Clean(p))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    })
                    .ToList()
            };
        }

        public static ValidationResult Validate(GearSnapshot snapshot)
        {
            var issues = new List<ValidationIssue>();

            if (string.IsNullOrWhiteSpace(snapshot.ItemType))
            {
                issues.Add(new(ValidationSeverity.Invalid, "The item slot is missing. Choose the slot before saving."));
            }

            if (snapshot.ItemPower < 0 || snapshot.ItemPower > MaxItemPower)
            {
                issues.Add(new(ValidationSeverity.Invalid, $"Item power {snapshot.ItemPower} is not a valid value."));
            }
            else if (snapshot.ItemPower == 0)
            {
                issues.Add(new(ValidationSeverity.NeedsCorrection, "Item power was not read. Enter it from the tooltip."));
            }

            var affixes = snapshot.Affixes ?? Array.Empty<ScannedAffix>();
            if (affixes.Count > MaxAffixes)
            {
                issues.Add(new(ValidationSeverity.Invalid, $"The scan has {affixes.Count} affixes, which is more than an item can have. Rescan the item."));
            }

            for (int i = 0; i < affixes.Count; i++)
            {
                var affix = affixes[i];
                if (affix.Value.HasValue && !double.IsFinite(affix.Value.Value))
                {
                    issues.Add(new(ValidationSeverity.Invalid, $"Affix {i + 1} has an unreadable number.", i));
                }

                if (string.IsNullOrWhiteSpace(affix.AffixId))
                {
                    issues.Add(new(ValidationSeverity.NeedsCorrection, $"Affix {i + 1} was not recognised. Choose the correct affix or remove the line.", i));
                }
                else if (affix.Kind == AffixKind.Unknown)
                {
                    issues.Add(new(ValidationSeverity.NeedsCorrection, $"Affix {i + 1} has no type. Choose normal, greater, tempered or implicit.", i));
                }
            }

            var duplicates = affixes
                .Select((a, i) => (Affix: a, Index: i))
                .Where(x => !string.IsNullOrWhiteSpace(x.Affix.AffixId))
                .GroupBy(x => (Id: x.Affix.AffixId.ToLowerInvariant(), Implicit: x.Affix.Kind == AffixKind.Implicit, Tempered: x.Affix.Kind == AffixKind.Tempered))
                .Where(g => g.Count() > 1);
            foreach (var group in duplicates)
            {
                foreach (var item in group.Skip(1))
                {
                    issues.Add(new(ValidationSeverity.NeedsCorrection,
                        $"Affix {item.Index + 1} appears more than once. This is usually a scan mistake; correct or remove it.", item.Index));
                }
            }

            return new ValidationResult(issues);
        }

        private static string Clean(string? value) => Clean(value, MaxTextLength);

        private static string Clean(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
            return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
        }
    }
}
