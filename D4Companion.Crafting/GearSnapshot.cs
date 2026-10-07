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
        /// <summary>Which unique the scanner recognised, if any. Empty when unknown.</summary>
        public string UniqueId { get; init; } = string.Empty;
        public string UniqueName { get; init; } = string.Empty;
        public IReadOnlyList<ScannedAffix> Affixes { get; init; } = Array.Empty<ScannedAffix>();
        /// <summary>
        /// The user checked every value and confirmed the item. Recommendations require this.
        /// </summary>
        public bool IsConfirmed { get; init; }
        public string Note { get; init; } = string.Empty;
        /// <summary>
        /// What the player told us about operations that are no longer available on this item.
        /// The scanner cannot read these from the tooltip.
        /// </summary>
        public ItemCraftState CraftState { get; init; } = new();
    }

    /// <summary>
    /// Crafting limits of one item, set by the player. Used to block operations the item can no longer take.
    /// Null means "not checked yet", which is different from "checked and allowed".
    /// </summary>
    public sealed record ItemCraftState
    {
        /// <summary>The item can't be changed any more, for example after a transfigure. Null: not checked.</summary>
        public bool? CannotBeModified { get; init; }
        /// <summary>The affix that was enchanted at the Occultist. Empty: checked, none yet. Null: not checked.</summary>
        public string? EnchantedAffixId { get; init; }
        /// <summary>Null: not checked.</summary>
        public bool? NoTempersLeft { get; init; }
        /// <summary>Null: not checked.</summary>
        public bool? FullyMasterworked { get; init; }
        /// <summary>Steps or goals the player found in-game are not possible on this item.</summary>
        public IReadOnlyList<RuledOutStep> RuledOut { get; init; } = Array.Empty<RuledOutStep>();

        public bool HasEnchant => !string.IsNullOrWhiteSpace(EnchantedAffixId);
        public bool EnchantChecked => EnchantedAffixId != null;
        public bool IsRuledOut(string key) => RuledOut.Any(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Something the player checked in-game and found not possible. Key identifies it to the advisor;
    /// Description is what the player sees.
    /// </summary>
    public sealed record RuledOutStep(string Key, string Description)
    {
        public static string StepKey(string operation, string affectedAffixId, string goalAffixId) => $"step|{operation}|{affectedAffixId}|{goalAffixId}";
        public static string GoalKey(string goalAffixId) => $"goal|{goalAffixId}";
    }

    /// <summary>
    /// One affix the selected build wants on a given item type.
    /// This is a copy of the preset data, never a reference to the preset itself.
    /// </summary>
    public sealed record TargetAffix
    {
        /// <summary>"Ring 1" or "Ring 2" when the build has two of this slot; empty otherwise.</summary>
        public string Variant { get; init; } = string.Empty;
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
        /// <summary>
        /// From the game data: whether this stat can be added by tempering. Null when unknown.
        /// </summary>
        public bool? CanBeTempered { get; init; }
        public IReadOnlyList<string> TuningPrisms { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// A frozen copy of the selected build (affix preset).
    /// </summary>
    /// <summary>A unique the build uses. Variant says which slot it's for ("Ring 1"), when the import says.</summary>
    public sealed record BuildUnique(string Id, string Name, string Variant = "");

    public sealed record BuildTarget
    {
        public string Name { get; init; } = string.Empty;
        public IReadOnlyList<TargetAffix> Affixes { get; init; } = Array.Empty<TargetAffix>();
        /// <summary>Unique items the build uses. Imports don't say which slot each one is for.</summary>
        public IReadOnlyList<BuildUnique> Uniques { get; init; } = Array.Empty<BuildUnique>();

        /// <summary>The slot variants for an item type, for example "Ring 1" and "Ring 2". Empty when the build has none.</summary>
        public IReadOnlyList<string> VariantsFor(string itemType) =>
            ForItemType(itemType).Select(a => a.Variant).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>A copy of the build where this item type keeps only one variant's affixes.</summary>
        public BuildTarget WithVariant(string itemType, string variant) => this with
        {
            Affixes = Affixes.Where(a => !string.Equals(a.ItemType, itemType, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(a.Variant, variant, StringComparison.OrdinalIgnoreCase)).ToList()
        };

        public IReadOnlyList<TargetAffix> ForItemType(string itemType)
        {
            if (string.IsNullOrWhiteSpace(itemType)) return Array.Empty<TargetAffix>();
            return Affixes.Where(a => string.Equals(a.ItemType, itemType, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
