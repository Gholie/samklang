using System.Windows;
using System.Windows.Threading;
using Samklang.Devices;
using Samklang.Resolver;
using Samklang.Sessions;

namespace Samklang;

public partial class MainWindow : Window
{
    private readonly SmtcTrackWatcher _trackWatcher;
    private readonly TrackSyncCoordinator _coordinator;
    private readonly DispatcherTimer _deviceFormatPoll;

    public MainWindow()
    {
        InitializeComponent();

        _trackWatcher = new SmtcTrackWatcher();
        var resolver = new FormatResolverChain([new FallbackFormatResolverLayer()]);
        var deviceController = new DeviceController(new PolicyConfigAudioEndpoint());

        _coordinator = new TrackSyncCoordinator(_trackWatcher, resolver, deviceController);
        _coordinator.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(UpdateDisplay);

        // The device format can change from outside our own switches (e.g. the user changing it
        // by hand in the Sound control panel), so poll it independently of track-change events
        // to keep the "live device format" reading in the window honest.
        _deviceFormatPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _deviceFormatPoll.Tick += (_, _) => _coordinator.RefreshDeviceFormat();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateDisplay();
        _deviceFormatPoll.Start();

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
        _deviceFormatPoll.Stop();
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

        DeviceFormatText.Text = _coordinator.DeviceFormat?.ToString() ?? "—";
    }
}
