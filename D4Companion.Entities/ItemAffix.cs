using System.Windows.Media;

namespace D4Companion.Entities
{
    public class ItemAffix
    {
        /// <summary>
        /// The unique identifier for the item affix.
        /// From AffixInfo.IdName
        /// </summary>
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public Color Color { get; set; } = Colors.Green;
        public bool IsAnyType { get; set; } = false;
        public bool IsGreater { get; set; } = false;
        public bool IsImplicit { get; set; } = false;
        public bool IsTempered { get; set; } = false;
        public List<string> TuningPrisms { get; set; } = new List<string>();
        /// <summary>
        /// Which of two same-type slots this belongs to, for example "Ring 1" or "Ring 2". Empty when the import
        /// doesn't say. Lets the Crafting Advisor compare each ring with its own half of the build.
        /// </summary>
        public string Variant { get; set; } = string.Empty;
    }
}
