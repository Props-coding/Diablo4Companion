using System.Globalization;
using System.Text.RegularExpressions;

namespace D4Companion.Crafting
{
    /// <summary>
    /// Turns game data text into what a player expects to read:
    /// "+#% Critical Strike Chance" becomes "Critical Strike Chance", values become "6.3%" or "1,813".
    /// </summary>
    public static partial class AffixText
    {
        /// <summary>
        /// Affix name without value placeholders, signs or formula markup.
        /// </summary>
        public static string ReadableName(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) return string.Empty;

            string text = description.Replace("\r", " ").Replace("\n", " ");
            text = FormulaMarkup().Replace(text, " ");
            text = Placeholder().Replace(text, " ");
            text = LoneSign().Replace(text, " ");
            text = MultiSpace().Replace(text, " ").Trim();
            text = text.Trim(' ', ',', ':', '-');

            // "+# to Frenzy" adds skill ranks.
            if (text.StartsWith("to ", StringComparison.OrdinalIgnoreCase)) text = "Ranks " + text;
            return text;
        }

        /// <summary>
        /// True when the affix value is a percentage, for example "+#% Critical Strike Chance".
        /// </summary>
        public static bool IsPercent(string? description) =>
            !string.IsNullOrWhiteSpace(description) && description.Contains("#%", StringComparison.Ordinal);

        /// <summary>
        /// "6.3%" for percentages, "1,813" for flat values, empty when there is no value.
        /// </summary>
        public static string FormatValue(double? value, bool isPercent, CultureInfo? culture = null)
        {
            if (value is not double number || !double.IsFinite(number)) return string.Empty;
            culture ??= CultureInfo.CurrentCulture;
            if (isPercent) return number.ToString("#,0.##", culture) + "%";
            return number.ToString(Math.Abs(number) >= 100 ? "#,0" : "#,0.##", culture);
        }

        /// <summary>
        /// "Critical Strike Chance · 6.3%", or just the name when there is no value.
        /// </summary>
        public static string NameWithValue(string name, double? value, bool isPercent, CultureInfo? culture = null)
        {
            string formatted = FormatValue(value, isPercent, culture);
            return formatted.Length == 0 ? name : $"{name} · {formatted}";
        }

        // "[{VALUE}*100|%|]" style formula markup.
        [GeneratedRegex(@"\[[^\]]*\]")]
        private static partial Regex FormulaMarkup();

        // "+#%", "#%", "+#", "#", "x#" placeholders.
        [GeneratedRegex(@"[+\-]?#+%?")]
        private static partial Regex Placeholder();

        // A "+" or "-" left behind on its own once the value is gone.
        [GeneratedRegex(@"(?<=^|\s)[+\-](?=\s|$)")]
        private static partial Regex LoneSign();

        [GeneratedRegex(@"\s{2,}")]
        private static partial Regex MultiSpace();
    }

    /// <summary>
    /// Player-facing names for the tuning prism ids used in the game data.
    /// </summary>
    public static class PrismNames
    {
        public sealed record PrismInfo(string Id, string Name, string Color);

        private static readonly Dictionary<string, PrismInfo> Known = new(StringComparer.OrdinalIgnoreCase)
        {
            ["TuningStone_1"] = new("TuningStone_1", "Aggressive Tuning Prism", "#D2575C"),
            ["TuningStone_2"] = new("TuningStone_2", "Protector's Tuning Prism", "#5B8BD9"),
            ["TuningStone_3"] = new("TuningStone_3", "Resourceful Tuning Prism", "#6FAE78"),
            ["TuningStone_4"] = new("TuningStone_4", "Pragmatic Tuning Prism", "#A07CC9"),
            ["TuningStone_5"] = new("TuningStone_5", "Chromatic Tuning Prism", "#8FC7E0"),
            ["TuningStone_6"] = new("TuningStone_6", "Adept's Tuning Prism", "#D4B84B"),
        };

        public static PrismInfo Get(string id)
        {
            if (Known.TryGetValue(id, out var info)) return info;
            string number = id.StartsWith("TuningStone_", StringComparison.OrdinalIgnoreCase) ? id["TuningStone_".Length..] : id;
            return new PrismInfo(id, $"Tuning Prism {number}", "#A29D94");
        }

        public static string Name(string id) => Get(id).Name;
    }
}
