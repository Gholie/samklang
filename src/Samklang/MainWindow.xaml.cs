using System.Windows;
using System.Windows.Threading;
using Samklang.Devices;
using Samklang.Domain;
using Samklang.Resolver;
using Samklang.Sessions;
using Samklang.SettingsManagement;
using Samklang.Timing;

namespace Samklang;

public partial class MainWindow : Window
{
    private readonly SmtcTrackWatcher _trackWatcher;
    private readonly SettingsManager _settingsManager;
    private readonly TrackSyncCoordinator _coordinator;
    private readonly DispatcherTimer _pollTimer;

    public MainWindow()
    {
        InitializeComponent();

        _trackWatcher = new SmtcTrackWatcher();
        var resolver = new FormatResolverChain([new FallbackFormatResolverLayer()]);
        var deviceController = new DeviceController(new PolicyConfigAudioEndpoint());

        _settingsManager = new SettingsManager(new JsonFileSettingsStore());
        _settingsManager.LoadOrSeed(TryGetCurrentDeviceFormat(deviceController));

        var reverter = new RestingFormatReverter(_settingsManager, deviceController, new SystemClock());

        _coordinator = new TrackSyncCoordinator(_trackWatcher, resolver, deviceController, reverter);
        _coordinator.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(UpdateDisplay);

        // Drives both the "live device format" reading (the device can change from outside our
        // own switches, e.g. the user changing it by hand in the Sound control panel) and the
        // Grace Period revert check — neither has its own event to hang off of, so both are
        // polled on the same cheap timer.
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => _coordinator.CheckGracePeriodRevert();
    }

    private static DeviceFormat? TryGetCurrentDeviceFormat(IDeviceController deviceController)
    {
        try
        {
            return deviceController.GetCurrentFormat();
        }
        catch
        {
            // Device probing can fail very early at startup (no active render device yet, COM
            // not ready); SettingsManager falls back to its own default Resting Format rather
            // than crashing construction over it.
            return null;
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateDisplay();
        LoadSettingsIntoForm();
        _pollTimer.Start();

        try
        {
            await _coordinator.StartAsync();
        }
        catch
        {
            // SMTC session watching can fail to start (no session manager available, no media
            // apps running yet, etc.); leave the window showing its empty defaults rather than
            // crashing the app over a transient/absent Windows media session.
        }
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _pollTimer.Stop();
        _trackWatcher.Dispose();
    }

    private void UpdateDisplay()
    {
        var track = _coordinator.CurrentTrack;
        TrackText.Text = track is null
            ? "(none — waiting for Apple Music)"
            : $"{track.Title} — {track.Artist} ({track.Album})";

        var resolution = _coordinator.Resolution;
        TargetFormatText.Text = resolution?.Target.ToString() ?? "—";
        ConfidenceText.Text = resolution?.Confidence.ToString() ?? "—";

        // The coordinator clamps the requested Target Format to a rate the device actually
        // supports (Samklang.Domain.RateFamilyClamp) before applying it; when that changed the
        // rate, show both so the user isn't left wondering why playback isn't at the rate the
        // track list implies.
        var applied = _coordinator.AppliedFormat;
        if (resolution is not null && applied is not null && applied.Value != resolution.Target)
        {
            ClampedFormatRow.Visibility = Visibility.Visible;
            ClampedFormatText.Text = $"{applied} (device doesn't support {resolution.Target})";
        }
        else
        {
            ClampedFormatRow.Visibility = Visibility.Collapsed;
        }

        DeviceFormatText.Text = _coordinator.DeviceFormat?.ToString() ?? "—";
    }

    private void LoadSettingsIntoForm()
    {
        var settings = _settingsManager.Current;
        RestingSampleRateBox.Text = settings.RestingFormat.SampleRateHz.ToString();
        RestingBitDepthBox.Text = settings.RestingFormat.BitDepth.ToString();
        GracePeriodSecondsBox.Text = settings.GracePeriod.TotalSeconds.ToString("0");
        SettingsStatusText.Text = string.Empty;
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RestingSampleRateBox.Text, out var sampleRateHz) || sampleRateHz <= 0 ||
            !int.TryParse(RestingBitDepthBox.Text, out var bitDepth) || bitDepth <= 0 ||
            !double.TryParse(GracePeriodSecondsBox.Text, out var gracePeriodSeconds) || gracePeriodSeconds < 0)
        {
            SettingsStatusText.Text = "Invalid values — not saved.";
            return;
        }

        _settingsManager.UpdateRestingFormat(new DeviceFormat(sampleRateHz, bitDepth));
        _settingsManager.UpdateGracePeriod(TimeSpan.FromSeconds(gracePeriodSeconds));
        SettingsStatusText.Text = "Saved.";
    }
}
