using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4Companion.Crafting;
using D4Companion.State;
using D4Companion.ViewModels.Entities;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace D4Companion.ViewModels
{
    public sealed class GearRowViewModel
    {
        public Guid Id { get; init; }
        public string Slot { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Details { get; init; } = string.Empty;
        public string BuildMatch { get; init; } = string.Empty;
        public string NextAction { get; init; } = string.Empty;
        public CraftingVerdict Verdict { get; init; }
        public DateTime LastChangedUtc { get; init; }
    }

    /// <summary>
    /// My Gear: every saved item with its latest build match and next action.
    /// </summary>
    public class GearViewModel : ObservableObject
    {
        private readonly CompanionState _state;
        private GearRowViewModel? _selectedItem;

        public GearViewModel(CompanionState state)
        {
            _state = state;
            _state.GearChanged += (s, e) => Refresh();
            _state.BuildTargetChanged += (s, e) => Refresh();

            OpenCommand = new RelayCommand<GearRowViewModel>(row => { if (row != null) _state.OpenGear(row.Id); });
            DeleteCommand = new RelayCommand<GearRowViewModel>(Delete);
            ScanNewCommand = new RelayCommand(() => _state.Navigate(CompanionPage.Crafting));

            Refresh();
        }

        public ICommand OpenCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ScanNewCommand { get; }

        public ObservableCollection<GearRowViewModel> Items { get; } = new();
        public bool HasItems => Items.Count > 0;
        public string LoadWarning => string.Join(" ", _state.Store.LoadIssues);
        public bool HasLoadWarning => _state.Store.LoadIssues.Count > 0;
        public string BuildName => string.IsNullOrWhiteSpace(_state.BuildTarget.Name) ? "No build selected" : _state.BuildTarget.Name;

        public GearRowViewModel? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        public void Refresh()
        {
            Items.Clear();
            var slotOrder = CompanionState.GearSlots.ToList();
            var rows = _state.Store.Records
                .Where(r => r.Latest != null)
                .Select(r => CreateRow(r, _state.BuildTarget))
                .OrderBy(r => slotOrder.IndexOf(r.Slot) is int i && i >= 0 ? i : int.MaxValue)
                .ThenByDescending(r => r.LastChangedUtc);

            foreach (var row in rows) Items.Add(row);

            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(BuildName));
            OnPropertyChanged(nameof(LoadWarning));
            OnPropertyChanged(nameof(HasLoadWarning));
        }

        public static GearRowViewModel CreateRow(GearRecord record, BuildTarget build)
        {
            var latest = record.Latest!;
            var snapshot = latest.Snapshot;
            var analysis = CraftingAnalyzer.Analyze(snapshot, build);

            string nextAction = analysis.Verdict switch
            {
                CraftingVerdict.NotConfirmed => "Confirm values",
                CraftingVerdict.NeedsCorrection => "Correct scan",
                CraftingVerdict.MeetsTarget => "Keep",
                CraftingVerdict.NeedsWork => "Crafting options",
                CraftingVerdict.NoTargetForSlot => "No target",
                _ => "Review"
            };

            return new GearRowViewModel
            {
                Id = record.Id,
                Slot = record.ItemType,
                Title = CompanionState.SlotName(record.ItemType),
                Details = $"Item power {snapshot.ItemPower}, {snapshot.Affixes.Count} affixes, {record.Revisions.Count} saved version{(record.Revisions.Count == 1 ? string.Empty : "s")}",
                BuildMatch = analysis.Verdict == CraftingVerdict.NeedsWork
                    ? $"{analysis.MatchCount} of {analysis.TargetCount} match"
                    : CraftingText.VerdictLabel(analysis.Verdict),
                NextAction = nextAction,
                Verdict = analysis.Verdict,
                LastChangedUtc = latest.SavedAtUtc
            };
        }

        private void Delete(GearRowViewModel? row)
        {
            if (row == null) return;
            if (_state.Store.Delete(row.Id))
            {
                _state.NotifyGearChanged();
            }
        }
    }
}
