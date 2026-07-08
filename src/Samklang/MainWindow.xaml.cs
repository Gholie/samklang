using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Samklang.Devices;
using Samklang.Domain;
using Samklang.Logging;
using Samklang.Resolver;
using Samklang.Resolver.Catalog;
using Samklang.Resolver.PlayCache;
using Samklang.Sessions;
using Samklang.SettingsManagement;
using Samklang.Timing;
using Samklang.Updates;
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
    private readonly AppUpdateService _updateService;

    // Issue #8: WPF has no native tray API, so this is System.Windows.Forms.NotifyIcon (enabled
    // via <UseWindowsForms> in the csproj) — fully-qualified throughout rather than `using`-ing
    // System.Windows.Forms, since it has several types (Application, Timer, ...) that collide
    // with WPF's.
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;

    // Set only by ExitApplication, right before calling Application.Shutdown(); everywhere else,
    // a window "close" (the X button, Alt+F4, ...) just hides to the tray instead.
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        // Issue #10: version visible in the UI (also shown in the tray tooltip — see
        // UpdateTrayTooltip) via a single formatting helper, so both stay in sync with the
        // csproj's <Version> without duplicating string formatting.
        Title = $"Samklang {VersionInfo.CurrentDisplay}";

        // Issue #9: follows the live Windows light/dark theme (and accent color) from here on —
        // the Light theme merged into App.xaml is just the initial resource load before this
        // takes over.
        Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

        _trackWatcher = new SmtcTrackWatcher();
        _deviceController = new DeviceController(new PolicyConfigAudioEndpoint());

        _settingsManager = new SettingsManager(new JsonFileSettingsStore());
        _settingsManager.LoadOrSeed(TryGetCurrentDeviceFormat(_deviceController));
        _deviceController.SetTargeting(_settingsManager.Current.DeviceTargetingMode, _settingsManager.Current.PinnedDeviceId);

        // Detailed (Info-level) file logging is opt-in via Settings and off by default — sync
        // AppLog's Enabled flag from the loaded value now, before anything else gets a chance to
        // log, and keep it in sync on every subsequent settings change so toggling it in the
        // Settings page takes effect immediately rather than after a restart. Warn/Error always
        // write regardless of this flag — see AppLog's doc comment.
        AppLog.Enabled = _settingsManager.Current.EnableDetailedLogging;
        _settingsManager.PropertyChanged += (_, _) => AppLog.Enabled = _settingsManager.Current.EnableDetailedLogging;

        // 2026-07-08 handoff: the first thing a diagnosable log needs is "did the app even start,
        // and which build" — everything else below is meaningless without this line to anchor a
        // session in the log file. always: true bypasses the EnableDetailedLogging gate (same as
        // every Warn/Error call, see AppLog's doc comment) — a user who only enables logging after
        // hitting a problem must still get this anchor line on the run where the problem showed up.
        AppLog.Info($"Samklang starting: {VersionInfo.CurrentDisplay}.", category: "App", always: true);

        _catalogHttpClient = new HttpClient();
        var catalogLayer = new CatalogFormatResolverLayer(
            new HttpAppleMusicCatalogClient(_catalogHttpClient),
            new WindowsRegionStorefrontProvider(() => _settingsManager.Current.StorefrontOverride));
        var playCacheLayer = new PlayCacheFormatResolverLayer();
        var resolver = new FormatResolverChain([catalogLayer, playCacheLayer, new FallbackFormatResolverLayer()]);

        var reverter = new RestingFormatReverter(_settingsManager, _deviceController, new SystemClock());

        _coordinator = new TrackSyncCoordinator(_trackWatcher, resolver, _deviceController, reverter);
        catalogLayer.LateResolutionAvailable += (_, args) => Dispatcher.BeginInvoke(() => _coordinator.ApplyLateResolution(args.Track, args.Resolution));

        // The tray tooltip isn't bindable (it lives on a Forms NotifyIcon, not a WPF element), so
        // it's still updated directly from the coordinator here. The dashboard/settings displays
        // themselves are driven by DashboardViewModel/SettingsViewModel below instead.
        _coordinator.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(UpdateTrayTooltip);

        _startupRegistration = new RegistryStartupRegistration();
        _updateService = new AppUpdateService();

        // Drives both the "live device format" reading (the device can change from outside our
        // own switches, e.g. the user changing it by hand in the Sound control panel) and the
        // Grace Period revert check — neither has its own event to hang off of, so both are
        // polled on the same cheap timer. This is also what makes Follow mode pick up a
        // Windows-default change, and Pinned mode notice a pinned device disappearing or
        // reappearing, within one poll interval (see TrackSyncCoordinator.RefreshDeviceFormat).
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => _coordinator.CheckGracePeriodRevert();

        _notifyIcon = CreateNotifyIcon();

        // MVVM split (issue #9): TrackSyncCoordinator's PropertyChanged already fires off the
        // SMTC/COM thread, so DashboardViewModel is given a UI-thread invoker that marshals its
        // reactions through this window's Dispatcher — required because History is an
        // ObservableCollection a bound ListView enumerates live, which throws if mutated off the
        // UI thread.
        // _trackWatcher doubles as the IMediaTransport (same SMTC session) — passed here too so
        // clicking an album track can drive playback (DashboardViewModel.PlayAlbumTrackCommand),
        // not just for the now-playing card's own transport buttons below.
        var dashboardViewModel = new DashboardViewModel(
            _coordinator, uiThreadInvoker: action => Dispatcher.BeginInvoke(action), settingsManager: _settingsManager,
            transport: _trackWatcher);
        var settingsViewModel = new SettingsViewModel(_settingsManager, _deviceController, _startupRegistration);

        // The album view rides along on the catalog layer's next-track prefetch — the album list
        // is fetched for prediction anyway, so showing it costs no extra lookup. The view model
        // marshals to the UI thread itself.
        catalogLayer.AlbumTracksAvailable += (_, args) => dashboardViewModel.OnAlbumTracksAvailable(args.AlbumTracks);

        // The watcher doubles as the media transport (both are the same SMTC session) — the
        // now-playing card gets artwork and previous/play-pause/next from it directly, bypassing
        // the coordinator, which stays a pure format-switching pipeline.
        var nowPlayingViewModel = new NowPlayingViewModel(
            _trackWatcher, _trackWatcher, _settingsManager, uiThreadInvoker: action => Dispatcher.BeginInvoke(action));
        DataContext = new MainViewModel(dashboardViewModel, settingsViewModel, nowPlayingViewModel);

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

        // Issue #10: silent background update check on every startup. Deliberately fire-and-forget
        // (not awaited) — AppUpdateService already no-ops outside a real install and swallows its
        // own errors, so there's nothing here for the window to wait on or react to; a found
        // update downloads and restarts the app on its own.
        _ = _updateService.CheckAndApplyUpdateAsync();
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

    private System.Windows.Forms.NotifyIcon CreateNotifyIcon()
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

        // Issue #10: a manual complement to the silent startup check (Window_Loaded) — this one
        // gives feedback via a balloon tip since the user explicitly asked for it.
        var checkUpdatesItem = new System.Windows.Forms.ToolStripMenuItem($"Check for Updates ({VersionInfo.CurrentDisplay})");
        checkUpdatesItem.Click += async (_, _) => await CheckForUpdatesFromTrayAsync();

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(pauseItem);
        contextMenu.Items.Add(checkUpdatesItem);
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

        return notifyIcon;
    }

    /// <summary>The tray menu's "Check for Updates" — same underlying flow as the silent startup check, but reports its outcome via a balloon tip since this one was explicitly requested.</summary>
    private async Task CheckForUpdatesFromTrayAsync()
    {
        var result = await _updateService.CheckAndApplyUpdateAsync();

        // The check can outlive the tray icon: if the user hit Exit while it was in flight, the
        // icon is already disposed and ShowBalloonTip would throw inside an async-void event
        // chain — taking the whole process down on its way out.
        if (_isExiting)
        {
            return;
        }

        var message = result switch
        {
            UpdateCheckResult.NotInstalled => "Update checks are only available in an installed copy of Samklang.",
            UpdateCheckResult.UpToDate => "You're on the latest version.",
            UpdateCheckResult.CheckFailed => "Couldn't check for updates — try again later.",
            UpdateCheckResult.UpdateApplied => "Update downloaded — restarting…",
            _ => string.Empty,
        };

        _notifyIcon.ShowBalloonTip(4000, "Samklang", message, System.Windows.Forms.ToolTipIcon.Info);
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

        var tooltip = $"Samklang {VersionInfo.CurrentDisplay}{pausedSuffix}\n{trackLine}\n{formatLine}";

        // NotifyIcon.Text throws if longer than 127 characters.
        _notifyIcon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }
}
