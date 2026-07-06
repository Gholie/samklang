using System.ComponentModel;
using Samklang.Devices;
using Samklang.Domain;
using Samklang.Resolver;
using Samklang.Sessions;

namespace Samklang;

/// <summary>
/// Wires the tracer-bullet pipeline together: on every Track change reported by the
/// <see cref="ITrackWatcher"/>, runs the <see cref="IFormatResolver"/> and applies the result via
/// the <see cref="IDeviceController"/>. Exposes the current Track, Format Resolution, and live
/// Device Format as notifying properties so a UI (MainWindow) can bind or subscribe without
/// knowing anything about SMTC, the resolver chain, or COM.
///
/// Framework-free by design, so this whole pipeline is unit-testable with fakes for all three
/// collaborators, independent of WPF.
/// </summary>
public sealed class TrackSyncCoordinator : INotifyPropertyChanged
{
    private readonly ITrackWatcher _trackWatcher;
    private readonly IFormatResolver _resolver;
    private readonly IDeviceController _deviceController;

    public TrackSyncCoordinator(ITrackWatcher trackWatcher, IFormatResolver resolver, IDeviceController deviceController)
    {
        _trackWatcher = trackWatcher;
        _resolver = resolver;
        _deviceController = deviceController;
        _trackWatcher.TrackChanged += OnTrackChanged;
    }

    /// <summary>The Track currently playing in Apple Music, or null when it isn't.</summary>
    public Track? CurrentTrack { get; private set; }

    /// <summary>The most recent Format Resolution (Target Format + Confidence), or null before the first Track.</summary>
    public FormatResolution? Resolution { get; private set; }

    /// <summary>The default render device's actual, live Device Format.</summary>
    public DeviceFormat? DeviceFormat { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task StartAsync() => _trackWatcher.StartAsync();

    /// <summary>Re-reads the device's actual Device Format, for callers that poll independently of track changes.</summary>
    public void RefreshDeviceFormat()
    {
        DeviceFormat = _deviceController.GetCurrentFormat();
        OnPropertyChanged(nameof(DeviceFormat));
    }

    private void OnTrackChanged(object? sender, TrackChangedEventArgs e)
    {
        CurrentTrack = e.Track;
        OnPropertyChanged(nameof(CurrentTrack));

        if (e.Track is null)
        {
            Resolution = null;
            OnPropertyChanged(nameof(Resolution));
            return;
        }

        Resolution = _resolver.Resolve(e.Track);
        OnPropertyChanged(nameof(Resolution));

        _deviceController.ApplyTargetFormat(Resolution.Target);
        RefreshDeviceFormat();
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
