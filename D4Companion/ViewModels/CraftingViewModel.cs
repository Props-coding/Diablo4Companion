using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4Companion.Crafting;
using D4Companion.State;
using D4Companion.ViewModels.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Input;

namespace D4Companion.ViewModels
{
    public sealed record SlotOption(string Id, string Name);

    public sealed class ComparisonRowViewModel
    {
        public string Name { get; init; } = string.Empty;
        public string ItemValue { get; init; } = string.Empty;
        public string Requirement { get; init; } = string.Empty;
        public AffixStatus? Status { get; init; }
        public bool IsOffTarget { get; init; }
        public string StatusText => IsOffTarget && Status == AffixStatus.Review ? "Not in build" : CraftingText.StatusLabel(Status);
        public string Explanation { get; init; } = string.Empty;
    }

    public sealed class PrismHintRowViewModel
    {
        public string AffixName { get; init; } = string.Empty;
        public IReadOnlyList<PrismChip> Prisms { get; init; } = Array.Empty<PrismChip>();
    }

    public sealed class HistoryRowViewModel
    {
        public string Label { get; init; } = string.Empty;
        public string When { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
    }

    public sealed class ChangeRowViewModel
    {
        public string Name { get; init; } = string.Empty;
        public string Change { get; init; } = string.Empty;
        public AffixChange Kind { get; init; }
    }

    /// <summary>
    /// Crafting Advisor. Singleton: the full window and the compact second-monitor view share it.
    /// </summary>
    public class CraftingViewModel : ObservableObject
    {
        private readonly ILogger _logger;
        private readonly CompanionState _state;

        private List<AffixOption> _affixCatalog = new();
        private Dictionary<string, string> _affixNames = new(StringComparer.OrdinalIgnoreCase);

        private bool _hasItem;
        private string _itemType = string.Empty;
        private string _itemPowerText = string.Empty;
        private string _rarity = string.Empty;
        private bool _isUnique;
        private bool _isConfirmed;
        private bool _hasUnsavedChanges;
        private Guid? _currentRecordId;
        private bool _isAfterCraftingDraft;
        private string _statusMessage = string.Empty;
        private CraftingAnalysis? _analysis;
        private DateTime _capturedAtUtc = DateTime.UtcNow;
        private bool _isEditing;
        private bool _includeGreaterAffixes;

        public CraftingViewModel(ILogger<CraftingViewModel> logger, CompanionState state)
        {
            _logger = logger;
            _state = state;

            _state.LiveScanChanged += (s, e) => OnLiveScanChanged();
            _state.ScannerStateChanged += (s, e) => OnLiveScanChanged();
            _state.BuildTargetChanged += (s, e) => OnBuildTargetChanged();
            _state.AffixCatalogChanged += (s, e) => LoadAffixCatalog();
            _state.OpenGearRequested += (s, id) => OpenRecord(id);

            SlotOptions = CompanionState.GearSlots.Select(id => new SlotOption(id, CompanionState.SlotName(id))).ToList();

            CaptureScanCommand = new RelayCommand(CaptureScan, () => _state.LiveScan != null);
            ToggleScannerCommand = new RelayCommand(() => _state.SetScanner(!_state.IsScannerOn));
            RescanAfterCraftingCommand = new RelayCommand(RescanAfterCrafting, () => _state.LiveScan != null && _currentRecordId != null);
            ConfirmCommand = new RelayCommand(Confirm, () => HasItem);
            SaveCommand = new RelayCommand(Save, () => HasItem);
            AddAffixCommand = new RelayCommand(AddAffix, () => HasItem);
            RemoveAffixCommand = new RelayCommand<ScannedAffixViewModel>(RemoveAffix);
            ClearCommand = new RelayCommand(Clear, () => HasItem);
            OpenBuildsCommand = new RelayCommand(() => _state.Navigate(CompanionPage.Builds));
            OpenGearCommand = new RelayCommand(() => _state.Navigate(CompanionPage.Gear));
            EditScanCommand = new RelayCommand(() => IsEditing = !IsEditing, () => HasItem);

            LoadAffixCatalog();
            RefreshPresets();
            RefreshNextAction();
        }

        // Commands

        public ICommand CaptureScanCommand { get; }
        public ICommand ToggleScannerCommand { get; }
        public ICommand RescanAfterCraftingCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand AddAffixCommand { get; }
        public ICommand RemoveAffixCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand OpenBuildsCommand { get; }
        public ICommand OpenGearCommand { get; }
        public ICommand EditScanCommand { get; }

        // Build selection

        public ObservableCollection<string> PresetNames { get; } = new();

        public string SelectedPresetName
        {
            get => _state.SelectedPresetName;
            set
            {
                if (!string.IsNullOrWhiteSpace(value)) _state.SelectPreset(value);
            }
        }

        public bool HasBuild => !string.IsNullOrWhiteSpace(_state.BuildTarget.Name);
        public string BuildSummary
        {
            get
            {
                if (!HasBuild) return "No build selected. Import one from Maxroll or Mobalytics on the Builds page.";
                int slots = _state.BuildTarget.Affixes.Select(a => a.ItemType).Distinct().Count();
                return $"{_state.BuildTarget.Affixes.Count} target affixes across {slots} slots.";
            }
        }

        // Live scan

        public bool IsLiveScanAvailable => _state.LiveScan != null;

        public bool IsScannerOn => _state.IsScannerOn;
        public string ScannerButtonText => _state.IsScannerOn ? "Turn scanner off" : "Turn scanner on";

        public string LiveScanText
        {
            get
            {
                var live = _state.LiveScan;
                if (live == null)
                {
                    return _state.IsScannerOn
                        ? "Scanner is on. Hover over a piece of gear in Diablo IV until the overlay marks it, then press Capture scan. If nothing happens, check that a system preset for your resolution is selected in Settings."
                        : "Scanner is off. Press Turn scanner on, then hover over a piece of gear in Diablo IV. Capture scan becomes available once an item has been read.";
                }
                string when = _state.LiveScanAtLocal?.ToString("t", CultureInfo.CurrentCulture) ?? string.Empty;
                string scanner = _state.IsScannerOn ? string.Empty : " Scanner is off.";
                return $"Last scanned: {CompanionState.SlotName(live.ItemType)}, item power {live.ItemPower}, {live.Affixes.Count} affixes ({when}).{scanner}";
            }
        }

        // Item under review

        public IReadOnlyList<SlotOption> SlotOptions { get; }
        public IReadOnlyList<AffixOption> AffixCatalog => _affixCatalog;
        public IReadOnlyList<AffixKind> KindOptions => ScannedAffixViewModel.KindOptions;
        public ObservableCollection<ScannedAffixViewModel> Affixes { get; } = new();
        public ObservableCollection<string> ValidationIssues { get; } = new();
        public bool HasValidationIssues => ValidationIssues.Count > 0;

        public bool HasItem
        {
            get => _hasItem;
            private set
            {
                if (SetProperty(ref _hasItem, value))
                {
                    OnPropertyChanged(nameof(ItemTitle));
                    OnPropertyChanged(nameof(ItemName));
                    OnPropertyChanged(nameof(ItemDetails));
                    RefreshCommands();
                }
            }
        }

        /// <summary>
        /// Show the dropdowns and inputs. Off by default: the item reads as clean rows.
        /// </summary>
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (SetProperty(ref _isEditing, value))
                {
                    OnPropertyChanged(nameof(IsReading));
                    OnPropertyChanged(nameof(EditButtonText));
                }
            }
        }

        public bool IsReading => !_isEditing;
        public string EditButtonText => _isEditing ? "Done editing" : "Edit scan";

        public string ItemName => HasItem ? CompanionState.SlotName(_itemType) : "No item yet";

        public string ItemDetails
        {
            get
            {
                if (!HasItem) return "Capture a scan to start.";
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(_itemPowerText)) parts.Add($"{_itemPowerText} Item Power");
                if (!string.IsNullOrWhiteSpace(_rarity)) parts.Add(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(_rarity.ToLowerInvariant()));
                if (_isUnique) parts.Add("Unique");
                return string.Join(" · ", parts);
            }
        }

        public string ItemTitle => !HasItem ? "No item captured"
            : (_isAfterCraftingDraft ? "Rescanned " : _currentRecordId != null ? "Saved " : "Scanned ") + CompanionState.SlotName(_itemType).ToLowerInvariant();

        public string ItemType
        {
            get => _itemType;
            set
            {
                if (SetProperty(ref _itemType, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(ItemTitle));
                    OnPropertyChanged(nameof(ItemName));
                    OnEdited();
                }
            }
        }

        public string ItemPowerText
        {
            get => _itemPowerText;
            set
            {
                if (SetProperty(ref _itemPowerText, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(ItemDetails));
                    OnEdited();
                }
            }
        }

        public string Rarity
        {
            get => _rarity;
            set
            {
                if (SetProperty(ref _rarity, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(ItemDetails));
                    OnEdited();
                }
            }
        }

        public bool IsUnique
        {
            get => _isUnique;
            set
            {
                if (SetProperty(ref _isUnique, value))
                {
                    OnPropertyChanged(nameof(ItemDetails));
                    OnEdited();
                }
            }
        }

        public bool IsConfirmed
        {
            get => _isConfirmed;
            private set
            {
                if (SetProperty(ref _isConfirmed, value))
                {
                    OnPropertyChanged(nameof(ConfirmationText));
                    OnPropertyChanged(nameof(ConfirmButtonText));
                    RefreshNextAction();
                }
            }
        }

        public string ConfirmationText => IsConfirmed
            ? "Confirmed. If you change anything you will need to confirm again."
            : "Does this match the game? If something is wrong, use Edit scan to fix it.";

        public string ConfirmButtonText => IsConfirmed ? "Confirmed" : "Looks right, confirm";

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                if (SetProperty(ref _hasUnsavedChanges, value)) RefreshNextAction();
            }
        }

        // Next action bar: always visible at the top of the page.

        public string NextActionTitle { get; private set; } = string.Empty;
        public string NextActionDetail { get; private set; } = string.Empty;
        public bool ShowCaptureAction { get; private set; }
        public bool ShowConfirmAction { get; private set; }
        public bool ShowSaveAction { get; private set; }
        public bool ShowRescanAction { get; private set; }

        private void RefreshNextAction()
        {
            ShowCaptureAction = ShowConfirmAction = ShowSaveAction = ShowRescanAction = false;

            if (!HasItem)
            {
                NextActionTitle = "Capture an item";
                NextActionDetail = _state.LiveScan != null
                    ? "The scanner has read an item. Capture it to start."
                    : _state.IsScannerOn ? "Hover over a piece of gear in Diablo IV." : "Turn the scanner on, then hover over a piece of gear in Diablo IV.";
                ShowCaptureAction = true;
            }
            else if (!IsConfirmed)
            {
                NextActionTitle = "Check the item, then confirm";
                NextActionDetail = "Compare it with the game. Use Edit scan if anything is wrong.";
                ShowConfirmAction = true;
            }
            else
            {
                NextActionTitle = Headline;
                NextActionDetail = HasTargetStat ? $"Target: {TargetStat}" : Summary;
                ShowSaveAction = HasUnsavedChanges;
                ShowRescanAction = !HasUnsavedChanges && _currentRecordId != null;
            }

            OnPropertyChanged(nameof(NextActionTitle));
            OnPropertyChanged(nameof(NextActionDetail));
            OnPropertyChanged(nameof(ShowCaptureAction));
            OnPropertyChanged(nameof(ShowConfirmAction));
            OnPropertyChanged(nameof(ShowSaveAction));
            OnPropertyChanged(nameof(ShowRescanAction));
        }

        // Recommendation

        public bool ShowRecommendation => IsConfirmed && _analysis != null && _analysis.Verdict != CraftingVerdict.NotConfirmed;
        public bool ShowConfirmFirst => !ShowRecommendation;
        public string VerdictText => _analysis == null ? string.Empty : CraftingText.VerdictLabel(_analysis.Verdict);
        public bool MeetsTarget => _analysis?.Verdict == CraftingVerdict.MeetsTarget;
        public string Headline => _analysis?.Recommendation.Headline ?? "Confirm the item first";
        public string Summary => _analysis?.Recommendation.Summary
            ?? "Capture a scan, check the values, and confirm. The advisor only gives a recommendation for a confirmed item.";
        public string TargetStat => _analysis?.Recommendation.TargetStat ?? string.Empty;
        public bool HasTargetStat => ShowRecommendation && !string.IsNullOrEmpty(TargetStat);
        public string ProtectedText => Join(_analysis?.Recommendation.Protected);
        public bool HasProtected => !string.IsNullOrEmpty(ProtectedText);

        /// <summary>
        /// Greater affixes are protected by default. The player can choose to include them in suggestions.
        /// </summary>
        public bool IncludeGreaterAffixes
        {
            get => _includeGreaterAffixes;
            set
            {
                if (SetProperty(ref _includeGreaterAffixes, value)) Analyze();
            }
        }

        public bool HasDetails => HasKeep || HasReplace || HasTarget || HasImprove || HasProtected;
        public string KeepText => Join(_analysis?.Recommendation.Keep);
        public bool HasKeep => !string.IsNullOrEmpty(KeepText);
        public string ReplaceText => _analysis?.Recommendation.ReplaceCandidate is ScannedAffix candidate ? NameOf(candidate) : string.Empty;
        public bool HasReplace => !string.IsNullOrEmpty(ReplaceText);
        public string ReplaceNote => _analysis?.Recommendation.ReplaceCandidateNote ?? string.Empty;
        public bool HasReplaceNote => !string.IsNullOrEmpty(ReplaceNote);
        public string TargetText => Join(_analysis?.Recommendation.MissingTargets);
        public bool HasTarget => !string.IsNullOrEmpty(TargetText);
        public string ImproveText => Join(_analysis?.Recommendation.ImproveTargets);
        public bool HasImprove => !string.IsNullOrEmpty(ImproveText);
        public ObservableCollection<PrismHintRowViewModel> PrismHints { get; } = new();
        public bool HasPrismHints => PrismHints.Count > 0;
        public ObservableCollection<string> InGameChecks { get; } = new();
        public bool HasInGameChecks => InGameChecks.Count > 0;
        public string BuildMatchStatement => _analysis?.BuildMatchStatement ?? string.Empty;
        public string ItemQualityStatement => CraftingAnalyzer.ItemQualityStatement;
        public string DamageStatement => CraftingAnalyzer.DamageStatement;
        public ObservableCollection<ComparisonRowViewModel> Comparisons { get; } = new();

        // History

        public ObservableCollection<HistoryRowViewModel> History { get; } = new();
        public bool HasHistory => History.Count > 0;
        public ObservableCollection<ChangeRowViewModel> Changes { get; } = new();
        public bool HasChanges => Changes.Count > 0;
        public string ChangesTitle { get; private set; } = "Before and after";
        public bool CanRescanAfterCrafting => _currentRecordId != null;

        // Actions

        private void CaptureScan()
        {
            var live = _state.LiveScan;
            if (live == null) return;

            _currentRecordId = null;
            _isAfterCraftingDraft = false;
            LoadSnapshot(live);
            HasUnsavedChanges = true;
            StatusMessage = "Scan captured. This copy will not change when you hover over other items.";
            RefreshHistory();
        }

        private void RescanAfterCrafting()
        {
            var live = _state.LiveScan;
            if (live == null || _currentRecordId == null) return;

            var record = _state.Store.Find(_currentRecordId.Value);
            if (record == null) return;

            if (!string.Equals(record.ItemType, live.ItemType, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"The last scan is a {CompanionState.SlotName(live.ItemType).ToLowerInvariant()}, but this item is a {CompanionState.SlotName(record.ItemType).ToLowerInvariant()}. Hover over the crafted item and try again.";
                return;
            }

            // Carry over Keep marks for affixes that are still on the item.
            var kept = Affixes.Where(a => a.IsKeep).Select(a => a.AffixId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var snapshot = live with
            {
                Affixes = live.Affixes.Select(a => a with { IsKeep = kept.Contains(a.AffixId) }).ToList()
            };

            _isAfterCraftingDraft = true;
            LoadSnapshot(snapshot);
            HasUnsavedChanges = true;
            StatusMessage = "New scan captured after crafting. Check it, confirm it, then save it to keep the history.";
            RefreshHistory();
        }

        private void Confirm()
        {
            var snapshot = BuildSnapshot(out var lineProblems);
            var validation = GearValidator.Validate(snapshot);
            ShowValidation(validation, lineProblems);

            if (lineProblems.Count > 0 || !validation.CanConfirm)
            {
                IsConfirmed = false;
                StatusMessage = "Fix the highlighted problems before confirming.";
                Analyze();
                return;
            }

            IsConfirmed = true;
            StatusMessage = "Item confirmed.";
            Analyze();
        }

        private void Save()
        {
            var snapshot = BuildSnapshot(out var lineProblems);
            var validation = GearValidator.Validate(snapshot);
            ShowValidation(validation, lineProblems);

            if (lineProblems.Count > 0 || !validation.CanSave)
            {
                StatusMessage = "This item has invalid data and was not saved. Fix the highlighted problems first.";
                return;
            }

            try
            {
                GearStoreResult result;
                if (_currentRecordId is Guid recordId && _state.Store.Find(recordId) != null)
                {
                    result = _state.Store.AddRevision(recordId, snapshot, _isAfterCraftingDraft ? "After crafting" : "Corrected");
                }
                else
                {
                    result = _state.Store.AddItem(snapshot, "Scanned");
                }

                if (!result.Success || result.Record == null)
                {
                    StatusMessage = result.Message;
                    return;
                }

                _currentRecordId = result.Record.Id;
                HasUnsavedChanges = false;
                StatusMessage = IsConfirmed ? "Saved to My Gear." : "Saved to My Gear. Confirm the item to get a recommendation.";
                _isAfterCraftingDraft = false;
                RefreshHistory();
                OnPropertyChanged(nameof(ItemTitle));
                OnPropertyChanged(nameof(CanRescanAfterCrafting));
                RefreshCommands();
                _state.NotifyGearChanged();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name);
                StatusMessage = "Saving failed. See the log for details.";
            }
        }

        private void AddAffix()
        {
            var row = CreateRow(new ScannedAffix { Kind = AffixKind.Normal, IsUserCorrected = true });
            Affixes.Add(row);
            OnEdited();
        }

        private void RemoveAffix(ScannedAffixViewModel? row)
        {
            if (row == null) return;
            row.Edited -= Row_Edited;
            Affixes.Remove(row);
            OnEdited();
        }

        private void Clear()
        {
            foreach (var row in Affixes) row.Edited -= Row_Edited;
            Affixes.Clear();
            _currentRecordId = null;
            _isAfterCraftingDraft = false;
            _itemType = string.Empty;
            _itemPowerText = string.Empty;
            _rarity = string.Empty;
            _isUnique = false;
            HasItem = false;
            IsEditing = false;
            IsConfirmed = false;
            HasUnsavedChanges = false;
            StatusMessage = string.Empty;
            ValidationIssues.Clear();
            OnPropertyChanged(string.Empty);
            Analyze();
            RefreshHistory();
        }

        public void OpenRecord(Guid recordId)
        {
            var record = _state.Store.Find(recordId);
            var latest = record?.Latest;
            if (record == null || latest == null) return;

            _currentRecordId = record.Id;
            _isAfterCraftingDraft = false;
            LoadSnapshot(latest.Snapshot);
            IsConfirmed = latest.Snapshot.IsConfirmed;
            HasUnsavedChanges = false;
            StatusMessage = $"Opened saved {CompanionState.SlotName(record.ItemType).ToLowerInvariant()}.";
            Analyze();
            RefreshHistory();
        }

        // Helpers

        private void LoadSnapshot(GearSnapshot snapshot)
        {
            foreach (var row in Affixes) row.Edited -= Row_Edited;
            Affixes.Clear();
            foreach (var affix in snapshot.Affixes)
            {
                Affixes.Add(CreateRow(affix));
            }

            _capturedAtUtc = snapshot.CapturedAtUtc;
            _itemType = snapshot.ItemType;
            _itemPowerText = snapshot.ItemPower > 0 ? snapshot.ItemPower.ToString(CultureInfo.CurrentCulture) : string.Empty;
            _rarity = snapshot.Rarity;
            _isUnique = snapshot.IsUnique;
            HasItem = true;
            IsConfirmed = false;
            ValidationIssues.Clear();
            OnPropertyChanged(nameof(ItemType));
            OnPropertyChanged(nameof(ItemPowerText));
            OnPropertyChanged(nameof(Rarity));
            OnPropertyChanged(nameof(IsUnique));
            OnPropertyChanged(nameof(ItemTitle));
            OnPropertyChanged(nameof(ItemName));
            OnPropertyChanged(nameof(ItemDetails));
            OnPropertyChanged(nameof(HasValidationIssues));
            OnPropertyChanged(nameof(CanRescanAfterCrafting));
            RefreshCommands();
            Analyze();
        }

        private ScannedAffixViewModel CreateRow(ScannedAffix affix)
        {
            var row = new ScannedAffixViewModel(affix, _affixCatalog, LookupName, id => _state.GetPrisms(id), id => _state.IsPercentAffix(id));
            row.Edited += Row_Edited;
            return row;
        }

        private void Row_Edited(object? sender, EventArgs e) => OnEdited();

        private void OnEdited()
        {
            if (!HasItem) return;
            HasUnsavedChanges = true;
            if (IsConfirmed)
            {
                IsConfirmed = false;
                StatusMessage = "You changed the item. Confirm it again to update the recommendation.";
            }
            Analyze();
        }

        private GearSnapshot BuildSnapshot(out List<string> lineProblems)
        {
            lineProblems = new List<string>();

            int itemPower = 0;
            if (!string.IsNullOrWhiteSpace(_itemPowerText) &&
                !int.TryParse(_itemPowerText.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out itemPower))
            {
                lineProblems.Add("Item power must be a whole number.");
                itemPower = 0;
            }

            var affixes = new List<ScannedAffix>();
            for (int i = 0; i < Affixes.Count; i++)
            {
                if (!Affixes[i].TryGetValue(out _))
                {
                    lineProblems.Add($"Affix {i + 1}: the value is not a number.");
                }
                affixes.Add(Affixes[i].ToModel());
            }

            return GearValidator.Sanitize(new GearSnapshot
            {
                CapturedAtUtc = _capturedAtUtc,
                ItemType = _itemType,
                ItemPower = itemPower,
                Rarity = _rarity,
                IsUnique = _isUnique,
                Affixes = affixes,
                IsConfirmed = IsConfirmed
            });
        }

        private void ShowValidation(ValidationResult validation, List<string> lineProblems)
        {
            ValidationIssues.Clear();
            foreach (var row in Affixes) row.Issue = string.Empty;

            foreach (var problem in lineProblems) ValidationIssues.Add(problem);
            foreach (var issue in validation.Issues)
            {
                ValidationIssues.Add(issue.Message);
                if (issue.AffixIndex is int index && index >= 0 && index < Affixes.Count)
                {
                    Affixes[index].Issue = issue.Message;
                }
            }
            OnPropertyChanged(nameof(HasValidationIssues));
        }

        private void Analyze()
        {
            _analysis = null;
            Comparisons.Clear();
            PrismHints.Clear();
            InGameChecks.Clear();
            foreach (var row in Affixes)
            {
                row.Status = null;
                row.IsOffTarget = false;
                row.Explanation = string.Empty;
            }

            if (HasItem && IsConfirmed)
            {
                try
                {
                    var snapshot = BuildSnapshot(out _) with { IsConfirmed = true };
                    _analysis = CraftingAnalyzer.Analyze(snapshot, _state.BuildTarget,
                        new AdvisorOptions { IncludeGreaterAffixes = _includeGreaterAffixes });

                    foreach (var comparison in _analysis.Comparisons)
                    {
                        Comparisons.Add(new ComparisonRowViewModel
                        {
                            Name = comparison.Name,
                            ItemValue = comparison.Scanned == null ? "Not on item" : FormatValue(comparison.Scanned),
                            IsOffTarget = comparison.IsOffTarget,
                            Requirement = comparison.Target == null ? "Not in build" : Requirement(comparison.Target),
                            Status = comparison.Status,
                            Explanation = comparison.Explanation
                        });

                        if (comparison.ScannedIndex is int index && index < Affixes.Count)
                        {
                            Affixes[index].Status = comparison.Status;
                            Affixes[index].IsOffTarget = comparison.IsOffTarget;
                            Affixes[index].Explanation = comparison.Explanation;
                        }
                    }

                    foreach (var hint in _analysis.Recommendation.PrismHints)
                    {
                        PrismHints.Add(new PrismHintRowViewModel { AffixName = hint.AffixName, Prisms = PrismChip.From(hint.Prisms) });
                    }
                    foreach (var check in _analysis.Recommendation.InGameChecks)
                    {
                        InGameChecks.Add(check);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name);
                    _analysis = null;
                }
            }

            OnPropertyChanged(nameof(ShowRecommendation));
            OnPropertyChanged(nameof(ShowConfirmFirst));
            OnPropertyChanged(nameof(VerdictText));
            OnPropertyChanged(nameof(MeetsTarget));
            OnPropertyChanged(nameof(Headline));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(KeepText));
            OnPropertyChanged(nameof(HasKeep));
            OnPropertyChanged(nameof(ReplaceText));
            OnPropertyChanged(nameof(HasReplace));
            OnPropertyChanged(nameof(ReplaceNote));
            OnPropertyChanged(nameof(HasReplaceNote));
            OnPropertyChanged(nameof(TargetText));
            OnPropertyChanged(nameof(HasTarget));
            OnPropertyChanged(nameof(ImproveText));
            OnPropertyChanged(nameof(HasImprove));
            OnPropertyChanged(nameof(HasPrismHints));
            OnPropertyChanged(nameof(HasInGameChecks));
            OnPropertyChanged(nameof(BuildMatchStatement));
            OnPropertyChanged(nameof(TargetStat));
            OnPropertyChanged(nameof(HasTargetStat));
            OnPropertyChanged(nameof(ProtectedText));
            OnPropertyChanged(nameof(HasProtected));
            OnPropertyChanged(nameof(HasDetails));
            RefreshNextAction();
        }

        private void RefreshHistory()
        {
            History.Clear();
            Changes.Clear();

            var record = _currentRecordId is Guid id ? _state.Store.Find(id) : null;
            if (record != null)
            {
                foreach (var revision in record.Revisions.Reverse())
                {
                    History.Add(new HistoryRowViewModel
                    {
                        Label = revision.Label,
                        When = revision.SavedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                        Summary = $"Item power {revision.Snapshot.ItemPower}, {revision.Snapshot.Affixes.Count} affixes" +
                                  (revision.Snapshot.IsConfirmed ? ", confirmed" : string.Empty)
                    });
                }

                // Before and after: the unsaved rescan against the last saved version,
                // or the last two saved versions.
                GearSnapshot? before = null;
                GearSnapshot? after = null;
                if (_isAfterCraftingDraft && HasItem)
                {
                    before = record.Latest?.Snapshot;
                    after = BuildSnapshot(out _);
                    ChangesTitle = "Before and after (not saved yet)";
                }
                else if (record.Revisions.Count >= 2)
                {
                    before = record.Revisions[^2].Snapshot;
                    after = record.Revisions[^1].Snapshot;
                    ChangesTitle = "Before and after the last change";
                }

                if (before != null && after != null)
                {
                    foreach (var diff in SnapshotComparer.Compare(before, after))
                    {
                        Changes.Add(new ChangeRowViewModel { Name = diff.Name, Change = diff.Description, Kind = diff.Change });
                    }
                }
            }

            OnPropertyChanged(nameof(HasHistory));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(ChangesTitle));
        }

        private void OnLiveScanChanged()
        {
            OnPropertyChanged(nameof(IsLiveScanAvailable));
            OnPropertyChanged(nameof(LiveScanText));
            OnPropertyChanged(nameof(IsScannerOn));
            OnPropertyChanged(nameof(ScannerButtonText));
            RefreshCommands();
            RefreshNextAction();
        }

        private void OnBuildTargetChanged()
        {
            RefreshPresets();
            OnPropertyChanged(nameof(HasBuild));
            OnPropertyChanged(nameof(BuildSummary));
            Analyze();
        }

        private void RefreshPresets()
        {
            var names = _state.PresetNames;
            if (!names.SequenceEqual(PresetNames))
            {
                PresetNames.Clear();
                foreach (var name in names) PresetNames.Add(name);
            }
            OnPropertyChanged(nameof(SelectedPresetName));
        }

        private void LoadAffixCatalog()
        {
            _affixCatalog = _state.GetAffixCatalog().Select(a => new AffixOption(a.Id, a.Name)).ToList();
            _affixNames = _affixCatalog.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
            OnPropertyChanged(nameof(AffixCatalog));
            foreach (var row in Affixes) row.Catalog = _affixCatalog;
        }

        private string LookupName(string affixId)
        {
            if (string.IsNullOrWhiteSpace(affixId)) return string.Empty;
            return _affixNames.TryGetValue(affixId, out var name) ? name : _state.GetAffixName(affixId);
        }

        private void RefreshCommands()
        {
            foreach (var command in new[] { CaptureScanCommand, RescanAfterCraftingCommand, ConfirmCommand, SaveCommand, AddAffixCommand, ClearCommand, EditScanCommand })
            {
                (command as IRelayCommand)?.NotifyCanExecuteChanged();
            }
        }

        private string NameOf(ScannedAffix affix) =>
            !string.IsNullOrWhiteSpace(affix.DisplayName) ? affix.DisplayName : LookupName(affix.AffixId);

        private string FormatValue(ScannedAffix affix)
        {
            string value = affix.Value.HasValue ? AffixText.FormatValue(affix.Value, _state.IsPercentAffix(affix.AffixId)) : "no value";
            return affix.Kind is AffixKind.Normal or AffixKind.Unknown ? value : $"{value} ({affix.Kind.ToString().ToLowerInvariant()})";
        }

        private string Requirement(TargetAffix target)
        {
            var parts = new List<string>();
            if (target.RequireGreater) parts.Add("greater");
            if (target.IsTempered) parts.Add("tempered");
            if (target.IsImplicit) parts.Add("implicit");
            if (target.MinimumValue is double minimum) parts.Add($"at least {AffixText.FormatValue(minimum, _state.IsPercentAffix(target.AffixId))}");
            return parts.Count == 0 ? "In build" : "In build, " + string.Join(", ", parts);
        }

        private static string Join(IReadOnlyList<string>? values) =>
            values == null || values.Count == 0 ? string.Empty : string.Join(", ", values);
    }
}
