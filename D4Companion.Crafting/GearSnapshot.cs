namespace D4Companion.Crafting
{
    /// <summary>
    /// How an affix appears on the item tooltip.
    /// </summary>
    public enum AffixKind
    {
        Unknown = 0,
        Normal = 1,
        Greater = 2,
        Tempered = 3,
        Implicit = 4
    }

    /// <summary>
    /// One affix line read from an item. Immutable: edits create a new instance.
    /// </summary>
    public sealed record ScannedAffix
    {
        /// <summary>
        /// Affix id (AffixInfo.IdName). Empty when OCR could not identify the affix.
        /// </summary>
        public string AffixId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        /// <summary>
        /// Raw OCR text, kept so the user can see what was read.
        /// </summary>
        public string OcrText { get; init; } = string.Empty;
        /// <summary>
        /// Numeric value read from the tooltip. Null when no value was read.
        /// </summary>
        public double? Value { get; init; }
        public AffixKind Kind { get; init; } = AffixKind.Unknown;
        public IReadOnlyList<string> TuningPrisms { get; init; } = Array.Empty<string>();
        /// <summary>
        /// Marked "Keep" by the user. A kept affix is never suggested as a replacement candidate.
        /// </summary>
        public bool IsKeep { get; init; }
        /// <summary>
        /// True when the user changed this affix manually after the scan.
        /// </summary>
        public bool IsUserCorrected { get; init; }

        public bool IsIdentified => !string.IsNullOrWhiteSpace(AffixId) && Kind != AffixKind.Unknown;
    }

    /// <summary>
    /// A frozen copy of a scanned item. It does not change when the live scan changes.
    /// </summary>
    public sealed record GearSnapshot
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;
        /// <summary>
        /// Item type id as used by the scanner, for example "ring" or "helm".
        /// </summary>
        public string ItemType { get; init; } = string.Empty;
        public int ItemPower { get; init; }
        public string Rarity { get; init; } = string.Empty;
        public bool IsUnique { get; init; }
        public IReadOnlyList<ScannedAffix> Affixes { get; init; } = Array.Empty<ScannedAffix>();
        /// <summary>
        /// The user checked every value and confirmed the item. Recommendations require this.
        /// </summary>
        public bool IsConfirmed { get; init; }
        public string Note { get; init; } = string.Empty;
    }

    /// <summary>
    /// One affix the selected build wants on a given item type.
    /// This is a copy of the preset data, never a reference to the preset itself.
    /// </summary>
    public sealed record TargetAffix
    {
        public string AffixId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string ItemType { get; init; } = string.Empty;
        public bool RequireGreater { get; init; }
        public bool IsTempered { get; init; }
        public bool IsImplicit { get; init; }
        /// <summary>
        /// Minimum value from the user's minimal affix value filter. Null when no minimum applies.
        /// </summary>
        public double? MinimumValue { get; init; }
        public IReadOnlyList<string> TuningPrisms { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// A frozen copy of the selected build (affix preset).
    /// </summary>
    public sealed record BuildTarget
    {
        public string Name { get; init; } = string.Empty;
        public IReadOnlyList<TargetAffix> Affixes { get; init; } = Array.Empty<TargetAffix>();

        public IReadOnlyList<TargetAffix> ForItemType(string itemType)
        {
            if (string.IsNullOrWhiteSpace(itemType)) return Array.Empty<TargetAffix>();
            return Affixes.Where(a => string.Equals(a.ItemType, itemType, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
