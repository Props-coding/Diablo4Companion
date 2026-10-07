using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using D4Companion.Crafting;
using D4Companion.Entities;
using D4Companion.Interfaces;
using D4Companion.Messages;
using D4Companion.State;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace D4Companion.ViewModels.Dialogs
{
    /// <summary>
    /// One version (profile or variant) of a downloaded build.
    /// </summary>
    public sealed class QuickImportVersionViewModel : ObservableObject
    {
        private bool _isSelected;

        public QuickImportVersionViewModel(string name, string detail, object source)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Unnamed version" : name;
            Detail = detail;
            Source = source;
        }

        public string Name { get; }
        public string Detail { get; }
        public object Source { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public sealed class QuickImportSiteViewModel : ObservableObject
    {
        private bool _isDetected;

        public QuickImportSiteViewModel(BuildSite site) => Site = site;

        public BuildSite Site { get; }
        public string Name => BuildLink.SiteName(Site);

        public bool IsDetected
        {
            get => _isDetected;
            set => SetProperty(ref _isDetected, value);
        }
    }

    /// <summary>
    /// The simple import window: paste a link, pick a version, name it.
    /// Uses the same downloaders and preset creation as the original import dialog.
    /// </summary>
    public class QuickImportViewModel : ObservableObject,
        IDisposable,
        IRecipient<MaxrollBuildsLoadedMessage>,
        IRecipient<D2CoreCompletedMessage>,
        IRecipient<D2CoreStatusUpdateMessage>,
        IRecipient<D4BuildsCompletedMessage>,
        IRecipient<D4BuildsStatusUpdateMessage>,
        IRecipient<InfinityBuildsCompletedMessage>,
        IRecipient<InfinityBuildsStatusUpdateMessage>,
        IRecipient<MobalyticsCompletedMessage>,
        IRecipient<MobalyticsStatusUpdateMessage>
    {
        private static readonly TimeSpan MaxrollTimeout = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan BrowserTimeout = TimeSpan.FromMinutes(3);

        private readonly IAffixManager _affixManager;
        private readonly IBuildsManagerD2Core _d2Core;
        private readonly IBuildsManagerD4Builds _d4Builds;
        private readonly IBuildsManagerInfinityBuilds _infinityBuilds;
        private readonly IBuildsManagerMaxroll _maxroll;
        private readonly IBuildsManagerMobalytics _mobalytics;
        private readonly CompanionState _state;
        private readonly ILogger _logger;

        private string _link = string.Empty;
        private BuildLinkInfo _linkInfo = BuildLink.Detect(string.Empty);
        private bool _isFetching;
        private string _status = string.Empty;
        private string _error = string.Empty;
        private string _buildTitle = string.Empty;
        private object? _build;
        private BuildSite _fetchSite = BuildSite.None;
        private HashSet<string> _snapshot = new();
        private int _fetchToken;
        private QuickImportVersionViewModel? _selectedVersion;
        private string _presetName = string.Empty;
        private bool _nameEdited;
        private bool _makeActive = true;

        public QuickImportViewModel(IAffixManager affixManager, IBuildsManagerD2Core d2Core, IBuildsManagerD4Builds d4Builds,
            IBuildsManagerInfinityBuilds infinityBuilds, IBuildsManagerMaxroll maxroll, IBuildsManagerMobalytics mobalytics,
            CompanionState state, ILogger logger)
        {
            _affixManager = affixManager;
            _d2Core = d2Core;
            _d4Builds = d4Builds;
            _infinityBuilds = infinityBuilds;
            _maxroll = maxroll;
            _mobalytics = mobalytics;
            _state = state;
            _logger = logger;

            Sites = new ObservableCollection<QuickImportSiteViewModel>(
                new[] { BuildSite.Maxroll, BuildSite.Mobalytics, BuildSite.D4Builds, BuildSite.D2Core, BuildSite.InfinityBuilds }
                .Select(s => new QuickImportSiteViewModel(s)));

            FetchCommand = new RelayCommand(FetchExecute, () => _linkInfo.IsValid && !IsFetching);
            SelectVersionCommand = new RelayCommand<QuickImportVersionViewModel>(SelectVersion);
            AddCommand = new RelayCommand(AddExecute, CanAdd);

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        public ICommand FetchCommand { get; }
        public ICommand SelectVersionCommand { get; }
        public ICommand AddCommand { get; }

        /// <summary>Set by the view: true once a build was added, so the dialog can close.</summary>
        public bool IsAdded { get; private set; }

        public ObservableCollection<QuickImportSiteViewModel> Sites { get; }
        public ObservableCollection<QuickImportVersionViewModel> Versions { get; } = new();

        public string Link
        {
            get => _link;
            set
            {
                if (!SetProperty(ref _link, value ?? string.Empty)) return;
                _linkInfo = BuildLink.Detect(_link);
                foreach (var site in Sites) site.IsDetected = site.Site == _linkInfo.Site;
                Error = string.Empty;
                OnPropertyChanged(nameof(LinkProblem));
                OnPropertyChanged(nameof(HasLinkProblem));
                ((RelayCommand)FetchCommand).NotifyCanExecuteChanged();
            }
        }

        public string LinkProblem => _linkInfo.Problem;
        public bool HasLinkProblem => _linkInfo.Problem.Length > 0;

        public bool IsFetching
        {
            get => _isFetching;
            private set
            {
                if (!SetProperty(ref _isFetching, value)) return;
                ((RelayCommand)FetchCommand).NotifyCanExecuteChanged();
                ((RelayCommand)AddCommand).NotifyCanExecuteChanged();
            }
        }

        public string Status
        {
            get => _status;
            private set
            {
                if (!SetProperty(ref _status, value)) return;
                OnPropertyChanged(nameof(HasStatus));
            }
        }

        public bool HasStatus => IsFetching && Status.Length > 0;

        public string Error
        {
            get => _error;
            private set
            {
                if (!SetProperty(ref _error, value)) return;
                OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => Error.Length > 0;

        public string BuildTitle
        {
            get => _buildTitle;
            private set => SetProperty(ref _buildTitle, value);
        }

        public bool HasBuild => _build != null;

        public QuickImportVersionViewModel? SelectedVersion
        {
            get => _selectedVersion;
            private set
            {
                if (!SetProperty(ref _selectedVersion, value)) return;
                foreach (var version in Versions) version.IsSelected = ReferenceEquals(version, value);
                if (!_nameEdited) SetSuggestedName();
                OnPropertyChanged(nameof(HasVersion));
                ((RelayCommand)AddCommand).NotifyCanExecuteChanged();
            }
        }

        public bool HasVersion => SelectedVersion != null;

        public string PresetName
        {
            get => _presetName;
            set
            {
                if (!SetProperty(ref _presetName, value ?? string.Empty)) return;
                _nameEdited = true;
                OnPropertyChanged(nameof(NameExists));
                ((RelayCommand)AddCommand).NotifyCanExecuteChanged();
            }
        }

        public bool NameExists => _affixManager.AffixPresets.Any(p => string.Equals(p.Name, PresetName.Trim(), StringComparison.Ordinal));

        public bool MakeActive
        {
            get => _makeActive;
            set => SetProperty(ref _makeActive, value);
        }

        // Fetch

        private void FetchExecute()
        {
            if (!_linkInfo.IsValid) return;

            ClearBuild();
            Error = string.Empty;
            _fetchSite = _linkInfo.Site;
            _snapshot = SnapshotKeys(_fetchSite);
            int token = ++_fetchToken;
            IsFetching = true;
            Status = $"Fetching from {BuildLink.SiteName(_fetchSite)}...";
            OnPropertyChanged(nameof(HasStatus));

            string key = _linkInfo.Key;
            BuildSite site = _fetchSite;
            _ = Task.Run(() =>
            {
                try
                {
                    switch (site)
                    {
                        case BuildSite.Maxroll: _maxroll.DownloadMaxrollBuild(key); break;
                        case BuildSite.Mobalytics: _mobalytics.DownloadMobalyticsBuild(key); break;
                        case BuildSite.D4Builds: _d4Builds.DownloadD4BuildsBuild(key); break;
                        case BuildSite.D2Core: _d2Core.DownloadD2CoreBuild(key); break;
                        case BuildSite.InfinityBuilds: _infinityBuilds.DownloadInfinityBuildsBuild(key); break;
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Quick import download failed");
                    OnUi(() => Fail(token));
                }
            });

            _ = WatchTimeout(token, site == BuildSite.Maxroll ? MaxrollTimeout : BrowserTimeout);
        }

        private async Task WatchTimeout(int token, TimeSpan timeout)
        {
            await Task.Delay(timeout);
            if (token == _fetchToken && IsFetching) Fail(token);
        }

        private void Fail(int token)
        {
            if (token != _fetchToken || !IsFetching) return;
            IsFetching = false;
            Status = string.Empty;
            Error = _fetchSite == BuildSite.Maxroll
                ? "Couldn't load this build. Check the link opens a Maxroll planner, then try again."
                : $"Couldn't load this build from {BuildLink.SiteName(_fetchSite)}. Check the link, then try again. The site may also be busy.";
        }

        // Called whenever a downloader reports it finished. Looks for the build that just arrived.
        private void OnDownloadFinished(BuildSite site, bool final)
        {
            if (!IsFetching || site != _fetchSite) return;

            int token = _fetchToken;
            if (TryResolve()) return;
            if (!final) return;

            // The list can reload just after the "done" signal. Give it a moment before giving up.
            _ = Task.Delay(1500).ContinueWith(_ => OnUi(() =>
            {
                if (token != _fetchToken || !IsFetching) return;
                if (!TryResolve()) Fail(token);
            }));
        }

        private bool TryResolve()
        {
            var found = FindFetchedBuild();
            if (found == null) return false;

            IsFetching = false;
            Status = string.Empty;
            ShowBuild(found.Value.Build, found.Value.Name, found.Value.Versions);
            return true;
        }

        private (object Build, string Name, List<QuickImportVersionViewModel> Versions)? FindFetchedBuild()
        {
            string key = _linkInfo.Key;
            switch (_fetchSite)
            {
                case BuildSite.Maxroll:
                {
                    var build = Pick(_maxroll.MaxrollBuilds, b => Key(b.Id, b.Date), b => Same(b.Id, key));
                    if (build == null) return null;
                    var versions = build.Data.Profiles
                        .Select(p => new QuickImportVersionViewModel(p.Name, Count(p.Items.Count, "item"), p)).ToList();
                    return (build, build.Name, versions);
                }
                case BuildSite.D2Core:
                {
                    var build = Pick(_d2Core.D2CoreBuilds, b => Key(b.Id, b.Date), b => Same(b.Id, key));
                    if (build == null) return null;
                    var versions = build.Data.Variants
                        .Select(v => new QuickImportVersionViewModel(v.Name, Count(v.Gear.Count, "gear slot"), v)).ToList();
                    return (build, build.Name, versions);
                }
                case BuildSite.D4Builds:
                {
                    var build = Pick(_d4Builds.D4BuildsBuilds, b => Key(b.Id, b.Date), b => Same(b.Id, key));
                    if (build == null) return null;
                    var versions = build.Variants
                        .Select(v => new QuickImportVersionViewModel(v.Name, Count(D4BuildsAffixCount(v), "affix", "affixes"), v)).ToList();
                    return (build, build.Name, versions);
                }
                case BuildSite.InfinityBuilds:
                {
                    var build = Pick(_infinityBuilds.InfinityBuildsBuilds, b => Key(b.Id, b.Date), b => Same(b.Url, key) || Same(b.Id, key));
                    if (build == null) return null;
                    var versions = build.Variants
                        .Select(v => new QuickImportVersionViewModel(v.Name, Count(InfinityAffixCount(v), "affix", "affixes"), v)).ToList();
                    return (build, build.Name, versions);
                }
                case BuildSite.Mobalytics:
                {
                    var build = Pick(_mobalytics.MobalyticsBuilds, b => Key(b.Id, b.Date), b => Same(b.Url, key) || Same(b.Id, key));
                    if (build == null) return null;
                    var versions = build.Variants
                        .Select(v => new QuickImportVersionViewModel(v.Name, Count(MobalyticsAffixCount(v), "affix", "affixes"), v)).ToList();
                    return (build, build.Name, versions);
                }
            }
            return null;
        }

        // Prefers a build that matches the link; otherwise one that is new or changed since the fetch started.
        private T? Pick<T>(IEnumerable<T> builds, Func<T, string> key, Func<T, bool> matchesLink) where T : class
        {
            var list = builds.ToList();
            var fresh = list.Where(b => !_snapshot.Contains(key(b))).ToList();
            return fresh.FirstOrDefault(matchesLink) ?? fresh.FirstOrDefault() ?? list.FirstOrDefault(matchesLink);
        }

        private HashSet<string> SnapshotKeys(BuildSite site) => site switch
        {
            BuildSite.Maxroll => _maxroll.MaxrollBuilds.Select(b => Key(b.Id, b.Date)).ToHashSet(),
            BuildSite.D2Core => _d2Core.D2CoreBuilds.Select(b => Key(b.Id, b.Date)).ToHashSet(),
            BuildSite.D4Builds => _d4Builds.D4BuildsBuilds.Select(b => Key(b.Id, b.Date)).ToHashSet(),
            BuildSite.InfinityBuilds => _infinityBuilds.InfinityBuildsBuilds.Select(b => Key(b.Id, b.Date)).ToHashSet(),
            BuildSite.Mobalytics => _mobalytics.MobalyticsBuilds.Select(b => Key(b.Id, b.Date)).ToHashSet(),
            _ => new HashSet<string>()
        };

        private static string Key(string id, string date) => $"{id}|{date}";

        private static bool Same(string a, string b) =>
            a.Length > 0 && string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

        private static string Count(int count, string singular, string? plural = null) =>
            count == 1 ? $"1 {singular}" : $"{count} {plural ?? singular + "s"}";

        private static int D4BuildsAffixCount(D4BuildsBuildVariant v) =>
            v.Helm.Count + v.Chest.Count + v.Gloves.Count + v.Pants.Count + v.Boots.Count + v.Amulet.Count +
            v.Ring.Count + v.Weapon.Count + v.Ranged.Count + v.Offhand.Count;

        private static int InfinityAffixCount(InfinityBuildsBuildVariant v) =>
            v.Helm.Count + v.Chest.Count + v.Gloves.Count + v.Pants.Count + v.Boots.Count + v.Amulet.Count +
            v.Ring.Count + v.Weapon.Count + v.Ranged.Count + v.Offhand.Count;

        private static int MobalyticsAffixCount(MobalyticsBuildVariant v) =>
            v.Helm.Count + v.Chest.Count + v.Gloves.Count + v.Pants.Count + v.Boots.Count + v.Amulet.Count +
            v.Ring.Count + v.Weapon.Count + v.Ranged.Count + v.Offhand.Count;

        private void ShowBuild(object build, string name, List<QuickImportVersionViewModel> versions)
        {
            _build = build;
            BuildTitle = string.IsNullOrWhiteSpace(name) ? "Untitled build" : name;
            Versions.Clear();
            foreach (var version in versions) Versions.Add(version);
            OnPropertyChanged(nameof(HasBuild));

            if (Versions.Count == 0)
            {
                Error = "This build has no versions to import.";
                return;
            }
            _nameEdited = false;
            SelectedVersion = Versions[0];
        }

        private void ClearBuild()
        {
            _build = null;
            BuildTitle = string.Empty;
            Versions.Clear();
            SelectedVersion = null;
            _nameEdited = false;
            _presetName = string.Empty;
            OnPropertyChanged(nameof(PresetName));
            OnPropertyChanged(nameof(NameExists));
            OnPropertyChanged(nameof(HasBuild));
        }

        private void SelectVersion(QuickImportVersionViewModel? version)
        {
            if (version != null) SelectedVersion = version;
        }

        private void SetSuggestedName()
        {
            string suggestion = BuildTitle;
            if (SelectedVersion != null && Versions.Count > 1 &&
                !BuildTitle.Contains(SelectedVersion.Name, StringComparison.OrdinalIgnoreCase))
            {
                suggestion = $"{BuildTitle} - {SelectedVersion.Name}";
            }
            _presetName = suggestion;
            OnPropertyChanged(nameof(PresetName));
            OnPropertyChanged(nameof(NameExists));
        }

        // Add

        private bool CanAdd() => !IsFetching && _build != null && SelectedVersion != null && !string.IsNullOrWhiteSpace(PresetName);

        private void AddExecute()
        {
            if (!CanAdd() || _build == null || SelectedVersion == null) return;

            string name = PresetName.Trim();
            try
            {
                switch (_build)
                {
                    case MaxrollBuild maxroll when SelectedVersion.Source is MaxrollBuildDataProfileJson profile:
                        _maxroll.CreatePresetFromMaxrollBuild(maxroll, profile.Name, name);
                        break;
                    case D2CoreBuild d2Core when SelectedVersion.Source is D2CoreBuildDataVariantJson variant:
                        _d2Core.CreatePresetFromD2CoreBuild(d2Core, variant.Name, name);
                        break;
                    case D4BuildsBuild d4Builds when SelectedVersion.Source is D4BuildsBuildVariant variant:
                        _d4Builds.CreatePresetFromD4BuildsBuild(variant, d4Builds.Name, name);
                        break;
                    case InfinityBuildsBuild infinity when SelectedVersion.Source is InfinityBuildsBuildVariant variant:
                        _infinityBuilds.CreatePresetFromInfinityBuildsBuild(variant, infinity.Name, name);
                        break;
                    case MobalyticsBuild mobalytics when SelectedVersion.Source is MobalyticsBuildVariant variant:
                        _mobalytics.CreatePresetFromMobalyticsBuild(variant, mobalytics.Name, name);
                        break;
                    default:
                        return;
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Quick import could not create the preset");
                Error = "Something went wrong while adding this build. Try again, or use Other options.";
                return;
            }

            if (MakeActive) _state.SelectPreset(name);
            IsAdded = true;
            AddCompleted?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? AddCompleted;

        // Messages

        public void Receive(MaxrollBuildsLoadedMessage message) => OnUi(() => OnDownloadFinished(BuildSite.Maxroll, false));
        public void Receive(D2CoreCompletedMessage message) => OnUi(() => OnDownloadFinished(BuildSite.D2Core, true));
        public void Receive(D4BuildsCompletedMessage message) => OnUi(() => OnDownloadFinished(BuildSite.D4Builds, true));
        public void Receive(InfinityBuildsCompletedMessage message) => OnUi(() => OnDownloadFinished(BuildSite.InfinityBuilds, true));
        public void Receive(MobalyticsCompletedMessage message) => OnUi(() => OnDownloadFinished(BuildSite.Mobalytics, true));

        public void Receive(D2CoreStatusUpdateMessage message) => ShowProgress(BuildSite.D2Core, message.Value.Status);
        public void Receive(D4BuildsStatusUpdateMessage message) => ShowProgress(BuildSite.D4Builds, message.Value.Status);
        public void Receive(InfinityBuildsStatusUpdateMessage message) => ShowProgress(BuildSite.InfinityBuilds, message.Value.Status);
        public void Receive(MobalyticsStatusUpdateMessage message) => ShowProgress(BuildSite.Mobalytics, message.Value.Status);

        private void ShowProgress(BuildSite site, string status) => OnUi(() =>
        {
            if (!IsFetching || site != _fetchSite || string.IsNullOrWhiteSpace(status)) return;
            Status = status;
            OnPropertyChanged(nameof(HasStatus));
        });

        private static void OnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        public void Dispose()
        {
            _fetchToken++;
            WeakReferenceMessenger.Default.UnregisterAll(this);
        }
    }
}
