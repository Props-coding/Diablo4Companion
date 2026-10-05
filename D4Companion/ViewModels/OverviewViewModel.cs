using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4Companion.Crafting;
using D4Companion.State;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace D4Companion.ViewModels
{
    /// <summary>
    /// Overview: answers "What should I do with my gear next?"
    /// </summary>
    public class OverviewViewModel : ObservableObject
    {
        private readonly CompanionState _state;

        public OverviewViewModel(CompanionState state)
        {
            _state = state;
            _state.GearChanged += (s, e) => Refresh();
            _state.BuildTargetChanged += (s, e) => Refresh();
            _state.LiveScanChanged += (s, e) => OnPropertyChanged(nameof(ScanStatus));

            OpenAdvisorCommand = new RelayCommand(() => _state.Navigate(CompanionPage.Crafting));
            OpenGearCommand = new RelayCommand(() => _state.Navigate(CompanionPage.Gear));
            OpenBuildsCommand = new RelayCommand(() => _state.Navigate(CompanionPage.Builds));
            OpenItemCommand = new RelayCommand<GearRowViewModel>(row => { if (row != null) _state.OpenGear(row.Id); });

            Refresh();
        }

        public ICommand OpenAdvisorCommand { get; }
        public ICommand OpenGearCommand { get; }
        public ICommand OpenBuildsCommand { get; }
        public ICommand OpenItemCommand { get; }

        public string BuildName => string.IsNullOrWhiteSpace(_state.BuildTarget.Name) ? "No build selected" : _state.BuildTarget.Name;
        public bool HasBuild => !string.IsNullOrWhiteSpace(_state.BuildTarget.Name);
        public string ScanStatus => _state.LiveScan == null ? "Waiting for a scan" : "Scan ready";

        public int SavedCount { get; private set; }
        public int KeepCount { get; private set; }
        public int WorkCount { get; private set; }
        public int ReviewCount { get; private set; }

        public string Answer { get; private set; } = string.Empty;
        public string AnswerDetail { get; private set; } = string.Empty;

        /// <summary>
        /// Saved items that need something from the player, most urgent first.
        /// </summary>
        public ObservableCollection<GearRowViewModel> NextSteps { get; } = new();
        public bool HasNextSteps => NextSteps.Count > 0;

        public void Refresh()
        {
            var rows = _state.Store.Records
                .Where(r => r.Latest != null)
                .Select(r => GearViewModel.CreateRow(r, _state.BuildTarget))
                .ToList();

            SavedCount = rows.Count;
            KeepCount = rows.Count(r => r.Verdict == CraftingVerdict.MeetsTarget);
            WorkCount = rows.Count(r => r.Verdict == CraftingVerdict.NeedsWork);
            ReviewCount = rows.Count(r => r.Verdict is CraftingVerdict.NotConfirmed or CraftingVerdict.NeedsCorrection);

            NextSteps.Clear();
            foreach (var row in rows
                .Where(r => r.Verdict is CraftingVerdict.NotConfirmed or CraftingVerdict.NeedsCorrection or CraftingVerdict.NeedsWork)
                .OrderBy(r => r.Verdict == CraftingVerdict.NeedsWork ? 1 : 0)
                .ThenByDescending(r => r.LastChangedUtc))
            {
                NextSteps.Add(row);
            }

            if (!HasBuild)
            {
                Answer = "Choose a build first";
                AnswerDetail = "Import a build from Maxroll or Mobalytics, or pick one of your presets on the Builds page.";
            }
            else if (SavedCount == 0)
            {
                Answer = "Scan your first item";
                AnswerDetail = "Hover over an item in Diablo IV, then capture it in the Crafting Advisor.";
            }
            else if (ReviewCount > 0)
            {
                Answer = $"Check {ReviewCount} scanned item{(ReviewCount == 1 ? string.Empty : "s")}";
                AnswerDetail = "Some saved items are not confirmed or have scan mistakes. Confirm them before spending materials.";
            }
            else if (WorkCount > 0)
            {
                Answer = $"Review crafting options for {WorkCount} item{(WorkCount == 1 ? string.Empty : "s")}";
                AnswerDetail = "These items are missing build affixes or are below your targets.";
            }
            else
            {
                Answer = "Your saved gear meets your build";
                AnswerDetail = "No crafting is needed for the items you saved. Scan new drops to check them.";
            }

            OnPropertyChanged(string.Empty);
        }
    }
}
