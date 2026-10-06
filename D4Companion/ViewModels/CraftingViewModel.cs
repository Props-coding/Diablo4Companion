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

    public sealed class PlanStepRowViewModel
    {
        public int Number { get; init; }
        public string Operation { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string AimFor { get; init; } = string.Empty;
        public bool HasAimFor => AimFor.Length > 0;
        public IReadOnlyList<PrismChip> Prism { get; init; } = Array.Empty<PrismChip>();
        public bool HasPrism => Prism.Count > 0;
        public string IfItWorks { get; init; } = string.Empty;
        public bool IsFirst => Number == 1;
    }

    /// <summary>Id null: not checked. Id empty: checked, no affix enchanted yet.</summary>
    public sealed record EnchantOption(string? Id, string Name);

    /// <summary>One answer for a crafting limit. Key "" is not checked, "yes" is allowed, "no" is blocked.</summary>
    public sealed record LimitOption(string Key, string Name);

    /// <summary>Something to check in the game before spending anything on the current step.</summary>
    public sealed class InGameCheckRowViewModel
    {
        public string Text { get; init; } = string.Empty;
        public bool IsDone { get; init; }
        public bool IsOpen => !IsDone;
    }

    public sealed class HistoryRowViewModel
    {
        public string Label { get; init; } = string.Empty;
        public string When { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string Tried { get; init; } = string.Empty;
        public bool HasTried => Tried.Length > 0;
        public string Result { get; init; } = string.Empty;
        public bool HasResult => Result.Length > 0;
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
        private string _uniqueId = string.Empty;
        private string _uniqueName = string.Empty;
        private bool _isConfirmed;
        private bool _hasUnsavedChanges;
        private Guid? _currentRecordId;
        private bool _isAfterCraftingDraft;
        private string _statusMessage = string.Empty;
        private CraftingAnalysis? _analysis;
        private DateTime _capturedAtUtc = DateTime.UtcNow;
        private bool _isEditing;
        private bool _includeGreaterAffixes;
        private ItemCraftState _craftState = new();
        // In-game checks the player has answered "yes" to for this item. Kept across rescans of the same item.
        private readonly HashSet<string> _confirmedChecks = new(StringComparer.Ordinal);
        // What the next saved version records as tried and what happened, for trial logs.
        private string _pendingAttempt = string.Empty;
        private string _pendingOutcome = string.Empty;

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
            RescanAfterCraftingCommand = new RelayCommand(RescanAfterCrafting, () => _state.LiveScan != null && HasItem);
            ConfirmCommand = new RelayCommand(Confirm, () => HasItem);
            SaveCommand = new RelayCommand(Save, () => HasItem);
            AddAffixCommand = new RelayCommand(AddAffix, () => HasItem);
            RemoveAffixCommand = new RelayCommand<ScannedAffixViewModel>(RemoveAffix);
            ClearCommand = new RelayCommand(Clear, () => HasItem);
            OpenBuildsCommand = new RelayCommand(() => _state.Navigate(CompanionPage.Builds));
            OpenGearCommand = new RelayCommand(() => _state.Navigate(CompanionPage.Gear));
            EditScanCommand = new RelayCommand(() => IsEditing = !IsEditing, () => HasItem);
            ConfirmCheckCommand = new RelayCommand<InGameCheckRowViewModel>(ConfirmCheck);
            RejectCheckCommand = new RelayCommand<InGameCheckRowViewModel>(RejectCheck);
            ClearRuledOutCommand = new RelayCommand(() => SetCraftState(_craftState with { RuledOut = Array.Empty<RuledOutStep>() }));
            CopyTrialLogCommand = new RelayCommand(CopyTrialLog);

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
        public ICommand ConfirmCheckCommand { get; }
        public ICommand RejectCheckCommand { get; }
        public ICommand ClearRuledOutCommand { get; }
        public ICommand CopyTrialLogCommand { get; }

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

        public string ItemName => !HasItem ? "No item yet"
            : _isUnique && _uniqueName.Length > 0 ? $"{_uniqueName} · {CompanionState.SlotName(_itemType)}"
            : CompanionState.SlotName(_itemType);

        public string ItemDetails
        {
            get
            {
                if (!HasItem) return "Capture a scan to start.";
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(_itemPowerText)) parts.Add($"{_itemPowerText} Item Power");
                if (!string.IsNullOrWhiteSpace(_rarity)) parts.Add(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(_rarity.ToLowerInvariant()));
                if (_isUnique && !string.Equals(_rarity, "Unique", StringComparison.OrdinalIgnoreCase)) parts.Add("Unique");
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
            ? "Changing anything will need a new confirmation."
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
                NextActionDetail = Instruction is CraftingInstruction step
                    ? (step.DesiredAffix.Length > 0 ? $"{step.Line}. Aim for {step.DesiredAffix}." : step.Line + ".")
                    : HasTargetStat ? $"Target: {TargetStat}" : Summary;
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

        // Crafting limits: what the player told us the item can no longer take.

        // "Not checked" is kept apart from "allowed": an unchecked limit becomes an in-game check on the step.

        public static IReadOnlyList<LimitOption> CanChangeOptions { get; } = new[]
        {
            new LimitOption("", "Not checked"), new LimitOption("yes", "Yes"), new LimitOption("no", "No, it's locked")
        };
        public static IReadOnlyList<LimitOption> TemperOptions { get; } = new[]
        {
            new LimitOption("", "Not checked"), new LimitOption("yes", "Yes, tempers left"), new LimitOption("no", "No tempers left")
        };
        public static IReadOnlyList<LimitOption> MasterworkOptions { get; } = new[]
        {
            new LimitOption("", "Not checked"), new LimitOption("yes", "Yes, ranks left"), new LimitOption("no", "Fully masterworked")
        };

        public string CanChangeKey
        {
            get => ToKey(_craftState.CannotBeModified);
            set => SetCraftState(_craftState with { CannotBeModified = FromKey(value) });
        }

        public string TempersKey
        {
            get => ToKey(_craftState.NoTempersLeft);
            set => SetCraftState(_craftState with { NoTempersLeft = FromKey(value) });
        }

        public string MasterworkKey
        {
            get => ToKey(_craftState.FullyMasterworked);
            set => SetCraftState(_craftState with { FullyMasterworked = FromKey(value) });
        }

        // The limits are stored as "blocked?": null not checked, false allowed ("yes"), true blocked ("no").
        private static string ToKey(bool? blocked) => blocked switch { null => "", false => "yes", true => "no" };
        private static bool? FromKey(string? key) => key switch { "yes" => false, "no" => true, _ => null };

        public ObservableCollection<EnchantOption> EnchantOptions { get; } = new();

        public EnchantOption? SelectedEnchant
        {
            get => EnchantOptions.FirstOrDefault(o => o.Id == null ? _craftState.EnchantedAffixId == null
                                                                   : string.Equals(o.Id, _craftState.EnchantedAffixId, StringComparison.OrdinalIgnoreCase))
                   ?? EnchantOptions.FirstOrDefault();
            set
            {
                if (value == null) return;
                SetCraftState(_craftState with { EnchantedAffixId = value.Id });
            }
        }

        public IReadOnlyList<string> RuledOut => _craftState.RuledOut.Select(r => r.Description).ToList();
        public bool HasRuledOut => _craftState.RuledOut.Count > 0;

        public string CraftLimitsSummary
        {
            get
            {
                var parts = new List<string>();
                if (_craftState.CannotBeModified == true) parts.Add("can't be changed");
                if (_craftState.HasEnchant) parts.Add("enchant used on " + LookupName(_craftState.EnchantedAffixId!));
                if (_craftState.NoTempersLeft == true) parts.Add("no tempers left");
                if (_craftState.FullyMasterworked == true) parts.Add("fully masterworked");
                if (_craftState.RuledOut.Count > 0) parts.Add($"{_craftState.RuledOut.Count} ruled out");

                int notChecked = new[] { _craftState.CannotBeModified, _craftState.NoTempersLeft, _craftState.FullyMasterworked }.Count(v => v == null)
                                 + (_craftState.EnchantChecked ? 0 : 1);
                if (notChecked == 4 && parts.Count == 0) return "Not checked";
                if (notChecked > 0) parts.Add($"{notChecked} not checked");
                return parts.Count == 0 ? "Checked, nothing blocked" : string.Join(" · ", parts);
            }
        }

        private void SetCraftState(ItemCraftState state)
        {
            if (state == _craftState) return;
            _craftState = state;
            HasUnsavedChanges = true;
            OnCraftStateChanged();
            Analyze();
        }

        private void OnCraftStateChanged()
        {
            OnPropertyChanged(nameof(CanChangeKey));
            OnPropertyChanged(nameof(TempersKey));
            OnPropertyChanged(nameof(MasterworkKey));
            OnPropertyChanged(nameof(SelectedEnchant));
            OnPropertyChanged(nameof(RuledOut));
            OnPropertyChanged(nameof(HasRuledOut));
            OnPropertyChanged(nameof(CraftLimitsSummary));
        }

        private void RefreshEnchantOptions()
        {
            EnchantOptions.Clear();
            EnchantOptions.Add(new EnchantOption(null, "Not checked"));
            EnchantOptions.Add(new EnchantOption(string.Empty, "None yet"));
            foreach (var row in Affixes.Where(a => !string.IsNullOrWhiteSpace(a.AffixId)))
            {
                if (EnchantOptions.Any(o => string.Equals(o.Id, row.AffixId, StringComparison.OrdinalIgnoreCase))) continue;
                EnchantOptions.Add(new EnchantOption(row.AffixId, LookupName(row.AffixId)));
            }
            OnPropertyChanged(nameof(SelectedEnchant));
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
        // The concrete crafting step, when one can be named.
        private CraftingInstruction? Instruction => ShowRecommendation ? _analysis?.Recommendation.Instruction : null;
        public bool HasInstruction => Instruction != null;
        /// <summary>The "Aim for" tag at the top, hidden when the step card already names the target.</summary>
        public bool ShowTargetTag => HasTargetStat && !HasInstruction;
        public string InstructionStation => Instruction?.Station ?? string.Empty;
        public string InstructionOperation => Instruction?.Operation ?? string.Empty;
        public string InstructionAffected => Instruction?.AffectedAffix ?? string.Empty;
        public bool HasInstructionAffected => InstructionAffected.Length > 0;
        public string InstructionDesired => Instruction?.DesiredAffix ?? string.Empty;
        public bool HasInstructionDesired => InstructionDesired.Length > 0;
        public IReadOnlyList<PrismChip> InstructionPrism => Instruction is { PrismId.Length: > 0 } step
            ? PrismChip.From(new[] { step.PrismId })
            : Array.Empty<PrismChip>();
        public bool HasInstructionPrism => InstructionPrism.Count > 0;
        public string InstructionReason => Instruction?.Reason ?? string.Empty;
        public string InstructionCaveat => Instruction?.Caveat ?? string.Empty;
        public IReadOnlyList<string> InstructionChecked => Instruction?.Checked ?? Array.Empty<string>();
        public bool HasInstructionChecked => InstructionChecked.Count > 0;
        public IReadOnlyList<string> InstructionConfirm => Instruction?.ConfirmInGame ?? Array.Empty<string>();
        public bool HasInstructionConfirm => InstructionConfirm.Count > 0;
        public string InstructionPlace => Instruction == null ? string.Empty : $"At the {Instruction.Station}";

        /// <summary>The step's in-game checks, each marked when the player has answered "yes".</summary>
        public IReadOnlyList<InGameCheckRowViewModel> InGameCheckRows =>
            InstructionConfirm.Select(c => new InGameCheckRowViewModel { Text = c, IsDone = _confirmedChecks.Contains(c) }).ToList();
        /// <summary>Something about the step is still unknown, so the next action is to check it in-game, not to craft.</summary>
        public bool NeedsInGameCheck => HasInstruction && InstructionConfirm.Any(c => !_confirmedChecks.Contains(c));
        public bool ShowCraftAction => HasInstruction && !NeedsInGameCheck;
        public string CardHeadline => NeedsInGameCheck ? "Check this option in-game" : Headline;
        public string ConditionalStep => NeedsInGameCheck ? $"If it checks out: {Headline}" : string.Empty;
        public bool HasCheckedInGame => HasInstruction && HasInstructionConfirm && !NeedsInGameCheck;

        // Kept separate: having the target affixes is not the same as meeting every requirement.
        public string AffixesStatement => ShowRecommendation ? _analysis?.AffixesStatement ?? string.Empty : string.Empty;
        public string ValuesStatement => ShowRecommendation ? _analysis?.ValuesStatement ?? string.Empty : string.Empty;
        public string GreaterStatement => ShowRecommendation ? _analysis?.GreaterStatement ?? string.Empty : string.Empty;
        public bool HasCompletion => AffixesStatement.Length > 0;
        public bool HasPlanCard => HasPlan || HasCompletion;
        /// <summary>Summary under the headline, only when there is no step to show (the step says it already).</summary>
        public bool ShowSummary => !HasInstruction;
        public bool ShowNextActionBar => !ShowRecommendation;
        public bool HasInstructionCaveat => InstructionCaveat.Length > 0;

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

        /// <summary>The full plan for this item: every step, each assuming the one before it worked.</summary>
        public ObservableCollection<PlanStepRowViewModel> PlanSteps { get; } = new();
        public bool HasPlan => ShowRecommendation && PlanSteps.Count > 0;
        public string PlanHeader => PlanSteps.Count == 1 ? "Full plan · 1 step" : $"Full plan · {PlanSteps.Count} steps";
        public string PlanEndNote { get; private set; } = string.Empty;
        public bool HasPlanEndNote => HasPlan && PlanEndNote.Length > 0;
        public string PlanDisclaimer => CraftingPlanner.PlanDisclaimer;
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

            ResetTrialState();
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
            if (live == null || !HasItem) return;

            // Rescanning needs a saved item to compare with. Save it first when it isn't saved yet.
            if (_currentRecordId == null || HasUnsavedChanges)
            {
                Save();
                if (_currentRecordId == null || HasUnsavedChanges) return;
            }

            var record = _state.Store.Find(_currentRecordId.Value);
            if (record == null) return;

            if (!string.Equals(record.ItemType, live.ItemType, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"The last scan is a {CompanionState.SlotName(live.ItemType).ToLowerInvariant()}, but this item is a {CompanionState.SlotName(record.ItemType).ToLowerInvariant()}. Hover over the crafted item and try again.";
                return;
            }

            var before = BuildSnapshot(out _);
            if (SameAffixes(before, live))
            {
                StatusMessage = "The scan looks the same as before. Do the step in the game, hover over the item again, then rescan.";
                return;
            }

            // Carry over Keep marks for affixes that are still on the item, and the item's crafting limits.
            var kept = Affixes.Where(a => a.IsKeep).Select(a => a.AffixId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var step = Instruction;
            var craftState = DetectEnchant(_analysis?.Recommendation, before, live, _craftState, out string enchantNote);
            var snapshot = live with
            {
                Affixes = live.Affixes.Select(a => a with { IsKeep = kept.Contains(a.AffixId) }).ToList(),
                CraftState = craftState
            };

            // Record what was tried and what the scans show happened, for the trial log.
            string result = string.Empty;
            if (step != null)
            {
                _pendingAttempt = CraftTrial.Attempt(step);
                _pendingOutcome = CraftTrial.Describe(CraftTrial.Judge(step, before, snapshot), step);
                result = _pendingOutcome + " ";
            }

            _isAfterCraftingDraft = true;
            LoadSnapshot(snapshot);
            HasUnsavedChanges = true;
            StatusMessage = result + "Check the new scan and confirm it to get the updated plan." + enchantNote;
            RefreshHistory();
        }

        /// <summary>
        /// After an enchant step, the Occultist can only change the enchanted affix again.
        /// Works out which affix that is: the original if it was kept, or the one new affix.
        /// </summary>
        private ItemCraftState DetectEnchant(CraftingRecommendation? previous, GearSnapshot before, GearSnapshot after, ItemCraftState state, out string note)
        {
            note = string.Empty;
            if (previous?.Instruction?.Operation != "Enchant" || previous.ReplaceCandidate == null || state.HasEnchant) return state;

            string original = previous.ReplaceCandidate.AffixId;
            bool stillThere = after.Affixes.Any(a => string.Equals(a.AffixId, original, StringComparison.OrdinalIgnoreCase));
            if (stillThere && before.Affixes.Select(a => a.AffixId).SequenceEqual(after.Affixes.Select(a => a.AffixId), StringComparer.OrdinalIgnoreCase))
            {
                note = $" If you enchanted {LookupName(original)} and kept it, it is now the enchanted affix. Check Crafting limits.";
                return state with { EnchantedAffixId = original };
            }

            var added = after.Affixes
                .Where(a => !before.Affixes.Any(b => string.Equals(b.AffixId, a.AffixId, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (added.Count == 1)
            {
                note = $" {LookupName(added[0].AffixId)} is marked as the enchanted affix. Change it under Crafting limits if that's wrong.";
                return state with { EnchantedAffixId = added[0].AffixId };
            }

            note = " Mark which affix you enchanted under Crafting limits.";
            return state;
        }

        private void ResetTrialState()
        {
            _confirmedChecks.Clear();
            _pendingAttempt = _pendingOutcome = string.Empty;
        }

        private void ConfirmCheck(InGameCheckRowViewModel? row)
        {
            if (row == null || !HasInstruction) return;
            _confirmedChecks.Add(row.Text);
            Analyze();
        }

        /// <summary>
        /// The player found in-game that a check fails. Limits about the item are set directly; anything else
        /// rules out this step (or the goal, when it can't roll on the item). The attempt is saved for the trial log.
        /// </summary>
        private void RejectCheck(InGameCheckRowViewModel? row)
        {
            if (row == null || Instruction is not CraftingInstruction step) return;

            // Save anything unsaved first, so a rescan result isn't overwritten by this entry.
            if (HasUnsavedChanges || _currentRecordId == null)
            {
                Save();
                if (HasUnsavedChanges || _currentRecordId == null) return;
            }

            string slot = CompanionState.SlotName(_itemType).ToLowerInvariant();
            string goalId = step.Goal?.AffixId ?? string.Empty;
            ItemCraftState state = row.Text switch
            {
                CraftingAnalyzer.CheckCanChange => _craftState with { CannotBeModified = true },
                CraftingAnalyzer.CheckTempersLeft => _craftState with { NoTempersLeft = true },
                CraftingAnalyzer.CheckMasterworkLeft => _craftState with { FullyMasterworked = true },
                _ when row.Text == step.GoalCheck && goalId.Length > 0 => _craftState with
                {
                    RuledOut = _craftState.RuledOut.Append(new RuledOutStep(RuledOutStep.GoalKey(goalId), $"{step.DesiredAffix} can't roll on this {slot}")).ToList()
                },
                _ => _craftState with
                {
                    RuledOut = _craftState.RuledOut.Append(new RuledOutStep(step.Key,
                        $"{step.Operation}{(step.AffectedAffix.Length > 0 ? " on " + step.AffectedAffix : string.Empty)} (failed check: {row.Text.TrimEnd('.')})")).ToList()
                }
            };

            _pendingAttempt = CraftTrial.Attempt(step);
            _pendingOutcome = $"Not possible in-game: {row.Text}";
            _isAfterCraftingDraft = false;
            SetCraftState(state);
            Save();
            StatusMessage = row.Text == CraftingAnalyzer.CheckNoEnchantYet
                ? "Noted. Mark which affix is enchanted under Crafting limits. The plan has been updated."
                : $"Noted that this isn't possible: {row.Text} The plan has been updated.";
        }

        private void CopyTrialLog()
        {
            var record = _currentRecordId is Guid id ? _state.Store.Find(id) : null;
            if (record == null)
            {
                StatusMessage = "Save the item first. The trial log is built from its saved versions.";
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(CraftTrial.FormatLog(record, ItemTitle, LookupName));
                StatusMessage = "Trial log copied. Paste it into your notes or a message.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name);
                StatusMessage = "Couldn't copy the trial log. Try again.";
            }
        }

        private static bool SameAffixes(GearSnapshot a, GearSnapshot b) =>
            a.Affixes.Count == b.Affixes.Count &&
            a.Affixes.Zip(b.Affixes).All(p => string.Equals(p.First.AffixId, p.Second.AffixId, StringComparison.OrdinalIgnoreCase)
                                              && p.First.Value == p.Second.Value && p.First.Kind == p.Second.Kind);

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
            // The "Confirmed" badge already says this. No second message.
            StatusMessage = string.Empty;
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
                    result = _isAfterCraftingDraft || _pendingOutcome.Length > 0
                        ? _state.Store.AddRevision(recordId, snapshot, _isAfterCraftingDraft ? "After crafting" : "Blocked in game", _pendingAttempt, _pendingOutcome)
                        : _state.Store.AddRevision(recordId, snapshot, "Corrected");
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
                _pendingAttempt = _pendingOutcome = string.Empty;
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
            _uniqueId = _uniqueName = string.Empty;
            ResetTrialState();
            _craftState = new ItemCraftState();
            EnchantOptions.Clear();
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

            ResetTrialState();
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
            _uniqueId = snapshot.UniqueId ?? string.Empty;
            _uniqueName = snapshot.UniqueName ?? string.Empty;
            _craftState = snapshot.CraftState ?? new ItemCraftState();
            RefreshEnchantOptions();
            OnCraftStateChanged();
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
            RefreshEnchantOptions();
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
                UniqueId = _isUnique ? _uniqueId : string.Empty,
                UniqueName = _isUnique ? _uniqueName : string.Empty,
                CraftState = _craftState,
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
            PlanSteps.Clear();
            PlanEndNote = string.Empty;
            foreach (var row in Affixes)
            {
                row.Status = null;
                row.IsOffTarget = false;
                row.IsProtected = false;
                row.Explanation = string.Empty;
            }

            if (HasItem && IsConfirmed)
            {
                try
                {
                    var snapshot = BuildSnapshot(out _) with { IsConfirmed = true };
                    var options = new AdvisorOptions { IncludeGreaterAffixes = _includeGreaterAffixes };
                    _analysis = CraftingAnalyzer.Analyze(snapshot, _state.BuildTarget, options);

                    var plan = CraftingPlanner.Plan(snapshot, _state.BuildTarget, options);
                    foreach (var step in plan.Steps)
                    {
                        var i = step.Instruction;
                        PlanSteps.Add(new PlanStepRowViewModel
                        {
                            Number = step.Number,
                            Operation = $"{i.Operation} at the {i.Station}",
                            Detail = i.AffectedAffix.Length > 0 ? $"Change {i.AffectedAffix}" : string.Empty,
                            AimFor = i.DesiredAffix,
                            Prism = i.PrismId.Length > 0 ? PrismChip.From(new[] { i.PrismId }) : Array.Empty<PrismChip>(),
                            IfItWorks = "If it works: " + step.IfItWorks
                        });
                    }
                    PlanEndNote = plan.EndNote;

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
                            Affixes[index].IsProtected = comparison.IsProtected;
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
            foreach (var name in new[] { nameof(HasPlan), nameof(PlanHeader), nameof(PlanEndNote), nameof(HasPlanEndNote),
                         nameof(HasInstruction), nameof(ShowTargetTag), nameof(InstructionStation), nameof(InstructionOperation),
                         nameof(InstructionAffected), nameof(HasInstructionAffected), nameof(InstructionDesired), nameof(HasInstructionDesired),
                         nameof(InstructionPrism), nameof(HasInstructionPrism), nameof(InstructionReason),
                         nameof(InstructionCaveat), nameof(HasInstructionCaveat),
                         nameof(InstructionChecked), nameof(HasInstructionChecked), nameof(InstructionConfirm), nameof(HasInstructionConfirm),
                         nameof(InstructionPlace), nameof(AffixesStatement), nameof(ValuesStatement), nameof(GreaterStatement),
                         nameof(HasCompletion), nameof(HasPlanCard), nameof(ShowSummary), nameof(ShowNextActionBar),
                         nameof(InGameCheckRows), nameof(NeedsInGameCheck), nameof(ShowCraftAction), nameof(CardHeadline),
                         nameof(ConditionalStep), nameof(HasCheckedInGame) })
            {
                OnPropertyChanged(name);
            }
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
                                  (revision.Snapshot.IsConfirmed ? ", confirmed" : string.Empty),
                        Tried = revision.Attempt.Length > 0 ? "Tried: " + revision.Attempt : string.Empty,
                        Result = revision.Outcome.Length > 0 ? "Result: " + revision.Outcome : string.Empty
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
