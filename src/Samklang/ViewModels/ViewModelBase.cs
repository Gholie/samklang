using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Samklang.ViewModels;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base for this issue's view models, mirroring the
/// hand-rolled notifying-property pattern already used by <see cref="TrackSyncCoordinator"/> and
/// <see cref="SettingsManagement.SettingsManager"/> rather than pulling in an MVVM toolkit
/// package for a codebase that doesn't otherwise use one.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sets <paramref name="field"/> to <paramref name="value"/> and raises
    /// <see cref="PropertyChanged"/> if it actually changed. Returns whether it changed, so
    /// callers can chain follow-up work (e.g. re-deriving another property) only when needed.
    /// </summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
