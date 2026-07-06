using System.Windows.Input;

namespace Samklang.ViewModels;

/// <summary>
/// The smallest <see cref="ICommand"/> that gets XAML <c>Button.Command</c> bindings working
/// without pulling in an MVVM toolkit package. <see cref="CanExecuteChanged"/> piggybacks on
/// <see cref="CommandManager.RequerySuggested"/> (WPF's own command-invalidation heartbeat, fired
/// on focus changes, keyboard/mouse input, etc.) rather than a bespoke invalidation event, since
/// none of this issue's commands have fast-changing CanExecute state that needs tighter timing.
/// </summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();
}
