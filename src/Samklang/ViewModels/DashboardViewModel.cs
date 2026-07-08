using System.Collections.ObjectModel;
using System.ComponentModel;
using Samklang.Domain;
using Samklang.Resolver.Catalog;
using Samklang.Sessions;
using Samklang.SettingsManagement;
using Samklang.Timing;

namespace Samklang.ViewModels;

/// <summary>
/// The now-playing panel and recent-switch history, driven entirely by
/// <see cref="TrackSyncCoordinator"/>'s notifying properties — this view model has no timers or
/// I/O of its own, it only reacts to <see cref="TrackSyncCoordinator.PropertyChanged"/> (issue
/// #9's "reflects live state changes without restart" acceptance criterion).
///
/// <para>
/// <b>Thread marshaling.</b> <see cref="TrackSyncCoordinator"/>'s events can fire off the UI
/// thread (the SMTC/COM watcher underneath it), but this view model's bound properties —
/// especially <see cref="History"/>, an <see cref="ObservableCollection{T}"/> that a WPF
/// <c>ListView</c> enumerates live — must only ever be touched from the UI thread. Rather than
/// hard-coding a WPF <c>Dispatcher</c> call in here (which would make this class need a real
/// message loop to unit-test), the caller supplies a <paramref name="uiThreadInvoker"/> that wraps
/// every reaction: production code passes <c>action =&gt; Dispatcher.BeginInvoke(action)</c>,
/// tests pass nothing and get direct synchronous invocation, so firing a fake watcher event
/// updates this view model's state immediately and assertably.
/// </para>
/// </summary>
public sealed class DashboardViewModel : ViewModelBase
{
    /// <summary>How many recent switches the history list keeps before trimming the oldest.</summary>
    public const int MaxHistoryEntries = 25;

    private const string NoTrackDisplay = "(none — waiting for Apple Music)";

    private readonly TrackSyncCoordinator _coordinator;
    private readonly SettingsManager? _settingsManager;
    private readonly IClock _clock;
    private readonly Action<Action> _uiThreadInvoker;
    private readonly IAppleMusicTrackLauncher? _trackLauncher;

    /// <summary>
    /// The current album's tracks as last delivered by
    /// <see cref="CatalogFormatResolverLayer.AlbumTracksAvailable"/> (via
    /// <see cref="OnAlbumTracksAvailable"/>) — kept in candidate form so track changes can be
    /// re-matched against it with <see cref="CatalogTrackMatcher"/>.
    /// </summary>
    private IReadOnlyList<CatalogSearchCandidate> _albumCandidates = [];

    private string _trackDisplay = NoTrackDisplay;
    private string _targetFormatDisplay = "—";
    private bool _hasClampedFormat;
    private string _clampedFormatDisplay = string.Empty;
    private string _confidenceDisplay = "—";
    private string _sourceLayerDisplay = "—";
    private string _audioTierDisplay = AudioTier.Unknown.ToDisplayString();
    private string _deviceFormatDisplay = "—";
    private bool _hasDeviceTargetWarning;
    private string _deviceTargetWarningMessage = string.Empty;
    private bool _isPaused;
    private bool _hasAlbumTracks;
    private string _albumTracksHeader = DefaultAlbumTracksHeader;
    private int _selectedTabIndex;

    private const string DefaultAlbumTracksHeader = "Album";

    public DashboardViewModel(
        TrackSyncCoordinator coordinator,
        IClock? clock = null,
        Action<Action>? uiThreadInvoker = null,
        SettingsManager? settingsManager = null,
        IAppleMusicTrackLauncher? trackLauncher = null)
    {
        _coordinator = coordinator;
        _settingsManager = settingsManager;
        _clock = clock ?? new SystemClock();
        _uiThreadInvoker = uiThreadInvoker ?? (action => action());
        _trackLauncher = trackLauncher;

        _coordinator.PropertyChanged += OnCoordinatorPropertyChanged;

        PlayAlbumTrackCommand = new RelayCommand<AlbumTrackEntry>(
            entry => _ = PlayTrackAsync(entry),
            CanPlayTrack);

        Refresh(appendHistoryEntry: false);

        // Seeds the tab the dashboard opens on from the "show switch log by default" setting,
        // one time only, by reading Settings directly rather than keeping a live-tracked property
        // for it: the redesign replaced the old either/or bottom-list visibility (which used to
        // need a property that tracked Settings changes live) with a TabControl, and nothing
        // needs this value again after startup. Re-deriving it on every settings change would also
        // yank the user back out of a tab they'd manually switched to while just, say, tweaking
        // the Grace Period. After this initial seed, SelectedTabIndex is purely user-driven via
        // the TabControl's two-way binding.
        SelectedTabIndex = (_settingsManager?.Current.ShowSwitchLog ?? false) ? 1 : 0;
    }

    /// <summary>Recent Format Resolutions/switches, newest first, trimmed to <see cref="MaxHistoryEntries"/>.</summary>
    public ObservableCollection<SwitchHistoryEntry> History { get; } = [];

    /// <summary>
    /// The current album's tracks in album order, the currently playing one flagged — the
    /// "Playing Next" tab's content. Populated whenever the catalog layer has an album list in
    /// hand, cleared when the playing Track stops matching it.
    /// </summary>
    public ObservableCollection<AlbumTrackEntry> AlbumTracks { get; } = [];

    /// <summary>
    /// Clicking an album-track row plays that exact song in its album context. It hands the row's
    /// Apple Music catalog id (and album id) to <see cref="IAppleMusicTrackLauncher"/>, which
    /// deep-links straight to the track within the album — so the right song plays no matter what
    /// the queue is (a discovery station, a shuffled playlist, or the album itself), and playback
    /// then continues through the rest of that album rather than back to whatever was playing
    /// before. This deliberately replaces the original relative-skip walk over
    /// <see cref="IMediaTransport"/>: SMTC exposes no "play this track" verb (see docs/CONTEXT.md's
    /// Next Track Buffer note, "SMTC exposes no play queue"), so walking Previous/Next only reached
    /// the right song when the queue *was* this album in album order and landed on an unrelated song
    /// otherwise — the reported bug. Disabled (see <see cref="CanPlayTrack"/>) when there's no
    /// launcher, the clicked row already is the current one, or the row has no catalog id to play.
    /// </summary>
    public RelayCommand<AlbumTrackEntry> PlayAlbumTrackCommand { get; }

    /// <summary>True while <see cref="AlbumTracks"/> has content; the album view shows a placeholder hint otherwise.</summary>
    public bool HasAlbumTracks
    {
        get => _hasAlbumTracks;
        private set => SetField(ref _hasAlbumTracks, value);
    }

    /// <summary>"Album — &lt;name&gt;" while an album is showing, or a plain "Album" placeholder header.</summary>
    public string AlbumTracksHeader
    {
        get => _albumTracksHeader;
        private set => SetField(ref _albumTracksHeader, value);
    }

    /// <summary>
    /// Which tab of the "Playing Next" (0) / "History" (1) TabControl is selected — two-way bound
    /// so the user can switch freely. Seeded once from <see cref="Settings.ShowSwitchLog"/> at
    /// startup (see the constructor) and left alone after that; it does not track the Settings
    /// value live — there is no live-tracked visibility property anymore (the redesign's
    /// TabControl replaced the either/or bottom list that used to need one).
    /// </summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetField(ref _selectedTabIndex, value);
    }

    public string TrackDisplay
    {
        get => _trackDisplay;
        private set => SetField(ref _trackDisplay, value);
    }

    public string TargetFormatDisplay
    {
        get => _targetFormatDisplay;
        private set => SetField(ref _targetFormatDisplay, value);
    }

    /// <summary>
    /// True when the device didn't support the resolved Target Format's rate and the coordinator
    /// clamped it — see <see cref="TrackSyncCoordinator.AppliedFormat"/>.
    /// </summary>
    public bool HasClampedFormat
    {
        get => _hasClampedFormat;
        private set => SetField(ref _hasClampedFormat, value);
    }

    public string ClampedFormatDisplay
    {
        get => _clampedFormatDisplay;
        private set => SetField(ref _clampedFormatDisplay, value);
    }

    public string ConfidenceDisplay
    {
        get => _confidenceDisplay;
        private set => SetField(ref _confidenceDisplay, value);
    }

    public string SourceLayerDisplay
    {
        get => _sourceLayerDisplay;
        private set => SetField(ref _sourceLayerDisplay, value);
    }

    /// <summary>See <see cref="AudioTierClassifier"/> — a heuristic badge, not real catalog data yet.</summary>
    public string AudioTierDisplay
    {
        get => _audioTierDisplay;
        private set => SetField(ref _audioTierDisplay, value);
    }

    public string DeviceFormatDisplay
    {
        get => _deviceFormatDisplay;
        private set => SetField(ref _deviceFormatDisplay, value);
    }

    /// <summary>True when a Pinned device is missing and the coordinator has fallen back to the Windows default.</summary>
    public bool HasDeviceTargetWarning
    {
        get => _hasDeviceTargetWarning;
        private set => SetField(ref _hasDeviceTargetWarning, value);
    }

    public string DeviceTargetWarningMessage
    {
        get => _deviceTargetWarningMessage;
        private set => SetField(ref _deviceTargetWarningMessage, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set => SetField(ref _isPaused, value);
    }

    /// <summary>
    /// Feeds the album view from <see cref="CatalogFormatResolverLayer.AlbumTracksAvailable"/>.
    /// Safe to call from any thread — like every other reaction it is marshaled through the
    /// UI-thread invoker before touching <see cref="AlbumTracks"/>.
    /// </summary>
    public void OnAlbumTracksAvailable(IReadOnlyList<CatalogSearchCandidate> albumTracks) =>
        _uiThreadInvoker(() =>
        {
            _albumCandidates = albumTracks;
            RebuildAlbumTracks();
        });

    private void OnCoordinatorPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        _uiThreadInvoker(() => Refresh(
            appendHistoryEntry: e.PropertyName == nameof(TrackSyncCoordinator.Resolution),
            trackChanged: e.PropertyName == nameof(TrackSyncCoordinator.CurrentTrack)));

    private void Refresh(bool appendHistoryEntry, bool trackChanged = false)
    {
        var track = _coordinator.CurrentTrack;
        TrackDisplay = track is null ? NoTrackDisplay : $"{track.Title} — {track.Artist} ({track.Album})";

        var resolution = _coordinator.Resolution;
        TargetFormatDisplay = resolution?.Target.ToString() ?? "—";
        ConfidenceDisplay = resolution?.Confidence.ToString() ?? "—";
        SourceLayerDisplay = resolution?.SourceLayer ?? "—";
        AudioTierDisplay = AudioTierClassifier.Classify(resolution).ToDisplayString();

        // The coordinator clamps the requested Target Format to a rate the device actually
        // supports (Domain.RateFamilyClamp) before applying it; when that changed the rate, show
        // both so the user isn't left wondering why playback isn't at the rate the track implies.
        // Compare rates only: the coordinator also pins the applied bit depth to 24 (a 16-bit
        // source deliberately plays at 24-bit), and that intentional pinning must not read as
        // "device doesn't support it".
        var applied = _coordinator.AppliedFormat;
        if (resolution is not null && applied is not null && applied.Value.SampleRateHz != resolution.Target.SampleRateHz)
        {
            HasClampedFormat = true;
            ClampedFormatDisplay = $"{applied} (device doesn't support {resolution.Target})";
        }
        else
        {
            HasClampedFormat = false;
            ClampedFormatDisplay = string.Empty;
        }

        DeviceFormatDisplay = _coordinator.DeviceFormat?.ToString() ?? "—";

        var targetStatus = _coordinator.TargetStatus;
        if (targetStatus is { IsFallback: true })
        {
            // Per issue #6's acceptance criteria: a pinned device that's gone missing must fall
            // back gracefully but stay visible to the user, not fail silently.
            HasDeviceTargetWarning = true;
            DeviceTargetWarningMessage =
                $"Pinned device unavailable — using Windows default ({targetStatus.FriendlyName ?? "unknown"}) instead.";
        }
        else
        {
            HasDeviceTargetWarning = false;
            DeviceTargetWarningMessage = string.Empty;
        }

        IsPaused = _coordinator.IsPaused;

        if (trackChanged)
        {
            RebuildAlbumTracks();
        }

        if (appendHistoryEntry && resolution is not null)
        {
            AppendHistoryEntry(resolution);
        }
    }

    /// <summary>
    /// Re-derives the album view from <see cref="_albumCandidates"/> and the current Track: the
    /// matching row (scored by <see cref="CatalogTrackMatcher"/>, the same conservative gate the
    /// resolver trusts) is flagged as current, and a Track that no longer matches the list at all
    /// (album jump, shuffle, a track the catalog never matched) clears it — a stale album must
    /// not linger under an unrelated song. A null Track (playback stopped/paused away) keeps the
    /// list as-is: resuming the same album is the common case, and a real change re-clears it.
    /// </summary>
    private void RebuildAlbumTracks()
    {
        var track = _coordinator.CurrentTrack;
        if (track is null)
        {
            return;
        }

        var current = CatalogTrackMatcher.FindBestMatch(track, _albumCandidates);
        if (current is null)
        {
            _albumCandidates = [];
            AlbumTracks.Clear();
            HasAlbumTracks = false;
            AlbumTracksHeader = DefaultAlbumTracksHeader;
            return;
        }

        AlbumTracks.Clear();
        for (var i = 0; i < _albumCandidates.Count; i++)
        {
            var candidate = _albumCandidates[i];
            AlbumTracks.Add(new AlbumTrackEntry(
                i + 1, candidate.Title, candidate.Artist,
                IsCurrent: ReferenceEquals(candidate, current), CatalogId: candidate.Id, Duration: candidate.Duration,
                AlbumId: candidate.AlbumId));
        }

        HasAlbumTracks = true;
        AlbumTracksHeader = $"{DefaultAlbumTracksHeader} — {current.Album}";
    }

    private bool CanPlayTrack(AlbumTrackEntry? entry) =>
        entry is not null
        && !entry.IsCurrent
        && !string.IsNullOrEmpty(entry.CatalogId)
        && _trackLauncher is not null
        && AlbumTracks.IndexOf(entry) >= 0;

    /// <summary>
    /// Deep-links Apple Music straight to <paramref name="entry"/>'s catalog track — see
    /// <see cref="PlayAlbumTrackCommand"/>'s doc comment for why this replaced the old
    /// relative-skip walk. Re-validates what <see cref="CanPlayTrack"/> checked: a row can leave
    /// the list, or become the current one, between a click landing in the UI thread's queue and
    /// this method actually running.
    /// </summary>
    private async Task PlayTrackAsync(AlbumTrackEntry? entry)
    {
        if (entry is null
            || entry.IsCurrent
            || string.IsNullOrEmpty(entry.CatalogId)
            || _trackLauncher is null
            || AlbumTracks.IndexOf(entry) < 0)
        {
            return;
        }

        await _trackLauncher.PlayTrackAsync(entry.CatalogId, entry.AlbumId);
    }

    private void AppendHistoryEntry(FormatResolution resolution)
    {
        var entry = new SwitchHistoryEntry(
            _clock.UtcNow,
            TrackDisplay,
            resolution.Target.ToString(),
            AudioTierClassifier.Classify(resolution).ToDisplayString(),
            resolution.Confidence.ToString(),
            resolution.SourceLayer);

        History.Insert(0, entry);
        while (History.Count > MaxHistoryEntries)
        {
            History.RemoveAt(History.Count - 1);
        }
    }
}
