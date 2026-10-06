using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D4Companion.Entities;
using D4Companion.Interfaces;
using D4Companion.State;
using D4Companion.ViewModels.Entities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace D4Companion.ViewModels
{
    public sealed class BuildCardViewModel
    {
        public string Name { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public bool IsActive { get; init; }
    }

    public sealed class BuildSlotViewModel : ObservableObject
    {
        private bool _isSelected;

        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Count { get; set; }
        public string Label => Count > 0 ? $"{Name} ({Count})" : Name;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public void Refresh() => OnPropertyChanged(nameof(Label));
    }

    public sealed class BuildAffixRowViewModel
    {
        public ItemAffix Model { get; init; } = new();
        public string Name { get; init; } = string.Empty;
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
        public IReadOnlyList<PrismChip> Prisms { get; init; } = Array.Empty<PrismChip>();
        public bool HasPrisms => Prisms.Count > 0;
        public string Note { get; init; } = string.Empty;
        public bool HasNote => Note.Length > 0;
        public bool IsDuplicate { get; init; }
    }

    public sealed class AffixSearchResultViewModel
    {
        public AffixInfo Model { get; init; } = new();
        public string Name { get; init; } = string.Empty;
        public string Classes { get; init; } = string.Empty;
    }

    /// <summary>
    /// Builds page: build cards, a clear import button, the active build's affixes per slot,
    /// and a searchable affix list. The original editor stays available for aspects, uniques and colours.
    /// </summary>
    public class BuildsViewModel : ObservableObject
    {
        // Index in AffixInfo.AllowedForPlayerClass, same order as the original editor.
        private static readonly (string Name, int Index)[] ClassIndexes =
        {
            ("Barbarian", 2), ("Druid", 1), ("Necromancer", 4), ("Paladin", 6),
            ("Rogue", 3), ("Sorcerer", 0), ("Spiritborn", 5), ("Warlock", 7)
        };

        private const int MaxSearchResults = 60;

        private readonly IAffixManager _affixManager;
        private readonly CompanionState _state;
        private readonly AffixViewModel _editor;

        private string _selectedSlot;
        private string _searchText = string.Empty;
        private string _selectedClass = "All classes";
        private bool _isAdvancedEditorOpen;

        public BuildsViewModel(IAffixManager affixManager, CompanionState state, AffixViewModel editor)
        {
            _affixManager = affixManager;
            _state = state;
            _editor = editor;
            _selectedSlot = CompanionState.GearSlots[0];

            foreach (var slot in CompanionState.GearSlots)
            {
                Slots.Add(new BuildSlotViewModel { Id = slot, Name = CompanionState.SlotName(slot), IsSelected = slot == _selectedSlot });
            }

            ClassOptions = new[] { "All classes" }.Concat(ClassIndexes.Select(c => c.Name)).ToList();

            ImportCommand = new RelayCommand(() => _editor.QuickImportCommand.Execute(null));
            SelectBuildCommand = new RelayCommand<BuildCardViewModel>(card => { if (card != null) _state.SelectPreset(card.Name); });
            SelectSlotCommand = new RelayCommand<BuildSlotViewModel>(SelectSlot);
            RemoveAffixCommand = new RelayCommand<BuildAffixRowViewModel>(RemoveAffix);
            AddAffixCommand = new RelayCommand<AffixSearchResultViewModel>(AddAffix);
            ToggleAdvancedEditorCommand = new RelayCommand(() => IsAdvancedEditorOpen = !IsAdvancedEditorOpen);

            _state.BuildTargetChanged += (s, e) => Refresh();
            _state.AffixCatalogChanged += (s, e) => { Refresh(); RefreshSearch(); };

            Refresh();
            RefreshSearch();
        }

        public ICommand ImportCommand { get; }
        public ICommand SelectBuildCommand { get; }
        public ICommand SelectSlotCommand { get; }
        public ICommand RemoveAffixCommand { get; }
        public ICommand AddAffixCommand { get; }
        public ICommand ToggleAdvancedEditorCommand { get; }

        /// <summary>
        /// The original editor's view model, shown when the advanced editor is open.
        /// </summary>
        public AffixViewModel Editor => _editor;

        public ObservableCollection<BuildCardViewModel> Builds { get; } = new();
        public bool HasBuilds => Builds.Count > 0;
        public bool HasActiveBuild => Builds.Any(b => b.IsActive);
        public string ActiveBuildName => Builds.FirstOrDefault(b => b.IsActive)?.Name ?? string.Empty;

        public ObservableCollection<BuildSlotViewModel> Slots { get; } = new();
        public ObservableCollection<BuildAffixRowViewModel> SlotAffixes { get; } = new();
        public bool HasSlotAffixes => SlotAffixes.Count > 0;
        public string SelectedSlotName => CompanionState.SlotName(_selectedSlot);
        public string AddTargetText => $"Adds to: {SelectedSlotName}";

        public IReadOnlyList<string> ClassOptions { get; }
        public ObservableCollection<AffixSearchResultViewModel> SearchResults { get; } = new();
        public string SearchHint { get; private set; } = string.Empty;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty)) RefreshSearch();
            }
        }

        public string SelectedClass
        {
            get => _selectedClass;
            set
            {
                if (SetProperty(ref _selectedClass, value ?? "All classes")) RefreshSearch();
            }
        }

        public bool IsAdvancedEditorOpen
        {
            get => _isAdvancedEditorOpen;
            set
            {
                if (SetProperty(ref _isAdvancedEditorOpen, value))
                {
                    OnPropertyChanged(nameof(IsMainOpen));
                    OnPropertyChanged(nameof(AdvancedEditorButtonText));
                }
            }
        }

        public bool IsMainOpen => !_isAdvancedEditorOpen;
        public string AdvancedEditorButtonText => _isAdvancedEditorOpen ? "Back to builds" : "Advanced editor";

        private void SelectSlot(BuildSlotViewModel? slot)
        {
            if (slot == null) return;
            _selectedSlot = slot.Id;
            foreach (var s in Slots) s.IsSelected = s.Id == slot.Id;
            RefreshSlotAffixes();
            OnPropertyChanged(nameof(SelectedSlotName));
            OnPropertyChanged(nameof(AddTargetText));
        }

        private void RemoveAffix(BuildAffixRowViewModel? row)
        {
            if (row == null) return;
            _affixManager.RemoveAffix(row.Model);
        }

        private void AddAffix(AffixSearchResultViewModel? result)
        {
            if (result == null || ActivePreset == null) return;
            _affixManager.AddAffix(result.Model, _selectedSlot);
        }

        private AffixPreset? ActivePreset =>
            _affixManager.AffixPresets.FirstOrDefault(p => p.Name.Equals(_state.SelectedPresetName));

        private void Refresh()
        {
            Builds.Clear();
            foreach (var preset in _affixManager.AffixPresets)
            {
                int slots = preset.ItemAffixes.Select(a => a.Type).Distinct().Count();
                Builds.Add(new BuildCardViewModel
                {
                    Name = preset.Name,
                    Summary = preset.ItemAffixes.Count == 0 ? "No affixes yet" : $"{preset.ItemAffixes.Count} affixes across {slots} slots",
                    IsActive = preset.Name.Equals(_state.SelectedPresetName)
                });
            }

            var affixes = ActivePreset?.ItemAffixes ?? new List<ItemAffix>();
            foreach (var slot in Slots)
            {
                slot.Count = affixes.Count(a => a.Type.Equals(slot.Id, StringComparison.OrdinalIgnoreCase));
                slot.Refresh();
            }

            RefreshSlotAffixes();
            OnPropertyChanged(nameof(HasBuilds));
            OnPropertyChanged(nameof(HasActiveBuild));
            OnPropertyChanged(nameof(ActiveBuildName));
        }

        private void RefreshSlotAffixes()
        {
            SlotAffixes.Clear();
            var preset = ActivePreset;
            if (preset != null)
            {
                var inSlot = preset.ItemAffixes
                    .Where(a => a.Type.Equals(_selectedSlot, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var seen = new HashSet<(string, bool, bool, bool)>();
                foreach (var affix in inSlot)
                {
                    var key = (affix.Id.ToLowerInvariant(), affix.IsImplicit, affix.IsTempered, affix.IsGreater);
                    bool isDuplicate = !seen.Add(key);
                    bool hasOtherForm = inSlot.Any(o => !ReferenceEquals(o, affix) && o.Id.Equals(affix.Id, StringComparison.OrdinalIgnoreCase) &&
                        (o.IsImplicit != affix.IsImplicit || o.IsTempered != affix.IsTempered));

                    string note = isDuplicate
                        ? "Listed twice, probably from the import. It is only counted once. You can remove this copy."
                        : hasOtherForm
                            ? "This build wants this stat more than once on this slot, for example as a regular and a tempered affix. Each is checked separately."
                            : string.Empty;

                    string name = _state.GetAffixName(affix.Id);
                    SlotAffixes.Add(new BuildAffixRowViewModel
                    {
                        Model = affix,
                        Name = string.IsNullOrWhiteSpace(name) ? "Unknown affix" : name,
                        Tags = Tags(affix),
                        Prisms = PrismChip.From(_state.GetPrisms(affix.Id)),
                        Note = note,
                        IsDuplicate = isDuplicate
                    });
                }
            }

            OnPropertyChanged(nameof(HasSlotAffixes));
        }

        private static IReadOnlyList<string> Tags(ItemAffix affix)
        {
            var tags = new List<string>();
            tags.Add(affix.IsImplicit ? "Implicit" : affix.IsTempered ? "Tempered" : "Regular");
            if (affix.IsGreater) tags.Add("Greater wanted");
            if (affix.IsAnyType) tags.Add("Any slot");
            return tags;
        }

        private void RefreshSearch()
        {
            SearchResults.Clear();

            var keywords = _searchText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int classIndex = ClassIndexes.FirstOrDefault(c => c.Name == _selectedClass).Name == null ? -1
                : ClassIndexes.First(c => c.Name == _selectedClass).Index;

            var matches = _affixManager.Affixes
                .Where(a => !string.IsNullOrWhiteSpace(a.IdName))
                .Where(a => classIndex < 0 || (a.AllowedForPlayerClass.Count > classIndex && a.AllowedForPlayerClass[classIndex] == 1))
                .Select(a => new { Info = a, Name = _state.GetAffixName(a.IdName) })
                .Where(a => a.Name.Length > 0 && keywords.All(k => a.Name.Contains(k, StringComparison.CurrentCultureIgnoreCase)))
                .GroupBy(a => a.Info.IdName)
                .Select(g => g.First())
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var match in matches.Take(MaxSearchResults))
            {
                SearchResults.Add(new AffixSearchResultViewModel
                {
                    Model = match.Info,
                    Name = match.Name,
                    Classes = ClassText(match.Info)
                });
            }

            SearchHint = matches.Count == 0 ? "No affixes match your search."
                : matches.Count > MaxSearchResults ? $"Showing {MaxSearchResults} of {matches.Count}. Type more to narrow it down."
                : $"{matches.Count} affixes";
            OnPropertyChanged(nameof(SearchHint));
        }

        private static string ClassText(AffixInfo info)
        {
            var allowed = info.AllowedForPlayerClass;
            if (allowed.Count == 0 || allowed.All(c => c == 1) || allowed.All(c => c == 0)) return "All classes";
            return string.Join(", ", ClassIndexes.Where(c => c.Index < allowed.Count && allowed[c.Index] == 1).Select(c => c.Name));
        }
    }
}
