using Samklang.Devices;
using Samklang.SettingsManagement;
using Samklang.Timing;

namespace Samklang;

/// <summary>
/// <see cref="IRestingFormatReverter"/>'s real implementation: tracks the instant playback went
/// idle and, once <see cref="Tick"/> observes that the Grace Period has elapsed since then,
/// applies the current Resting Format to the device exactly once. Resuming
/// (<see cref="NotifyActive"/>) before that point clears the idle-since timestamp entirely, so
/// no revert — and no switch of any kind — happens for a quick pause/resume within the Grace
/// Period.
///
/// Driven purely by <see cref="IClock"/> reads rather than a real timer, so the decision logic
/// is unit-testable by advancing a fake clock instead of waiting on wall-clock time.
/// </summary>
public sealed class RestingFormatReverter(SettingsManager settingsManager, IDeviceController deviceController, IClock clock)
    : IRestingFormatReverter
{
    private DateTimeOffset? _idleSinceUtc;
    private bool _revertedForCurrentIdlePeriod;

    public void NotifyIdle() => _idleSinceUtc ??= clock.UtcNow;

    public void NotifyActive()
    {
        _idleSinceUtc = null;
        _revertedForCurrentIdlePeriod = false;
    }

    public void Tick()
    {
        if (_idleSinceUtc is null || _revertedForCurrentIdlePeriod)
        {
            return;
        }

        if (clock.UtcNow - _idleSinceUtc.Value < settingsManager.Current.GracePeriod)
        {
            return;
        }

        deviceController.ApplyTargetFormat(settingsManager.Current.RestingFormat);
        _revertedForCurrentIdlePeriod = true;
    }
}
