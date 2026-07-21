using Samklang.Devices;
using Samklang.Domain;
using Samklang.Logging;
using Samklang.Sessions;
using Samklang.Timing;

namespace Samklang;

/// <summary>
/// Decorates an <see cref="IDeviceController"/> with an optional pause/resume around
/// <see cref="ApplyTargetFormat"/>, masking the brief mute/rebuild hiccup Windows makes on a
/// shared-mode format switch by not feeding the device any audio during that window — see
/// <see cref="SettingsManagement.FormatSwitchBehavior.PauseDuringSwitch"/>. It also runs a
/// reactive recovery watch after every other real switch (see <see cref="StartRecoveryWatch"/>)
/// to work around a separate Apple Music bug: its own SMTC session sometimes reports itself
/// <see cref="PlaybackState.Paused"/> after a shared-mode format switch invalidates its WASAPI
/// stream, and does not auto-resume on its own, even when nothing asked it to pause.
///
/// Kept as a decorator around <see cref="IDeviceController"/>, rather than folded into
/// <see cref="DeviceController"/> itself or into <see cref="TrackSyncCoordinator"/>, specifically
/// so neither of those needs an <see cref="IMediaTransport"/> dependency: <see cref="DeviceController"/>
/// stays pure hardware (<see cref="IAudioEndpoint"/> only), and <see cref="TrackSyncCoordinator"/>
/// keeps the invariant documented on <see cref="IMediaTransport"/> — the format-switching pipeline
/// never depends on the ability to drive playback. This class is the one place that bridges the two,
/// wired in at the composition root.
///
/// <see cref="IMediaTransport.TogglePlayPauseAsync"/> is awaited synchronously rather than turning
/// <see cref="ApplyTargetFormat"/> async, which would ripple into <see cref="TrackSyncCoordinator"/>'s
/// <c>lock</c>-based synchronization. That blocking wait is done via <see cref="ToggleAndWait"/>,
/// which runs the toggle on the thread pool (<see cref="Task.Run(Func{Task})"/>) rather than awaiting
/// it inline on the calling thread — deliberately, because <see cref="ApplyTargetFormat"/> is
/// <em>not</em> confined to <see cref="TrackSyncCoordinator"/>'s SMTC/COM callback threads: its own
/// thread-safety doc comment documents <c>Resume()</c> (tray menu) and <c>ApplyLateResolution</c>
/// (Dispatcher-marshaled) as arriving on the WPF dispatcher thread too, and both call through to
/// <see cref="ApplyTargetFormat"/>. <see cref="SmtcTrackWatcher.TogglePlayPauseAsync"/>'s internal
/// await doesn't use <c>ConfigureAwait(false)</c>, so its continuation is marshaled back onto
/// whatever <see cref="System.Threading.SynchronizationContext"/> was current when it started; awaited
/// inline with <c>GetAwaiter().GetResult()</c> directly on the dispatcher thread, that continuation
/// would need the very message pump this call is blocking — a deadlock. Starting it via
/// <see cref="Task.Run(Func{Task})"/> instead means it begins with no captured
/// <see cref="System.Threading.SynchronizationContext"/>, so its continuation runs on a thread-pool
/// thread regardless of which thread called <see cref="ApplyTargetFormat"/>.
/// </summary>
public sealed class PlaybackPausingDeviceController(
    IDeviceController inner,
    IMediaTransport mediaTransport,
    Func<PlaybackState?> currentPlaybackState,
    Func<bool> pauseDuringSwitchEnabled,
    IDelay delay) : IDeviceController
{
    /// <summary>How long to wait between recovery polls — see <see cref="StartRecoveryWatch"/>.</summary>
    private static readonly TimeSpan RecoveryPollInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>How many recovery polls to make before giving up — 10 * 150ms = 1.5s total,
    /// comfortably past how quickly Apple Music's SMTC session reported Paused in the diagnosed
    /// case.</summary>
    private const int RecoveryPollCount = 10;

    public DeviceFormat? GetCurrentFormat() => inner.GetCurrentFormat();

    /// <summary>
    /// Test-only seam: the <see cref="Task"/> started by the most recent recovery watch (or null
    /// if none has started yet), so tests can await its completion instead of racing detached
    /// background work.
    /// </summary>
    internal Task? LastRecoveryTask { get; private set; }

    /// <summary>
    /// Pauses before and resumes after the inner switch, but only when the toggle is on, playback
    /// is actually <see cref="PlaybackState.Playing"/> right now (pausing something that's already
    /// paused would just toggle it back into playing), and the inner controller would actually
    /// perform a switch (checked the same way <see cref="DeviceController.ApplyTargetFormat"/>
    /// does, so a same-rate no-op never triggers a needless pause/resume blip). Otherwise, still
    /// starts the reactive recovery watch (see <see cref="StartRecoveryWatch"/>) after a real
    /// switch, since that path never proactively paused Apple Music in the first place.
    /// </summary>
    public bool ApplyTargetFormat(DeviceFormat target)
    {
        if (ShouldPauseAround(target))
        {
            ToggleAndWait();
            try
            {
                return inner.ApplyTargetFormat(target);
            }
            finally
            {
                ToggleAndWait();
            }
        }

        var wasPlaying = currentPlaybackState() == PlaybackState.Playing;
        var switched = inner.ApplyTargetFormat(target);
        if (switched && wasPlaying)
        {
            StartRecoveryWatch();
        }

        return switched;
    }

    /// <summary>See the class doc comment for why this goes through <see cref="Task.Run(Func{Task})"/>
    /// instead of awaiting <see cref="IMediaTransport.TogglePlayPauseAsync"/> inline.</summary>
    private void ToggleAndWait() => Task.Run(mediaTransport.TogglePlayPauseAsync).GetAwaiter().GetResult();

    /// <summary>
    /// Detached (not awaited by <see cref="ApplyTargetFormat"/>) background poll that watches for
    /// Apple Music unexpectedly reporting <see cref="PlaybackState.Paused"/> shortly after a real
    /// switch it wasn't asked to pause around, and resumes it once if so. Bounded to
    /// <see cref="RecoveryPollCount"/> polls so a switch that never triggers the bug doesn't leave
    /// anything running. Must stay detached: <see cref="ApplyTargetFormat"/> can run on the WPF
    /// dispatcher thread (see the class doc comment), and blocking it for the ~1.5s this poll
    /// needs would freeze the window.
    /// </summary>
    private void StartRecoveryWatch()
    {
        LastRecoveryTask = Task.Run(async () =>
        {
            for (var i = 0; i < RecoveryPollCount; i++)
            {
                await delay.Wait(RecoveryPollInterval);
                if (currentPlaybackState() == PlaybackState.Paused)
                {
                    AppLog.Info(
                        "Apple Music unexpectedly paused after a format switch — auto-resuming.",
                        category: "MediaTransport");
                    await mediaTransport.TogglePlayPauseAsync();
                    return;
                }
            }
        });
    }

    public IReadOnlySet<int> GetSupportedSampleRates(int bitDepth) => inner.GetSupportedSampleRates(bitDepth);

    public void SetTargeting(DeviceTargetingMode mode, string? pinnedDeviceId) => inner.SetTargeting(mode, pinnedDeviceId);

    public IReadOnlyList<RenderDevice> GetActiveRenderDevices() => inner.GetActiveRenderDevices();

    public DeviceTargetStatus GetTargetStatus() => inner.GetTargetStatus();

    private bool ShouldPauseAround(DeviceFormat target)
    {
        if (!pauseDuringSwitchEnabled() || currentPlaybackState() != PlaybackState.Playing)
        {
            return false;
        }

        return inner.GetCurrentFormat()?.SampleRateHz != target.SampleRateHz;
    }
}
