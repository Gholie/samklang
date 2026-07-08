using System.ComponentModel;
using Samklang.Devices;
using Samklang.Domain;
using Samklang.Logging;
using Samklang.Resolver;
using Samklang.Sessions;

namespace Samklang;

/// <summary>
/// <see cref="ITrackWatcher"/>, runs the <see cref="IFormatResolver"/>, clamps its Target Format
/// to a rate the default render device actually supports (<see cref="RateFamilyClamp"/>, using
/// capabilities probed via <see cref="IDeviceController.GetSupportedSampleRates"/>), and applies
/// the clamped result via the <see cref="IDeviceController"/>. Also feeds the watcher's
/// Track/Playback State signals to an <see cref="IRestingFormatReverter"/>, which reverts the
/// device to the Resting Format after the Grace Period once playback is idle (paused, stopped, or
/// Apple Music closed). Exposes the current Track, Format Resolution, clamped Applied Format, and
/// live Device Format as notifying properties so a UI (MainWindow) can bind or subscribe —
/// including showing requested vs. applied when clamping changed the rate — without knowing
/// anything about SMTC, the resolver chain, or COM.
///
/// Framework-free by design, so this whole pipeline is unit-testable with fakes for all
/// collaborators, independent of WPF.
///
/// Thread safety: the track watcher's events arrive on SMTC/COM callback threads while
/// <see cref="CheckGracePeriodRevert"/> (the UI poll timer), <see cref="ApplyLateResolution"/>
/// (Dispatcher-marshaled), and Pause/Resume (tray menu) arrive on the UI thread, so every
/// state-mutating entry point serializes on <see cref="_gate"/> — this is also what keeps the
/// reverter's idle bookkeeping single-threaded. <see cref="PropertyChanged"/> is raised while
/// holding the gate; production subscribers react via <c>Dispatcher.BeginInvoke</c> (never
/// synchronously on the raising thread), so no subscriber work runs under the lock.
/// </summary>
public sealed class TrackSyncCoordinator : INotifyPropertyChanged
{
    private readonly ITrackWatcher _trackWatcher;
    private readonly IFormatResolver _resolver;
    private readonly IDeviceController _deviceController;
    private readonly IRestingFormatReverter _reverter;
    private readonly object _gate = new();

    public TrackSyncCoordinator(
        ITrackWatcher trackWatcher,
        IFormatResolver resolver,
        IDeviceController deviceController,
        IRestingFormatReverter reverter)
    {
        _trackWatcher = trackWatcher;
        _resolver = resolver;
        _deviceController = deviceController;
        _reverter = reverter;
        _trackWatcher.TrackChanged += OnTrackChanged;
        _trackWatcher.PlaybackStateChanged += OnPlaybackStateChanged;
    }

    /// <summary>The Track currently playing in Apple Music, or null when it isn't.</summary>
    public Track? CurrentTrack { get; private set; }

    /// <summary>The most recent Format Resolution (Target Format + Confidence), or null before the first Track.</summary>
    public FormatResolution? Resolution { get; private set; }

    /// <summary>
    /// The Target Format actually applied to the device for the current Track, after bit-depth
    /// pinning and rate-family clamping — or null before the first Track. Its bit depth is always
    /// <see cref="FallbackFormatResolverLayer.PinnedBitDepth"/> (a resolver layer that read a real
    /// 16-bit source keeps 16 in <c>Resolution.Target</c> for display, but 24-bit playback of
    /// 16-bit content is bit-perfect, so the device is never switched below 24). Its rate equals
    /// <c>Resolution.Target</c>'s unless the device didn't support it, in which case the UI
    /// should show both.
    /// </summary>
    public DeviceFormat? AppliedFormat { get; private set; }

    /// <summary>The effective render device's actual, live Device Format.</summary>
    public DeviceFormat? DeviceFormat { get; private set; }

    /// <summary>
    /// The device-targeting status as of the last refresh: which device is actually in effect,
    /// its friendly name, and whether that's a fallback away from a missing Pinned device. Null
    /// until the first refresh.
    /// </summary>
    public DeviceTargetStatus? TargetStatus { get; private set; }

    /// <summary>
    /// Whether Track- and Grace-Period-driven format switching is currently suppressed (the tray
    /// menu's "Pause switching"). Track and Playback State are still observed while paused — so a
    /// tray tooltip bound to <see cref="CurrentTrack"/> stays live — but no Target Format is
    /// resolved or applied to the device, and no idle tracking towards a Grace Period revert
    /// happens, until <see cref="Resume"/> is called.
    /// </summary>
    public bool IsPaused { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task StartAsync() => _trackWatcher.StartAsync();

    /// <summary>Suppresses format switching until <see cref="Resume"/> is called. Idempotent while already paused.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (IsPaused)
            {
                return;
            }

            IsPaused = true;
            OnPropertyChanged(nameof(IsPaused));
        }
    }

    /// <summary>
    /// Resumes format switching after a prior <see cref="Pause"/>, immediately re-resolving and
    /// applying the current Track's format — a track that started while paused would otherwise
    /// keep playing at whatever format was left applied until the *next* track change. Idempotent
    /// while not paused.
    /// </summary>
    public void Resume()
    {
        lock (_gate)
        {
            if (!IsPaused)
            {
                return;
            }

            IsPaused = false;
            OnPropertyChanged(nameof(IsPaused));

            if (CurrentTrack is { } track)
            {
                ApplyResolution(_resolver.Resolve(track));
            }
        }
    }

    /// <summary>
    /// Re-reads the effective device's actual Device Format and targeting status, for callers
    /// that poll independently of track changes — this is what makes Follow mode pick up a
    /// Windows-default change, and Pinned mode notice a pinned device disappearing or
    /// reappearing, within one poll interval.
    /// </summary>
    public void RefreshDeviceFormat()
    {
        lock (_gate)
        {
            DeviceFormat = _deviceController.GetCurrentFormat();
            OnPropertyChanged(nameof(DeviceFormat));

            TargetStatus = _deviceController.GetTargetStatus();
            OnPropertyChanged(nameof(TargetStatus));
        }
    }

    /// <summary>
    /// Checks whether the Grace Period has elapsed since playback went idle and, if so, reverts
    /// the device to the Resting Format. Intended to be called periodically (e.g. from a UI poll
    /// timer) since idle duration otherwise has no event to hang off of.
    /// </summary>
    public void CheckGracePeriodRevert()
    {
        lock (_gate)
        {
            if (!IsPaused)
            {
                _reverter.Tick();
            }

            RefreshDeviceFormat();
        }
    }

    private void OnTrackChanged(object? sender, TrackChangedEventArgs e)
    {
        lock (_gate)
        {
            // SMTC placeholder states ("Connecting…", a station name with no artist, or even a
            // real-looking title with an empty artist) are not real Track changes — see
            // TransientTrackDetector. Keep showing whatever was last current (possibly nothing,
            // if this is the very first update the app has ever seen) through the ~1s gap:
            // CurrentTrack is left untouched, nothing is resolved, no history entry is appended,
            // and no device switch happens. This intentionally applies only to media-property
            // updates carrying placeholder metadata, not to playback-status or session-closed
            // transitions (e.Track is null for those, which is handled below as before).
            if (e.Track is { } candidate && TransientTrackDetector.IsTransient(candidate))
            {
                AppLog.Info($"Ignoring transient SMTC state \"{candidate.Title}\" — keeping current track.", category: "TrackSync");
                return;
            }

            CurrentTrack = e.Track;
            OnPropertyChanged(nameof(CurrentTrack));

            if (IsPaused)
            {
                // Switching is paused: Current Track keeps updating above (so a tray tooltip stays
                // live), but resolving/applying a Target Format is suppressed entirely, including the
                // idle notification below — otherwise a paused idle period would silently queue up a
                // Grace Period revert that fires the moment Resume() is called.
                return;
            }

            if (e.Track is null)
            {
                Resolution = null;
                OnPropertyChanged(nameof(Resolution));
                AppliedFormat = null;
                OnPropertyChanged(nameof(AppliedFormat));

                // No track at all means Apple Music closed (or hasn't been picked up yet), one of
                // the three idle conditions alongside paused/stopped.
                _reverter.NotifyIdle();
                return;
            }

            ApplyResolution(_resolver.Resolve(e.Track));
        }
    }

    /// <summary>
    /// Applies a Format Resolution that arrived after the fact for a Track that has since become
    /// stale — e.g. the catalog layer's bounded wait (see
    /// <see cref="Resolver.Catalog.CatalogFormatResolverLayer"/>) timed out and a lower-confidence
    /// layer's result was already applied, but the catalog lookup kept running in the background
    /// and eventually produced an Exact result. Silently ignored if <paramref name="track"/> is no
    /// longer the current Track (the user has since moved on, so applying it now would be wrong),
    /// or if switching has been paused since the lookup started — "Pause switching" means no
    /// switch of any kind, including late corrections. Intended to be wired to such a layer's
    /// "late resolution" event from the composition root.
    /// </summary>
    public void ApplyLateResolution(Track track, FormatResolution resolution)
    {
        lock (_gate)
        {
            if (IsPaused || CurrentTrack != track)
            {
                return;
            }

            ApplyResolution(resolution);
        }
    }

    private void ApplyResolution(FormatResolution resolution)
    {
        Resolution = resolution;
        OnPropertyChanged(nameof(Resolution));

        // 16-bit sources are deliberately ignored for device switching: the device target is
        // always 24-bit (24-bit playback of 16-bit content is bit-perfect), so only the sample
        // rate ever drives a switch. Resolution.Target above keeps the source's true bit depth
        // for display.
        var target = resolution.Target with { BitDepth = FallbackFormatResolverLayer.PinnedBitDepth };

        var supportedSampleRates = _deviceController.GetSupportedSampleRates(target.BitDepth);
        var previousAppliedFormat = AppliedFormat;
        AppliedFormat = RateFamilyClamp.Clamp(target, supportedSampleRates);
        OnPropertyChanged(nameof(AppliedFormat));

        // Timed even though ApplyTargetFormat no-ops (returns false, no mute/switch) when the
        // sample rate already matches — a switch that's unexpectedly slow (a device driver being
        // slow to apply the new format) is exactly the case a diagnosable log needs elapsed time
        // for, and the no-op case is cheap to time anyway.
        var switchTimer = System.Diagnostics.Stopwatch.StartNew();
        var switched = _deviceController.ApplyTargetFormat(AppliedFormat.Value);
        switchTimer.Stop();

        if (AppliedFormat != previousAppliedFormat)
        {
            AppLog.Info(
                $"Switching device to {AppliedFormat} for \"{CurrentTrack?.Title}\" " +
                $"(resolved {resolution.Target} via {resolution.SourceLayer}, {resolution.Confidence}) " +
                $"— {(switched ? "applied" : "skipped, rate already matched")} in {switchTimer.ElapsedMilliseconds} ms.",
                category: "TrackSync");
        }

        RefreshDeviceFormat();
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        lock (_gate)
        {
            if (IsPaused)
            {
                return;
            }

            if (e.State == PlaybackState.Playing)
            {
                _reverter.NotifyActive();
            }
            else
            {
                // Paused, Stopped, or null (session gone) are all idle per CONTEXT.md's Grace Period
                // definition.
                _reverter.NotifyIdle();
            }
        }
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
