using System.Windows;
using System.Windows.Input;

namespace Samklang;

/// <summary>
/// Attached "Command"/"CommandParameter" pair for turning a plain click on any <see cref="UIElement"/>
/// into an <see cref="ICommand"/> invocation. Exists because <c>ListViewItem</c> (unlike
/// <c>ButtonBase</c>) has no built-in <c>Command</c> property, and this project has no MVVM
/// toolkit or Blend-interactivity package to reach for instead — see the dashboard's album track
/// list in MainWindow.xaml, where it makes a row click run
/// <see cref="ViewModels.DashboardViewModel.PlayAlbumTrackCommand"/> with the row's
/// <c>AlbumTrackEntry</c> as parameter, entirely from XAML with no code-behind glue.
///
/// <para>
/// Fires on <see cref="UIElement.MouseLeftButtonUp"/> (mirroring ordinary button-click semantics —
/// the press already happened, this is the release that completes the click) and re-checks
/// <see cref="ICommand.CanExecute"/> at click time, so a command that's since become disabled
/// doesn't fire just because the element still has old handlers attached.
/// </para>
/// </summary>
public static class ItemClickBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(ItemClickBehavior), new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
        "CommandParameter", typeof(object), typeof(ItemClickBehavior));

    public static void SetCommand(UIElement element, ICommand? value) => element.SetValue(CommandProperty, value);

    public static ICommand? GetCommand(UIElement element) => (ICommand?)element.GetValue(CommandProperty);

    public static void SetCommandParameter(UIElement element, object? value) => element.SetValue(CommandParameterProperty, value);

    public static object? GetCommandParameter(UIElement element) => element.GetValue(CommandParameterProperty);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        // Unsubscribe first regardless of the old value: a Style Setter re-applies this whenever
        // the container is recycled/rebound (WPF's virtualization reuses ListViewItem containers),
        // and a stale handler left over from a previous item would fire the wrong row's command.
        element.MouseLeftButtonUp -= OnMouseLeftButtonUp;
        if (e.NewValue is not null)
        {
            element.MouseLeftButtonUp += OnMouseLeftButtonUp;
        }
    }

    private static void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        var command = GetCommand(element);
        var parameter = GetCommandParameter(element);
        if (command is not null && command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }
}
