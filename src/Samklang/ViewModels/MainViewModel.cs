namespace Samklang.ViewModels;

/// <summary>
/// Root view model for <see cref="MainWindow"/>: just composes the now-playing, dashboard, and
/// settings view models so the window can bind to a single <c>DataContext</c> and switch between
/// them (e.g. via a <c>TabControl</c>) without any of them knowing about the others.
/// </summary>
public sealed class MainViewModel(DashboardViewModel dashboard, SettingsViewModel settings, NowPlayingViewModel nowPlaying)
{
    public DashboardViewModel Dashboard { get; } = dashboard;

    public SettingsViewModel Settings { get; } = settings;

    public NowPlayingViewModel NowPlaying { get; } = nowPlaying;
}
