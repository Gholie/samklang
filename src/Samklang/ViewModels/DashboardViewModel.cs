using System.Collections.ObjectModel;
using System.ComponentModel;
using Samklang.Domain;
using Samklang.Resolver.Catalog;
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
    private bool _showSwitchLog;

    private const string DefaultAlbumTracksHeader = "Album";

    public DashboardViewModel(
        TrackSyncCoordinator coordinator,
        IClock? clock = null,
        Action<Action>? uiThreadInvoker = null,
        SettingsManager? settingsManager = null)
    {
        _coordinator = coordinator;
        _settingsManager = settingsManager;
        _clock = clock ?? new SystemClock();
        _uiThreadInvoker = uiThreadInvoker ?? (action => action());

        _coordinator.PropertyChanged += OnCoordinatorPropertyChanged;
        if (settingsManager is not null)
        {
            settingsManager.PropertyChanged += (_, _) => _uiThreadInvoker(RefreshShowSwitchLog);
        }

        RefreshShowSwitchLog();
        Refresh(appendHistoryEntry: false);
    }

    /// <summary>Recent Format Resolutions/switches, newest first, trimmed to <see cref="MaxHistoryEntries"/>.</summary>
    public ObservableCollection<SwitchHistoryEntry> History { get; } = [];

    /// <summary>
    /// The current album's tracks in album order, the currently playing one flagged — the
    /// dashboard's default bottom list (see <see cref="ShowSwitchLog"/>). Populated whenever the
    /// catalog layer has an album list in hand, cleared when the playing Track stops matching it.
    /// </summary>
    public ObservableCollection<AlbumTrackEntry> AlbumTracks { get; } = [];

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
    /// Mirrors <see cref="Settings.ShowSwitchLog"/> live: true shows the recent-switches log,
    /// false (the default) shows the album track list instead.
    /// </summary>
    public bool ShowSwitchLog
    {
        get => _showSwitchLog;
        private set
        {
            if (SetField(ref _showSwitchLog, value))
            {
                OnPropertyChanged(nameof(ShowAlbumTracks));
            }
        }
    }

    /// <summary>The complement of <see cref="ShowSwitchLog"/>, so the view can bind both visibilities without an inverting converter.</summary>
    public bool ShowAlbumTracks => !ShowSwitchLog;

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
            AlbumTracks.Add(new AlbumTrackEntry(i + 1, candidate.Title, candidate.Artist, IsCurrent: ReferenceEquals(candidate, current)));
        }

        HasAlbumTracks = true;
        AlbumTracksHeader = $"{DefaultAlbumTracksHeader} — {current.Album}";
    }

    private void RefreshShowSwitchLog() =>
        ShowSwitchLog = _settingsManager?.Current.ShowSwitchLog ?? false;

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
