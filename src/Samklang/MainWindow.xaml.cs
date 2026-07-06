using System.ComponentModel;
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
using Samklang.ViewModels;

namespace Samklang;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly SmtcTrackWatcher _trackWatcher;
    private readonly IDeviceController _deviceController;
    private readonly SettingsManager _settingsManager;
    private readonly TrackSyncCoordinator _coordinator;
    private readonly IStartupRegistration _startupRegistration;
    private readonly DispatcherTimer _pollTimer;
    private readonly HttpClient _catalogHttpClient;

    // Issue #8: WPF has no native tray API, so this is System.Windows.Forms.NotifyIcon (enabled
    // via <UseWindowsForms> in the csproj) — fully-qualified throughout rather than `using`-ing
    // System.Windows.Forms, since it has several types (Application, Timer, ...) that collide
    // with WPF's.
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ToolStripMenuItem _pauseMenuItem;

    // Set only by ExitApplication, right before calling Application.Shutdown(); everywhere else,
    // a window "close" (the X button, Alt+F4, ...) just hides to the tray instead.
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        // Issue #9: follows the live Windows light/dark theme (and accent color) from here on —
        // the Light theme merged into App.xaml is just the initial resource load before this
        // takes over.
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

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

        // The tray tooltip isn't bindable (it lives on a Forms NotifyIcon, not a WPF element), so
        // it's still updated directly from the coordinator here. The dashboard/settings displays
        // themselves are driven by DashboardViewModel/SettingsViewModel below instead.
        _coordinator.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(UpdateTrayTooltip);

        _startupRegistration = new RegistryStartupRegistration();

        // Drives both the "live device format" reading (the device can change from outside our
        // own switches, e.g. the user changing it by hand in the Sound control panel) and the
        // Grace Period revert check — neither has its own event to hang off of, so both are
        // polled on the same cheap timer. This is also what makes Follow mode pick up a
        // Windows-default change, and Pinned mode notice a pinned device disappearing or
        // reappearing, within one poll interval (see TrackSyncCoordinator.RefreshDeviceFormat).
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => _coordinator.CheckGracePeriodRevert();

        (_notifyIcon, _pauseMenuItem) = CreateNotifyIcon();

        // MVVM split (issue #9): TrackSyncCoordinator's PropertyChanged already fires off the
        // SMTC/COM thread, so DashboardViewModel is given a UI-thread invoker that marshals its
        // reactions through this window's Dispatcher — required because History is an
        // ObservableCollection a bound ListView enumerates live, which throws if mutated off the
        // UI thread.
        var dashboardViewModel = new DashboardViewModel(_coordinator, uiThreadInvoker: action => Dispatcher.BeginInvoke(action));
        var settingsViewModel = new SettingsViewModel(_settingsManager, _deviceController, _startupRegistration);
        DataContext = new MainViewModel(dashboardViewModel, settingsViewModel);

        UpdateTrayTooltip();
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

    /// <summary>
    /// Tray-first behavior (issue #8): closing the window (the X button, Alt+F4, ...) just hides
    /// it to the tray instead of exiting the app. The app only really exits via
    /// <see cref="ExitApplication"/>, which sets <see cref="_isExiting"/> before shutting down.
    /// </summary>
    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _pollTimer.Stop();
        _trackWatcher.Dispose();
        _catalogHttpClient.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    /// <summary>Restores the window from the tray — used by the tray's own "Open Samklang" item, its double-click, and a second app launch's activation signal (see App.xaml.cs).</summary>
    public void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>The tray menu's "Exit" — the only path (besides an explicit Setting, not yet added) that actually terminates the app instead of hiding to the tray.</summary>
    private void ExitApplication()
    {
        _isExiting = true;
        System.Windows.Application.Current.Shutdown();
    }

    private (System.Windows.Forms.NotifyIcon, System.Windows.Forms.ToolStripMenuItem) CreateNotifyIcon()
    {
        var openItem = new System.Windows.Forms.ToolStripMenuItem("Open Samklang");
        openItem.Click += (_, _) => RestoreFromTray();

        var pauseItem = new System.Windows.Forms.ToolStripMenuItem("Pause switching") { CheckOnClick = true };
        pauseItem.Click += (_, _) =>
        {
            if (pauseItem.Checked)
            {
                _coordinator.Pause();
            }
            else
            {
                _coordinator.Resume();
            }

            UpdateTrayTooltip();
        };

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(pauseItem);
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        var notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = TryGetAppIcon(),
            ContextMenuStrip = contextMenu,
            Text = "Samklang",
            Visible = true,
        };
        notifyIcon.DoubleClick += (_, _) => RestoreFromTray();

        return (notifyIcon, pauseItem);
    }

    private static System.Drawing.Icon TryGetAppIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (processPath is not null)
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // Fall through to the generic icon below — a missing/unreadable exe icon shouldn't
            // stop the tray icon (and the app) from working.
        }

        return System.Drawing.SystemIcons.Application;
    }

    /// <summary>
    /// Keeps the tray icon's tooltip reflecting live status (current Track and applied format,
    /// per issue #8's acceptance criteria) every time the coordinator's state changes.
    /// </summary>
    private void UpdateTrayTooltip()
    {
        var track = _coordinator.CurrentTrack;
        var trackLine = track is null ? "No track playing" : $"{track.Title} — {track.Artist}";
        var formatLine = _coordinator.AppliedFormat?.ToString() ?? "No format applied";
        var pausedSuffix = _coordinator.IsPaused ? " (switching paused)" : string.Empty;

        var tooltip = $"Samklang{pausedSuffix}\n{trackLine}\n{formatLine}";

        // NotifyIcon.Text throws if longer than 127 characters.
        _notifyIcon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }
}
