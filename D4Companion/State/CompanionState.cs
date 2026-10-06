using CommunityToolkit.Mvvm.Messaging;
using D4Companion.Constants;
using D4Companion.Crafting;
using D4Companion.Entities;
using D4Companion.Interfaces;
using D4Companion.Messages;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace D4Companion.State
{
    public enum CompanionPage
    {
        Overview,
        Gear,
        Crafting,
        Builds,
        Settings,
        Trading,
        Logging,
        Debug
    }

    /// <summary>
    /// Shared state for Overview, My Gear and Crafting Advisor.
    /// Registered as a singleton so all three screens see the same scan, build and saved gear.
    /// Every event is raised on the UI thread.
    /// </summary>
    public class CompanionState
    {
        public static readonly IReadOnlyList<string> GearSlots = new[]
        {
            ItemTypeConstants.Helm, ItemTypeConstants.Chest, ItemTypeConstants.Gloves, ItemTypeConstants.Pants,
            ItemTypeConstants.Boots, ItemTypeConstants.Amulet, ItemTypeConstants.Ring, ItemTypeConstants.Weapon,
            ItemTypeConstants.Ranged, ItemTypeConstants.Offhand
        };

        private readonly ILogger _logger;
        private readonly IAffixManager _affixManager;
        private readonly ISettingsManager _settingsManager;

        private string _lastLiveSignature = string.Empty;

        public CompanionState(ILogger<CompanionState> logger, IAffixManager affixManager, ISettingsManager settingsManager)
        {
            _logger = logger;
            _affixManager = affixManager;
            _settingsManager = settingsManager;

            Store = new GearStore(Path.Combine("Config", "Gear.json"));
            try
            {
                Store.Load();
                foreach (var issue in Store.LoadIssues)
                {
                    _logger.LogWarning(issue);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name);
            }

            BuildTarget = CreateBuildTarget();

            WeakReferenceMessenger.Default.Register<TooltipDataReadyMessage>(this, HandleTooltipDataReadyMessage);
            WeakReferenceMessenger.Default.Register<ToggleOverlayFromGUIMessage>(this, (r, m) => OnUiThread(() =>
            {
                IsScannerOn = m.Value.IsEnabled;
                ScannerStateChanged?.Invoke(this, EventArgs.Empty);
            }));
            WeakReferenceMessenger.Default.Register<SelectedAffixPresetUpdatedMessage>(this, (r, m) => RefreshBuildTarget());
            WeakReferenceMessenger.Default.Register<AffixPresetChangedMessage>(this, (r, m) => RefreshBuildTarget());
            WeakReferenceMessenger.Default.Register<AffixPresetAddedMessage>(this, (r, m) => RefreshBuildTarget());
            WeakReferenceMessenger.Default.Register<AffixPresetRemovedMessage>(this, (r, m) => RefreshBuildTarget());
            WeakReferenceMessenger.Default.Register<SelectedAffixesChangedMessage>(this, (r, m) => RefreshBuildTarget());
            WeakReferenceMessenger.Default.Register<ApplicationLoadedMessage>(this, (r, m) => RefreshBuildTarget());
            WeakReferenceMessenger.Default.Register<AffixLanguageChangedMessage>(this, (r, m) => OnUiThread(() =>
            {
                _affixTexts = null;
                AffixCatalogChanged?.Invoke(this, EventArgs.Empty);
                RefreshBuildTarget();
            }));
        }

        public event EventHandler? LiveScanChanged;
        public event EventHandler? ScannerStateChanged;
        public event EventHandler? BuildTargetChanged;
        public event EventHandler? GearChanged;
        public event EventHandler? AffixCatalogChanged;
        public event EventHandler<CompanionPage>? NavigationRequested;
        public event EventHandler<Guid>? OpenGearRequested;

        public GearStore Store { get; }

        /// <summary>
        /// The most recent item read by the OCR scanner. Changes whenever the mouse moves to another item.
        /// </summary>
        public GearSnapshot? LiveScan { get; private set; }
        public DateTime? LiveScanAtLocal { get; private set; }

        /// <summary>
        /// Copy of the selected affix preset. Never shares objects with the preset itself.
        /// </summary>
        public BuildTarget BuildTarget { get; private set; }

        public IReadOnlyList<string> PresetNames => _affixManager.AffixPresets.Select(p => p.Name).ToList();
        public string SelectedPresetName => _settingsManager.Settings.SelectedAffixPreset;

        /// <summary>
        /// The OCR scanner only runs while the overlay is on. The original overlay toggle stays the owner of this state.
        /// </summary>
        public bool IsScannerOn { get; private set; }

        public void SetScanner(bool isOn)
        {
            // Same message the overlay hotkey uses, so the Builds page toggle, overlay and scanner stay in sync.
            WeakReferenceMessenger.Default.Send(new ToggleOverlayMessage(new ToggleOverlayMessageParams
            {
                IsEnabled = isOn
            }));
        }

        public void SelectPreset(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName) || presetName == SelectedPresetName) return;

            WeakReferenceMessenger.Default.Send(new SelectAffixPresetRequestMessage(new AffixPresetChangedMessageParams
            {
                PresetName = presetName
            }));
        }

        public void Navigate(CompanionPage page) => NavigationRequested?.Invoke(this, page);

        public void OpenGear(Guid recordId)
        {
            OpenGearRequested?.Invoke(this, recordId);
            Navigate(CompanionPage.Crafting);
        }

        public void NotifyGearChanged() => GearChanged?.Invoke(this, EventArgs.Empty);

        public string GetAffixName(string affixId)
        {
            if (string.IsNullOrWhiteSpace(affixId)) return string.Empty;
            // Never show raw ids to the user; callers fall back to the scanned text.
            return FindAffixText(affixId)?.Name ?? string.Empty;
        }

        /// <summary>
        /// True when the affix value is a percentage, so values can be shown as "6.3%".
        /// </summary>
        public bool IsPercentAffix(string affixId) =>
            !string.IsNullOrWhiteSpace(affixId) && (FindAffixText(affixId)?.IsPercent ?? false);

        /// <summary>
        /// From the game data: whether this stat can be added by tempering. Null when the affix isn't in the data.
        /// </summary>
        public bool? CanBeTempered(string affixId)
        {
            if (string.IsNullOrWhiteSpace(affixId)) return null;
            var affix = _affixManager.Affixes.FirstOrDefault(a => a.IdName.Equals(affixId, StringComparison.OrdinalIgnoreCase))
                ?? _affixManager.GetAffixInfoByIdName(affixId);
            return affix?.IsTemperingAvailable;
        }

        public IReadOnlyList<string> GetPrisms(string affixId)
        {
            if (string.IsNullOrWhiteSpace(affixId)) return Array.Empty<string>();
            return _affixManager.GetAffixTuningPrismsByIdName(affixId).ToList();
        }

        public IReadOnlyList<(string Id, string Name)> GetAffixCatalog()
        {
            return AffixTexts.Values
                .Select(a => (a.Id, a.Name))
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private sealed record AffixTextInfo(string Id, string Name, bool IsPercent);

        private Dictionary<string, AffixTextInfo>? _affixTexts;

        /// <summary>
        /// Readable affix names by full id. "+#% Maximum Life" and "+# Maximum Life" both read
        /// "Maximum Life", so the percent one gets "(%)" when names would otherwise repeat.
        /// </summary>
        private Dictionary<string, AffixTextInfo> AffixTexts
        {
            get
            {
                if (_affixTexts != null) return _affixTexts;

                var entries = _affixManager.Affixes
                    .Where(a => !string.IsNullOrWhiteSpace(a.IdName))
                    .GroupBy(a => a.IdName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Select(a => new AffixTextInfo(a.IdName, AffixText.ReadableName(a.Description), AffixText.IsPercent(a.Description)))
                    .Where(a => a.Name.Length > 0)
                    .ToList();

                var repeated = entries.GroupBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

                _affixTexts = entries
                    .Select(e => repeated.Contains(e.Name) && e.IsPercent ? e with { Name = e.Name + " (%)" } : e)
                    .ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
                return _affixTexts;
            }
        }

        private AffixTextInfo? FindAffixText(string affixId)
        {
            if (AffixTexts.TryGetValue(affixId, out var info)) return info;

            // Older saved data may hold a single id from the joined list.
            var affix = _affixManager.GetAffixInfoByIdName(affixId);
            return affix != null && AffixTexts.TryGetValue(affix.IdName, out info) ? info : null;
        }

        public static string SlotName(string itemType) => itemType switch
        {
            ItemTypeConstants.Helm => "Helm",
            ItemTypeConstants.Chest => "Chest",
            ItemTypeConstants.Gloves => "Gloves",
            ItemTypeConstants.Pants => "Pants",
            ItemTypeConstants.Boots => "Boots",
            ItemTypeConstants.Amulet => "Amulet",
            ItemTypeConstants.Ring => "Ring",
            ItemTypeConstants.Weapon => "Weapon",
            ItemTypeConstants.Ranged => "Ranged weapon",
            ItemTypeConstants.Offhand => "Offhand",
            "" => "Unknown slot",
            _ => itemType
        };

        private void HandleTooltipDataReadyMessage(object recipient, TooltipDataReadyMessage message)
        {
            try
            {
                // Copy everything we need right away, on the scanner thread. No references to the tooltip are kept.
                var snapshot = MapTooltip(message.Value.Tooltip);
                if (snapshot == null) return;

                string signature = Signature(snapshot);
                if (signature == _lastLiveSignature) return;
                _lastLiveSignature = signature;

                OnUiThread(() =>
                {
                    LiveScan = snapshot;
                    LiveScanAtLocal = DateTime.Now;
                    LiveScanChanged?.Invoke(this, EventArgs.Empty);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name);
            }
        }

        private GearSnapshot? MapTooltip(ItemTooltipDescriptor? tooltip)
        {
            if (tooltip == null || string.IsNullOrWhiteSpace(tooltip.ItemType)) return null;
            if (!GearSlots.Contains(tooltip.ItemType)) return null;

            var affixes = new List<ScannedAffix>();
            var areas = tooltip.ItemAffixAreas.ToList();
            var detected = tooltip.ItemAffixes.ToList();
            var ocr = tooltip.OcrResultAffixes.ToList();

            for (int i = 0; i < areas.Count; i++)
            {
                var area = areas[i];
                string affixId = detected.FirstOrDefault(a => a.Item1 == i)?.Item2?.Id ?? string.Empty;
                var ocrResult = ocr.FirstOrDefault(o => o.AreaIndex == i)?.OcrResult;
                if (string.IsNullOrWhiteSpace(affixId)) affixId = ocrResult?.AffixId ?? string.Empty;

                double value = area.AffixValue;
                if ((value == 0 || !double.IsFinite(value)) && ocrResult != null) value = ocrResult.TextValue;

                affixes.Add(new ScannedAffix
                {
                    AffixId = affixId,
                    DisplayName = GetAffixName(affixId),
                    OcrText = ocrResult?.Text ?? string.Empty,
                    Value = double.IsFinite(value) && value != 0 ? value : null,
                    Kind = ToKind(area.AffixType),
                    TuningPrisms = GetPrisms(affixId)
                });
            }

            return GearValidator.Sanitize(new GearSnapshot
            {
                ItemType = tooltip.ItemType,
                ItemPower = tooltip.ItemPower,
                Rarity = tooltip.IsUniqueItem ? ItemRarityConstants.Unique : tooltip.ItemRarity,
                IsUnique = tooltip.IsUniqueItem,
                Affixes = affixes,
                IsConfirmed = false
            });
        }

        private static AffixKind ToKind(string affixType) => affixType switch
        {
            AffixTypeConstants.Normal => AffixKind.Normal,
            AffixTypeConstants.Greater => AffixKind.Greater,
            AffixTypeConstants.Tempered => AffixKind.Tempered,
            AffixTypeConstants.Implicit => AffixKind.Implicit,
            _ => AffixKind.Unknown
        };

        private static string Signature(GearSnapshot snapshot) =>
            $"{snapshot.ItemType}|{snapshot.ItemPower}|{snapshot.Rarity}|" +
            string.Join(";", snapshot.Affixes.Select(a => $"{a.AffixId}:{a.Kind}:{a.Value}"));

        private void RefreshBuildTarget()
        {
            OnUiThread(() =>
            {
                BuildTarget = CreateBuildTarget();
                BuildTargetChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        private BuildTarget CreateBuildTarget()
        {
            try
            {
                string name = _settingsManager.Settings.SelectedAffixPreset;
                var preset = _affixManager.AffixPresets.FirstOrDefault(p => p.Name.Equals(name));
                if (preset == null) return new BuildTarget();

                bool useMinimum = _settingsManager.Settings.IsMinimalAffixValueFilterEnabled;

                // Copy only plain values. The preset's ItemAffix objects are editable elsewhere in the app.
                var targets = preset.ItemAffixes
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Id))
                    .Select(a =>
                    {
                        double minimum = useMinimum ? _affixManager.GetAffixMinimalValue(a.Id) : 0;
                        return new TargetAffix
                        {
                            AffixId = a.Id,
                            DisplayName = GetAffixName(a.Id),
                            ItemType = a.Type,
                            RequireGreater = a.IsGreater,
                            IsTempered = a.IsTempered,
                            IsImplicit = a.IsImplicit,
                            MinimumValue = minimum > 0 ? minimum : null,
                            CanBeTempered = CanBeTempered(a.Id),
                            TuningPrisms = GetPrisms(a.Id)
                        };
                    })
                    // An import can list the same affix twice for a slot. Count it once.
                    .GroupBy(t => (Id: t.AffixId.ToLowerInvariant(), Type: t.ItemType.ToLowerInvariant(), t.IsImplicit, t.IsTempered))
                    .Select(g => g.First() with { RequireGreater = g.Any(t => t.RequireGreater) })
                    .ToList();

                return new BuildTarget { Name = preset.Name, Affixes = targets };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name);
                return new BuildTarget();
            }
        }

        private static void OnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.BeginInvoke(action);
            }
        }
    }
}
