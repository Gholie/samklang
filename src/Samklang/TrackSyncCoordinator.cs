using System.ComponentModel;
using Samklang.Devices;
using Samklang.Domain;
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
/// </summary>
public sealed class TrackSyncCoordinator : INotifyPropertyChanged
{
    private readonly ITrackWatcher _trackWatcher;
    private readonly IFormatResolver _resolver;
    private readonly IDeviceController _deviceController;
    private readonly IRestingFormatReverter _reverter;

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
    /// The Target Format actually applied to the device for the current Track, after rate-family
    /// clamping — or null before the first Track. Equal to <c>Resolution.Target</c> unless the
    /// device didn't support the requested rate, in which case the UI should show both.
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

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task StartAsync() => _trackWatcher.StartAsync();

    /// <summary>
    /// Re-reads the effective device's actual Device Format and targeting status, for callers
    /// that poll independently of track changes — this is what makes Follow mode pick up a
    /// Windows-default change, and Pinned mode notice a pinned device disappearing or
    /// reappearing, within one poll interval.
    /// </summary>
    public void RefreshDeviceFormat()
    {
        DeviceFormat = _deviceController.GetCurrentFormat();
        OnPropertyChanged(nameof(DeviceFormat));

        TargetStatus = _deviceController.GetTargetStatus();
        OnPropertyChanged(nameof(TargetStatus));
    }

    /// <summary>
    /// Checks whether the Grace Period has elapsed since playback went idle and, if so, reverts
    /// the device to the Resting Format. Intended to be called periodically (e.g. from a UI poll
    /// timer) since idle duration otherwise has no event to hang off of.
    /// </summary>
    public void CheckGracePeriodRevert()
    {
        _reverter.Tick();
        RefreshDeviceFormat();
    }

    private void OnTrackChanged(object? sender, TrackChangedEventArgs e)
    {
        CurrentTrack = e.Track;
        OnPropertyChanged(nameof(CurrentTrack));

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

    /// <summary>
    /// Applies a Format Resolution that arrived after the fact for a Track that has since become
    /// stale — e.g. the catalog layer's bounded wait (see
    /// <see cref="Resolver.Catalog.CatalogFormatResolverLayer"/>) timed out and a lower-confidence
    /// layer's result was already applied, but the catalog lookup kept running in the background
    /// and eventually produced an Exact result. Silently ignored if <paramref name="track"/> is no
    /// longer the current Track (the user has since moved on, so applying it now would be wrong).
    /// Intended to be wired to such a layer's "late resolution" event from the composition root.
    /// </summary>
    public void ApplyLateResolution(Track track, FormatResolution resolution)
    {
        if (CurrentTrack != track)
        {
            return;
        }

        ApplyResolution(resolution);
    }

    private void ApplyResolution(FormatResolution resolution)
    {
        Resolution = resolution;
        OnPropertyChanged(nameof(Resolution));

        var supportedSampleRates = _deviceController.GetSupportedSampleRates(resolution.Target.BitDepth);
        AppliedFormat = RateFamilyClamp.Clamp(resolution.Target, supportedSampleRates);
        OnPropertyChanged(nameof(AppliedFormat));

        _deviceController.ApplyTargetFormat(AppliedFormat.Value);
        RefreshDeviceFormat();
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
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

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
