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
/// stream, and does not auto-resume on its own, even when nothing asked it to pause. The watch
/// only treats an observed <see cref="PlaybackState.Paused"/> as that bug when it shows up within
/// the first few polls — see <see cref="RecoveryBugWindowPollCount"/> — since a pause noticed
/// later in the window is far more likely to be the user pausing on purpose (format switches fire
/// on track change, exactly when a "wrong track, stop" pause is likely), and unconditionally
/// resuming that would silently undo a real user action. Only one watch is ever allowed to be
/// in flight: starting a new one cancels whatever the previous one was doing (see
/// <see cref="_recoveryCts"/>), because two real switches inside the ~1.5s window otherwise leave
/// two watches polling the same lagging SMTC state, and a superseded watch can observe a stale
/// <see cref="PlaybackState.Paused"/> left over from before the newer switch's own resume already
/// fixed things — and toggle again, leaving playback paused, which is exactly the bug this watch
/// exists to prevent.
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

    /// <summary>
    /// How many of the leading <see cref="RecoveryPollCount"/> polls still count as "the Apple
    /// Music bug" if they observe <see cref="PlaybackState.Paused"/>. In the diagnosed case the
    /// SMTC session flipped to <see cref="PlaybackState.Paused"/> on the very first poll (150ms
    /// after the switch); this adds one extra poll (300ms total) of slack for ordinary
    /// poll-to-poll timing jitter without reopening the false-positive this exists to close — a
    /// pause a user presses is very unlikely to land in the first 300ms after a track-change-driven
    /// switch, but by the far end of the window (e.g. poll 8, ~1.2s in) it reads as deliberate, not
    /// the bug. Deliberately a separate bound from <see cref="RecoveryPollCount"/>, which still
    /// governs how long the watch keeps running (a late, presumed-user pause still ends the watch —
    /// see <see cref="StartRecoveryWatch"/> — it just does not trigger a resume).
    /// </summary>
    private const int RecoveryBugWindowPollCount = 2;

    /// <summary>
    /// Guards <see cref="_recoveryCts"/> so cancelling the previous recovery watch and installing
    /// the new one (in <see cref="StartRecoveryWatch"/>) happens as one atomic step — without this,
    /// two <see cref="ApplyTargetFormat"/> calls racing each other on different threads (the class
    /// doc comment covers why that's possible) could both read the same "previous" token and each
    /// think they're the one that superseded it.
    /// </summary>
    private readonly object _recoveryGate = new();

    /// <summary>The in-flight recovery watch's cancellation source, if a watch is currently
    /// running — see <see cref="StartRecoveryWatch"/>.</summary>
    private CancellationTokenSource? _recoveryCts;

    public DeviceFormat? GetCurrentFormat() => inner.GetCurrentFormat();

    /// <summary>
    /// Test-only seam: the <see cref="Task"/> started by the most recent recovery watch (or null
    /// if none has started yet), so tests can await its completion instead of racing detached
    /// background work. Resolves even when that watch was cancelled — see
    /// <see cref="StartRecoveryWatch"/> — so awaiting it never leaves a test hanging or throws.
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

    /// <summary>
    /// How long <see cref="ToggleAndWait"/> waits for a play/pause toggle to complete before giving
    /// up on it. <see cref="IMediaTransport.TogglePlayPauseAsync"/> is a cross-process SMTC call to
    /// an app already running on the same machine, not a network round trip — it normally lands in
    /// well under 100ms — so 2 seconds is generous headroom for ordinary OS scheduling noise while
    /// still keeping the worst case (Apple Music alive but wedged) a brief, bounded hitch rather
    /// than the indefinite freeze <see cref="ToggleAndWait"/> used to risk on whatever thread called
    /// <see cref="ApplyTargetFormat"/> — including the WPF dispatcher thread (see the class doc
    /// comment). Chosen independently of <see cref="RecoveryPollInterval"/>/<see cref="RecoveryPollCount"/>
    /// above, which bound a detached background poll that is never on the UI thread and so carries
    /// no equivalent freeze risk.
    ///
    /// <para>
    /// An instance property with a default, rather than a private const, purely as a test seam —
    /// see <c>PlaybackPausingDeviceControllerTests</c>' hanging-transport coverage, which overrides
    /// this to a few milliseconds so that coverage doesn't cost the suite real wall-clock time.
    /// Production code (the composition root in <c>MainWindow</c>) never touches it.
    /// </para>
    /// </summary>
    internal TimeSpan ToggleTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// See the class doc comment for why this goes through <see cref="Task.Run(Func{Task})"/>
    /// instead of awaiting <see cref="IMediaTransport.TogglePlayPauseAsync"/> inline. The wait
    /// itself is bounded by <see cref="ToggleTimeout"/>: <see cref="Task.Wait(TimeSpan)"/> is a
    /// plain blocking wait (no <c>await</c>, so nothing here captures the calling thread's
    /// <see cref="System.Threading.SynchronizationContext"/> the way the deadlock this class avoids
    /// would need) that simply stops waiting once the timeout elapses, leaving the toggle task
    /// running loose in the background if it ever does complete. A caller on the WPF dispatcher
    /// thread (see the class doc comment) degrades to a missed pause or resume instead of a frozen
    /// window — the failure mode the timeout exists to bound, not something callers need to react
    /// to, so this returns normally either way rather than throwing.
    /// </summary>
    private void ToggleAndWait()
    {
        var toggleTask = Task.Run(mediaTransport.TogglePlayPauseAsync);
        if (!toggleTask.Wait(ToggleTimeout))
        {
            AppLog.Warn(
                $"Play/pause toggle did not complete within {ToggleTimeout.TotalSeconds:0.#}s — " +
                "Apple Music may be unresponsive. Continuing without it; the toggle may still land " +
                "later if the app recovers.",
                category: "MediaTransport");
            return;
        }

        // Already finished by the time Wait(TimeSpan) returned true above; this just unwraps a
        // fault the same way the original inline GetAwaiter().GetResult() did.
        // SmtcTrackWatcher.TogglePlayPauseAsync catches and logs its own exceptions, so in
        // practice this should never actually throw.
        toggleTask.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Detached (not awaited by <see cref="ApplyTargetFormat"/>) background poll that watches for
    /// Apple Music unexpectedly reporting <see cref="PlaybackState.Paused"/> shortly after a real
    /// switch it wasn't asked to pause around, and resumes it once if the pause showed up within
    /// <see cref="RecoveryBugWindowPollCount"/> polls — see that constant's doc comment for why a
    /// later pause is treated as the user's, not the bug's, and left alone. Bounded to
    /// <see cref="RecoveryPollCount"/> polls either way, so a switch that never triggers the bug
    /// doesn't leave anything running. Must stay detached: <see cref="ApplyTargetFormat"/> can run
    /// on the WPF dispatcher thread (see the class doc comment), and blocking it for the ~1.5s
    /// this poll needs would freeze the window.
    ///
    /// <para>
    /// Cancels whatever the previous watch was doing before starting this one, under
    /// <see cref="_recoveryGate"/>, so at most one watch is ever polling — see the class doc
    /// comment for why a superseded watch left running is dangerous (it can act on stale SMTC
    /// state and double-toggle into a pause). The cancellation is caught and swallowed rather than
    /// left to fault the task: a cancelled watch must still complete <see cref="LastRecoveryTask"/>
    /// successfully so tests can await it, and an unobserved faulted task would otherwise surface
    /// later as a <see cref="TaskScheduler.UnobservedTaskException"/>.
    /// </para>
    /// </summary>
    private void StartRecoveryWatch()
    {
        var cts = new CancellationTokenSource();
        lock (_recoveryGate)
        {
            _recoveryCts?.Cancel();
            _recoveryCts = cts;
        }

        var token = cts.Token;
        LastRecoveryTask = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < RecoveryPollCount; i++)
                {
                    await delay.Wait(RecoveryPollInterval, token);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (currentPlaybackState() != PlaybackState.Paused)
                    {
                        continue;
                    }

                    if (i >= RecoveryBugWindowPollCount)
                    {
                        AppLog.Info(
                            "Playback paused after a format switch, past the early recovery window — presuming a user pause, not resuming.",
                            category: "MediaTransport");
                        return;
                    }

                    AppLog.Info(
                        "Apple Music unexpectedly paused after a format switch — auto-resuming.",
                        category: "MediaTransport");
                    await mediaTransport.TogglePlayPauseAsync();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer switch's watch — see the doc comment above. Not a real
                // failure, so it's swallowed here rather than left to fault the task.
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

        // A null current format means no render device resolved (see DeviceController.ApplyTargetFormat),
        // so the inner switch below is about to early-return false without touching anything — the
        // same kind of no-op a same-rate match is, just for a different reason. Guarding on it here
        // keeps that no-op from still bracketing itself in a pointless, audible pause/resume blip.
        var current = inner.GetCurrentFormat();
        return current is not null && current.Value.SampleRateHz != target.SampleRateHz;
    }
}
