using CommunityToolkit.Mvvm.ComponentModel;
using D4Companion.Crafting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace D4Companion.ViewModels.Entities
{
    public sealed record AffixOption(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// A tuning prism shown as a small coloured chip.
    /// </summary>
    public sealed record PrismChip(string Name, string Color)
    {
        public static IReadOnlyList<PrismChip> From(IEnumerable<string> prismIds) =>
            prismIds.Select(PrismNames.Get).Select(p => new PrismChip(p.Name, p.Color)).ToList();
    }

    /// <summary>
    /// One editable affix line of the item under review in the Crafting Advisor.
    /// </summary>
    public class ScannedAffixViewModel : ObservableObject
    {
        public static IReadOnlyList<AffixKind> KindOptions { get; } = new[]
        {
            AffixKind.Normal, AffixKind.Greater, AffixKind.Tempered, AffixKind.Implicit, AffixKind.Unknown
        };

        private readonly Func<string, string> _nameLookup;
        private readonly Func<string, IReadOnlyList<string>> _prismLookup;
        private readonly Func<string, bool> _percentLookup;
        private bool _isOffTarget;
        private string _affixId;
        private string _valueText;
        private AffixKind _kind;
        private bool _isKeep;
        private bool _isUserCorrected;
        private AffixStatus? _status;
        private string _explanation = string.Empty;
        private string _issue = string.Empty;
        private IReadOnlyList<AffixOption> _catalog;

        public ScannedAffixViewModel(ScannedAffix model, IReadOnlyList<AffixOption> catalog, Func<string, string> nameLookup, Func<string, IReadOnlyList<string>> prismLookup, Func<string, bool> percentLookup)
        {
            _percentLookup = percentLookup;
            _catalog = catalog;
            _nameLookup = nameLookup;
            _prismLookup = prismLookup;
            _affixId = model.AffixId;
            _valueText = model.Value.HasValue ? model.Value.Value.ToString("0.###", CultureInfo.CurrentCulture) : string.Empty;
            _kind = model.Kind;
            _isKeep = model.IsKeep;
            _isUserCorrected = model.IsUserCorrected;
            OcrText = model.OcrText;
        }

        /// <summary>
        /// Raised when the user edits anything. The item must be confirmed again afterwards.
        /// </summary>
        public event EventHandler? Edited;

        public string OcrText { get; }

        /// <summary>
        /// What the scanner read from the game, shown under the affix so the user can check it.
        /// </summary>
        public string ScannedText => string.IsNullOrWhiteSpace(OcrText) ? string.Empty : "Read from game: " + OcrText.Replace("\r", " ").Replace("\n", " ").Trim();
        public bool HasScannedText => !string.IsNullOrWhiteSpace(OcrText);

        /// <summary>
        /// Affixes the user can pick from. Held per row so the list is already there when the
        /// affix picker first binds its selection.
        /// </summary>
        public IReadOnlyList<AffixOption> Catalog
        {
            get => _catalog;
            set
            {
                if (SetProperty(ref _catalog, value)) OnPropertyChanged(nameof(SelectedAffix));
            }
        }

        public string AffixId
        {
            get => _affixId;
            set
            {
                // The affix picker pushes null when its list is not loaded yet or the typed text
                // matches nothing. Never let that wipe the scanned affix.
                if (value == null || value == _affixId) return;
                _affixId = value;
                MarkCorrected();
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(SelectedAffix));
                OnPropertyChanged(nameof(ValueDisplay));
                OnPropertyChanged(nameof(Prisms));
                OnPropertyChanged(nameof(PrismsText));
                OnPropertyChanged(nameof(HasPrisms));
                Edited?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// The catalog entry for the current affix, for the affix picker. Null when the affix is not in the catalog.
        /// </summary>
        public AffixOption? SelectedAffix
        {
            get => _catalog.FirstOrDefault(o => string.Equals(o.Id, _affixId, StringComparison.OrdinalIgnoreCase));
            set
            {
                // The picker pushes null while it loads. Never let that wipe the scanned affix.
                if (value == null) return;
                AffixId = value.Id;
            }
        }

        public string DisplayName
        {
            get
            {
                string name = _nameLookup(_affixId);
                if (!string.IsNullOrWhiteSpace(name)) return name;
                return string.IsNullOrWhiteSpace(OcrText) ? "Unknown affix" : $"Not recognised: {OcrText}";
            }
        }

        public string ValueText
        {
            get => _valueText;
            set
            {
                value ??= string.Empty;
                if (value == _valueText) return;
                _valueText = value;
                MarkCorrected();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ValueDisplay));
                Edited?.Invoke(this, EventArgs.Empty);
            }
        }

        public AffixKind Kind
        {
            get => _kind;
            set
            {
                if (value == _kind) return;
                _kind = value;
                MarkCorrected();
                OnPropertyChanged();
                OnPropertyChanged(nameof(KindTag));
                OnPropertyChanged(nameof(HasKindTag));
                OnPropertyChanged(nameof(IsGreater));
                Edited?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsKeep
        {
            get => _isKeep;
            set
            {
                if (value == _isKeep) return;
                _isKeep = value;
                OnPropertyChanged();
                Edited?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool IsUserCorrected
        {
            get => _isUserCorrected;
            private set => SetProperty(ref _isUserCorrected, value);
        }

        public string PrismsText
        {
            get
            {
                var prisms = _prismLookup(_affixId);
                return prisms.Count == 0 ? string.Empty : "Prisms: " + string.Join(", ", prisms.Select(PrismNames.Name));
            }
        }

        public IReadOnlyList<PrismChip> Prisms => PrismChip.From(_prismLookup(_affixId));

        /// <summary>
        /// The value as the game shows it: "6.3%" or "1,813".
        /// </summary>
        public string ValueDisplay
        {
            get
            {
                if (!TryGetValue(out double? value) || value == null) return string.IsNullOrWhiteSpace(_valueText) ? string.Empty : _valueText;
                return AffixText.FormatValue(value, _percentLookup(_affixId));
            }
        }

        public bool IsGreater => _kind == AffixKind.Greater;

        public string KindTag => _kind switch
        {
            AffixKind.Greater => "Greater",
            AffixKind.Tempered => "Tempered",
            AffixKind.Implicit => "Implicit",
            AffixKind.Unknown => "Type unknown",
            _ => string.Empty
        };

        public bool HasKindTag => KindTag.Length > 0;

        /// <summary>
        /// The affix is on the item but not in the build.
        /// </summary>
        public bool IsOffTarget
        {
            get => _isOffTarget;
            set
            {
                if (SetProperty(ref _isOffTarget, value)) OnPropertyChanged(nameof(StatusText));
            }
        }

        public bool HasPrisms => _prismLookup(_affixId).Count > 0;

        /// <summary>
        /// Comparison status after the item is confirmed. Null before that.
        /// </summary>
        public AffixStatus? Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(HasStatus));
                }
            }
        }

        public string StatusText => _isOffTarget && _status == AffixStatus.Review ? "Not in build" : CraftingText.StatusLabel(_status);
        public bool HasStatus => _status != null;

        public string Explanation
        {
            get => _explanation;
            set => SetProperty(ref _explanation, value);
        }

        /// <summary>
        /// Validation problem with this line, if any.
        /// </summary>
        public string Issue
        {
            get => _issue;
            set
            {
                if (SetProperty(ref _issue, value)) OnPropertyChanged(nameof(HasIssue));
            }
        }

        public bool HasIssue => !string.IsNullOrWhiteSpace(_issue);

        public bool TryGetValue(out double? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(_valueText)) return true;

            string text = _valueText.Trim().TrimEnd('%').Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                if (!double.IsFinite(parsed)) return false;
                value = parsed;
                return true;
            }
            return false;
        }

        public ScannedAffix ToModel()
        {
            TryGetValue(out double? value);
            return new ScannedAffix
            {
                AffixId = _affixId,
                DisplayName = _nameLookup(_affixId),
                OcrText = OcrText,
                Value = value,
                Kind = _kind,
                TuningPrisms = _prismLookup(_affixId).ToList(),
                IsKeep = _isKeep,
                IsUserCorrected = _isUserCorrected
            };
        }

        private void MarkCorrected() => IsUserCorrected = true;
    }

    /// <summary>
    /// Shared wording for statuses, so every screen uses the same labels.
    /// </summary>
    public static class CraftingText
    {
        public static string StatusLabel(AffixStatus? status) => status switch
        {
            AffixStatus.Match => "Matches",
            AffixStatus.Missing => "Missing",
            AffixStatus.BelowMinimum => "Below minimum",
            AffixStatus.GreaterNeeded => "Greater affix needed",
            AffixStatus.Review => "Review",
            AffixStatus.Unknown => "Unknown, needs correction",
            _ => string.Empty
        };

        public static string VerdictLabel(CraftingVerdict verdict) => verdict switch
        {
            CraftingVerdict.MeetsTarget => "Matches the build",
            CraftingVerdict.NeedsWork => "Needs work",
            CraftingVerdict.NeedsCorrection => "Needs correction",
            CraftingVerdict.NotConfirmed => "Not confirmed",
            CraftingVerdict.NoTargetForSlot => "No target for this slot",
            CraftingVerdict.NotApplicable => "Not compared",
            _ => string.Empty
        };
    }
}
