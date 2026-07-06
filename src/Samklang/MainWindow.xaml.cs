using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Samklang.Devices;
using Samklang.Domain;
using Samklang.Resolver;
using Samklang.Resolver.Catalog;
using Samklang.Sessions;
using Samklang.SettingsManagement;
using Samklang.Timing;

namespace Samklang;

public partial class MainWindow : Window
{
    private readonly SmtcTrackWatcher _trackWatcher;
    private readonly IDeviceController _deviceController;
    private readonly SettingsManager _settingsManager;
    private readonly TrackSyncCoordinator _coordinator;
    private readonly DispatcherTimer _pollTimer;
    private readonly HttpClient _catalogHttpClient;

    public MainWindow()
    {
        InitializeComponent();

        _trackWatcher = new SmtcTrackWatcher();
        _deviceController = new DeviceController(new PolicyConfigAudioEndpoint());

        _settingsManager = new SettingsManager(new JsonFileSettingsStore());
        _settingsManager.LoadOrSeed(TryGetCurrentDeviceFormat(_deviceController));
        _deviceController.SetTargeting(_settingsManager.Current.DeviceTargetingMode, _settingsManager.Current.PinnedDeviceId);

        _catalogHttpClient = new HttpClient();
        var catalogLayer = new CatalogFormatResolverLayer(
            new HttpAppleMusicCatalogClient(_catalogHttpClient),
            new WindowsRegionStorefrontProvider(() => _settingsManager.Current.StorefrontOverride));
        var resolver = new FormatResolverChain([catalogLayer, new FallbackFormatResolverLayer()]);

        var reverter = new RestingFormatReverter(_settingsManager, _deviceController, new SystemClock());

        _coordinator = new TrackSyncCoordinator(_trackWatcher, resolver, _deviceController, reverter);
        catalogLayer.LateResolutionAvailable += (_, args) => Dispatcher.BeginInvoke(() => _coordinator.ApplyLateResolution(args.Track, args.Resolution));
        _coordinator.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(UpdateDisplay);

        // Drives both the "live device format" reading (the device can change from outside our
        // own switches, e.g. the user changing it by hand in the Sound control panel) and the
        // Grace Period revert check — neither has its own event to hang off of, so both are
        // polled on the same cheap timer. This is also what makes Follow mode pick up a
        // Windows-default change, and Pinned mode notice a pinned device disappearing or
        // reappearing, within one poll interval (see TrackSyncCoordinator.RefreshDeviceFormat).
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
        _catalogHttpClient.Dispose();
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

        var targetStatus = _coordinator.TargetStatus;
        if (targetStatus is null)
        {
            DeviceTargetStatusText.Text = string.Empty;
            DeviceTargetStatusText.Visibility = Visibility.Collapsed;
        }
        else if (targetStatus.IsFallback)
        {
            // Per this issue's acceptance criteria: a pinned device that's gone missing must
            // fall back gracefully but stay visible to the user, not fail silently.
            DeviceTargetStatusText.Text =
                $"Pinned device unavailable — using Windows default ({targetStatus.FriendlyName ?? "unknown"}) instead.";
            DeviceTargetStatusText.Visibility = Visibility.Visible;
        }
        else
        {
            DeviceTargetStatusText.Text = string.Empty;
            DeviceTargetStatusText.Visibility = Visibility.Collapsed;
        }
    }

    private void LoadSettingsIntoForm()
    {
        var settings = _settingsManager.Current;
        RestingSampleRateBox.Text = settings.RestingFormat.SampleRateHz.ToString();
        RestingBitDepthBox.Text = settings.RestingFormat.BitDepth.ToString();
        GracePeriodSecondsBox.Text = settings.GracePeriod.TotalSeconds.ToString("0");

        FollowDefaultRadio.IsChecked = settings.DeviceTargetingMode == DeviceTargetingMode.FollowDefault;
        PinDeviceRadio.IsChecked = settings.DeviceTargetingMode == DeviceTargetingMode.Pinned;

        RefreshDevicePickerItems();
        if (settings.PinnedDeviceId is not null)
        {
            DevicePickerBox.SelectedValue = settings.PinnedDeviceId;
        }

        SettingsStatusText.Text = string.Empty;
    }

    /// <summary>Repopulates the device picker from the render devices Windows currently reports as active.</summary>
    private void RefreshDevicePickerItems()
    {
        var previouslySelected = DevicePickerBox.SelectedValue as string;
        DevicePickerBox.ItemsSource = _deviceController.GetActiveRenderDevices();
        DevicePickerBox.DisplayMemberPath = nameof(RenderDevice.FriendlyName);
        DevicePickerBox.SelectedValuePath = nameof(RenderDevice.Id);
        if (previouslySelected is not null)
        {
            DevicePickerBox.SelectedValue = previouslySelected;
        }
    }

    private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e) => RefreshDevicePickerItems();

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RestingSampleRateBox.Text, out var sampleRateHz) || sampleRateHz <= 0 ||
            !int.TryParse(RestingBitDepthBox.Text, out var bitDepth) || bitDepth <= 0 ||
            !double.TryParse(GracePeriodSecondsBox.Text, out var gracePeriodSeconds) || gracePeriodSeconds < 0)
        {
            SettingsStatusText.Text = "Invalid values — not saved.";
            return;
        }

        var targetingMode = PinDeviceRadio.IsChecked == true ? DeviceTargetingMode.Pinned : DeviceTargetingMode.FollowDefault;
        var pinnedDeviceId = DevicePickerBox.SelectedValue as string;
        if (targetingMode == DeviceTargetingMode.Pinned && pinnedDeviceId is null)
        {
            SettingsStatusText.Text = "Pick a device to pin — not saved.";
            return;
        }

        _settingsManager.UpdateRestingFormat(new DeviceFormat(sampleRateHz, bitDepth));
        _settingsManager.UpdateGracePeriod(TimeSpan.FromSeconds(gracePeriodSeconds));
        _settingsManager.UpdateDeviceTargeting(targetingMode, pinnedDeviceId);
        _deviceController.SetTargeting(targetingMode, pinnedDeviceId);
        SettingsStatusText.Text = "Saved.";
    }
}
