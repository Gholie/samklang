using System.Collections.ObjectModel;
using System.ComponentModel;
using Samklang.Domain;
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
    private readonly IClock _clock;
    private readonly Action<Action> _uiThreadInvoker;

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

    public DashboardViewModel(TrackSyncCoordinator coordinator, IClock? clock = null, Action<Action>? uiThreadInvoker = null)
    {
        _coordinator = coordinator;
        _clock = clock ?? new SystemClock();
        _uiThreadInvoker = uiThreadInvoker ?? (action => action());

        _coordinator.PropertyChanged += OnCoordinatorPropertyChanged;
        Refresh(appendHistoryEntry: false);
    }

    /// <summary>Recent Format Resolutions/switches, newest first, trimmed to <see cref="MaxHistoryEntries"/>.</summary>
    public ObservableCollection<SwitchHistoryEntry> History { get; } = [];

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

    private void OnCoordinatorPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        _uiThreadInvoker(() => Refresh(appendHistoryEntry: e.PropertyName == nameof(TrackSyncCoordinator.Resolution)));

    private void Refresh(bool appendHistoryEntry)
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

        if (appendHistoryEntry && resolution is not null)
        {
            AppendHistoryEntry(resolution);
        }
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
