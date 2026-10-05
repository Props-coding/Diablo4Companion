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
        private string _affixId;
        private string _valueText;
        private AffixKind _kind;
        private bool _isKeep;
        private bool _isUserCorrected;
        private AffixStatus? _status;
        private string _explanation = string.Empty;
        private string _issue = string.Empty;

        public ScannedAffixViewModel(ScannedAffix model, Func<string, string> nameLookup, Func<string, IReadOnlyList<string>> prismLookup)
        {
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

        public string AffixId
        {
            get => _affixId;
            set
            {
                value ??= string.Empty;
                if (value == _affixId) return;
                _affixId = value;
                MarkCorrected();
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(PrismsText));
                OnPropertyChanged(nameof(HasPrisms));
                Edited?.Invoke(this, EventArgs.Empty);
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
                return prisms.Count == 0 ? string.Empty : "Prisms: " + string.Join(", ", prisms);
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
                if (SetProperty(ref _status, value)) OnPropertyChanged(nameof(StatusText));
            }
        }

        public string StatusText => CraftingText.StatusLabel(_status);

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
