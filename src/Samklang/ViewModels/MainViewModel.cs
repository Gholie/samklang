namespace Samklang.ViewModels;

/// <summary>
/// Root view model for <see cref="MainWindow"/>: just composes the dashboard and settings view
/// models so the window can bind to a single <c>DataContext</c> and switch between them (e.g. via
/// a <c>TabControl</c>) without either view model knowing about the other.
/// </summary>
public sealed class MainViewModel(DashboardViewModel dashboard, SettingsViewModel settings)
{
    public DashboardViewModel Dashboard { get; } = dashboard;

    public SettingsViewModel Settings { get; } = settings;
}
