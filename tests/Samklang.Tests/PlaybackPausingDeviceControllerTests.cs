using System.Threading;
using Samklang.Devices;
using Samklang.Domain;
using Samklang.Sessions;
using Samklang.Timing;
using Xunit;

namespace Samklang.Tests;

public class PlaybackPausingDeviceControllerTests
{
    /// <summary>Completes instantly (no real wall-clock wait), so recovery-watch tests run fast
    /// and deterministically instead of racing real <see cref="Task.Delay(TimeSpan)"/> timing.
    /// Still honors an already-cancelled token the same way <see cref="Task.Delay(TimeSpan,CancellationToken)"/>
    /// would, so tests that pre-cancel a watch's token don't need a slower, real-waiting fake.</summary>
    private sealed class FakeDelay : IDelay
    {
        public int WaitCount { get; private set; }

        public Task Wait(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A <see cref="IDelay"/> whose very first <see cref="Wait"/> call never completes on its own —
    /// it parks there until either <see cref="ParkedCancellationToken"/> is cancelled or the test
    /// times out waiting on <see cref="FirstPollReached"/>. Every later call (i.e. any watch
    /// started after the first one) resolves immediately, like <see cref="FakeDelay"/>. This makes
    /// it possible to deterministically simulate a second <see cref="PlaybackPausingDeviceController.ApplyTargetFormat"/>
    /// superseding a first recovery watch that is still mid-poll, instead of racing two real
    /// <see cref="Task.Run(Action)"/> background threads against the test's own thread.
    /// </summary>
    private sealed class FirstPollParkingDelay : IDelay
    {
        private readonly ManualResetEventSlim _firstPollReached = new(initialState: false);
        private readonly TaskCompletionSource _firstPollGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public Task Wait(TimeSpan duration, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) > 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            cancellationToken.Register(() => _firstPollGate.TrySetCanceled(cancellationToken));
            _firstPollReached.Set();
            return _firstPollGate.Task;
        }

        /// <summary>Blocks the calling (test) thread until the first ever <see cref="Wait"/> call
        /// has been made and is parked, so the test can be sure a second switch will find that
        /// watch still in flight before it supersedes it.</summary>
        public void WaitForFirstPoll() => Assert.True(_firstPollReached.Wait(TimeSpan.FromSeconds(5)));
    }

    private sealed class FakeDeviceController : IDeviceController
    {
        public DeviceFormat? Current { get; set; }
        public List<string> Calls { get; } = [];

        public DeviceFormat? GetCurrentFormat()
        {
            Calls.Add("get");
            return Current;
        }

        public bool ApplyTargetFormat(DeviceFormat target)
        {
            if (Current?.SampleRateHz == target.SampleRateHz)
            {
                Calls.Add("apply:skip");
                return false;
            }

            Calls.Add($"apply:{target}");
            Current = target;
            return true;
        }

        public IReadOnlySet<int> GetSupportedSampleRates(int bitDepth) => new HashSet<int>();

        public void SetTargeting(DeviceTargetingMode mode, string? pinnedDeviceId) => Calls.Add("set-targeting");

        public IReadOnlyList<RenderDevice> GetActiveRenderDevices() => [];

        public DeviceTargetStatus GetTargetStatus() => new(null, null, false);
    }

    private sealed class FakeMediaTransport : IMediaTransport
    {
        public byte[]? ArtworkBytes => null;
        public event EventHandler? ArtworkChanged { add { } remove { } }
        public List<string> Calls { get; } = [];

        public Task SkipPreviousAsync() => Task.CompletedTask;

        public Task TogglePlayPauseAsync()
        {
            Calls.Add("toggle");
            return Task.CompletedTask;
        }

        public Task SkipNextAsync() => Task.CompletedTask;
    }

    [Fact]
    public void ApplyTargetFormat_pauses_and_resumes_around_a_real_switch_when_enabled_and_playing()
    {
        var inner = new FakeDeviceController { Current = new DeviceFormat(48_000, 24) };
        var transport = new FakeMediaTransport();
        var controller = new PlaybackPausingDeviceController(
            inner, transport, () => PlaybackState.Playing, () => true, new FakeDelay());
        var target = new DeviceFormat(44_100, 24);

        var switched = controller.ApplyTargetFormat(target);

        Assert.True(switched);
        // The inner switch itself happened, and the transport was toggled exactly twice
        // (pause before, resume after) — see the ordering test below for the sequencing.
        Assert.Equal(["get", $"apply:{target}"], inner.Calls);
        Assert.Equal(["toggle", "toggle"], transport.Calls);
    }

    [Fact]
    public void ApplyTargetFormat_pauses_before_and_resumes_after_the_inner_switch()
    {
        var inner = new FakeDeviceController { Current = new DeviceFormat(48_000, 24) };
        var events = new List<string>();
        var transport = new RecordingMediaTransport(events);
        var recordingInner = new RecordingDeviceController(inner, events);
        var controller = new PlaybackPausingDeviceController(
            recordingInner, transport, () => PlaybackState.Playing, () => true, new FakeDelay());

        controller.ApplyTargetFormat(new DeviceFormat(44_100, 24));

        Assert.Equal(["toggle", "apply", "toggle"], events);
    }

    private sealed class RecordingMediaTransport(List<string> events) : IMediaTransport
    {
        public byte[]? ArtworkBytes => null;
        public event EventHandler? ArtworkChanged { add { } remove { } }

        public Task SkipPreviousAsync() => Task.CompletedTask;

        public Task TogglePlayPauseAsync()
        {
            events.Add("toggle");
            return Task.CompletedTask;
        }

        public Task SkipNextAsync() => Task.CompletedTask;
    }

    private sealed class RecordingDeviceController(IDeviceController inner, List<string> events) : IDeviceController
    {
        public DeviceFormat? GetCurrentFormat() => inner.GetCurrentFormat();

        public bool ApplyTargetFormat(DeviceFormat target)
        {
            events.Add("apply");
            return inner.ApplyTargetFormat(target);
        }

        public IReadOnlySet<int> GetSupportedSampleRates(int bitDepth) => inner.GetSupportedSampleRates(bitDepth);

        public void SetTargeting(DeviceTargetingMode mode, string? pinnedDeviceId) => inner.SetTargeting(mode, pinnedDeviceId);

        public IReadOnlyList<RenderDevice> GetActiveRenderDevices() => inner.GetActiveRenderDevices();

        public DeviceTargetStatus GetTargetStatus() => inner.GetTargetStatus();
    }

    [Fact]
    public void ApplyTargetFormat_does_not_pause_when_the_toggle_is_off()
    {
        var inner = new FakeDeviceController { Current = new DeviceFormat(48_000, 24) };
        var transport = new FakeMediaTransport();
        var controller = new PlaybackPausingDeviceController(
            inner, transport, () => PlaybackState.Playing, () => false, new FakeDelay());

        controller.ApplyTargetFormat(new DeviceFormat(44_100, 24));

        Assert.Empty(transport.Calls);
    }

    [Fact]
    public void ApplyTargetFormat_does_not_pause_when_playback_is_not_currently_playing()
    {
        var inner = new FakeDeviceController { Current = new DeviceFormat(48_000, 24) };
        var transport = new FakeMediaTransport();
        var controller = new PlaybackPausingDeviceController(
            inner, transport, () => PlaybackState.Paused, () => true, new FakeDelay());

        controller.ApplyTargetFormat(new DeviceFormat(44_100, 24));

        Assert.Empty(transport.Calls);
    }

    [Fact]
    public void ApplyTargetFormat_does_not_pause_when_the_rate_already_matches()
    {
        var target = new DeviceFormat(48_000, 24);
        var inner = new FakeDeviceController { Current = target };
        var transport = new FakeMediaTransport();
        var controller = new PlaybackPausingDeviceController(
            inner, transport, () => PlaybackState.Playing, () => true, new FakeDelay());

        var switched = controller.ApplyTargetFormat(target);

        Assert.False(switched);
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public void ApplyTargetFormat_still_resumes_when_the_inner_switch_throws()
    {
        var inner = new ThrowingDeviceController { Current = new DeviceFormat(48_000, 24) };
        var transport = new FakeMediaTransport();
        var controller = new PlaybackPausingDeviceController(
            inner, transport, () => PlaybackState.Playing, () => true, new FakeDelay());

        Assert.Throws<InvalidOperationException>(() => controller.ApplyTargetFormat(new DeviceFormat(44_100, 24)));

        Assert.Equal(2, transport.Calls.Count);
    }

    private sealed class ThrowingDeviceController : IDeviceController
    {
        public DeviceFormat? Current { get; set; }

        public DeviceFormat? GetCurrentFormat() => Current;

        public bool ApplyTargetFormat(DeviceFormat target) => throw new InvalidOperationException("switch failed");

        public IReadOnlySet<int> GetSupportedSampleRates(int bitDepth) => new HashSet<int>();

        public void SetTargeting(DeviceTargetingMode mode, string? pinnedDeviceId)
        {
        }

        public IReadOnlyList<RenderDevice> GetActiveRenderDevices() => [];

        public DeviceTargetStatus GetTargetStatus() => new(null, null, false);
    }

    /// <summary>
    /// Regression test for the deadlock risk documented on <see cref="PlaybackPausingDeviceController"/>:
    /// <see cref="PlaybackPausingDeviceController.ApplyTargetFormat"/> can run on the WPF dispatcher
    /// thread (via <c>TrackSyncCoordinator.Resume</c>/<c>ApplyLateResolution</c>), which has a
    /// <see cref="SynchronizationContext"/> that marshals continuations back onto itself via
    /// <see cref="SynchronizationContext.Post"/>. <see cref="ContextSensitiveMediaTransport"/> mimics
    /// <c>SmtcTrackWatcher.TogglePlayPauseAsync</c>'s real await (no <c>ConfigureAwait(false)</c>). If
    /// the toggle were awaited inline on the calling thread instead of escaping onto the thread pool
    /// first, that await's continuation would be posted to whatever context was current when it
    /// started — the assertion below fails if that ever happens again.
    /// </summary>
    [Fact]
    public void ApplyTargetFormat_does_not_marshal_the_toggle_back_onto_the_calling_synchronization_context()
    {
        var previousContext = SynchronizationContext.Current;
        var recordingContext = new RecordingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(recordingContext);
        try
        {
            var inner = new FakeDeviceController { Current = new DeviceFormat(48_000, 24) };
            var transport = new ContextSensitiveMediaTransport();
            var controller = new PlaybackPausingDeviceController(
                inner, transport, () => PlaybackState.Playing, () => true, new FakeDelay());

            controller.ApplyTargetFormat(new DeviceFormat(44_100, 24));

            Assert.False(recordingContext.PostWasCalled);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    /// <summary>Runs posted continuations inline (so a regression fails the assertion instead of
    /// hanging the test run) while still recording whether marshaling back was ever attempted.</summary>
    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        public bool PostWasCalled { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostWasCalled = true;
            d(state);
        }
    }

    private sealed class ContextSensitiveMediaTransport : IMediaTransport
    {
        public byte[]? ArtworkBytes => null;
        public event EventHandler? ArtworkChanged { add { } remove { } }

        public Task SkipPreviousAsync() => Task.CompletedTask;

        // Mirrors SmtcTrackWatcher.TogglePlayPauseAsync: a real await with default,
        // context-capturing continuation behavior — not ConfigureAwait(false).
        public async Task TogglePlayPauseAsync() => await Task.Delay(1);

        public Task SkipNextAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// Regression test for the "Apple Music actually pauses under Keep Feeding Audio" bug: after
    /// a real switch that nothing proactively paused around, an unexpected drop to
    /// <see cref="PlaybackState.Paused"/> should be auto-resumed once.
    /// </summary>
    [Fact]
    public async Task Recovery_resumes_playback_when_it_unexpectedly_pauses_after_a_real_switch()
    {
        var inner = new FakeDeviceController { Current = new DeviceFormat(48_000, 24) };
        var transport = new FakeMediaTransport();
        var callCount = 0;
        // Call 0 is ApplyTargetFormat's own "was playing before the switch" check; every call
        // after that is a recovery poll — reporting Paused from the very first poll mirrors how
        // quickly the real SMTC session flipped in the diagnosed case.
        PlaybackState? StateFn() => callCount++ == 0 ? PlaybackState.Playing : PlaybackState.Paused;
        var controller = new PlaybackPausingDeviceController(inner, transport, StateFn, () => false, new FakeDelay());

        var switched = controller.ApplyTargetFormat(new DeviceFormat(44_100, 24));

        Assert.True(switched);
        Assert.NotNull(controller.LastRecoveryTask);
        await controller.LastRecoveryTask!;
        Assert.Equal(["toggle"], transport.Calls);
    }

    /// <summary>
    /// Regression test for the "user presses pause right after a track change" false positive:
    /// a track change is exactly when the recovery watch's window opens (it starts after every
    /// real switch), so a pause the user genuinely intends must not get silently un-paused. Paused
    /// first showing up at poll 8 of 10 — comfortably past <c>RecoveryBugWindowPollCount</c> —
    /// reads as deliberate, unlike the diagnosed bug case which flipped on the very first poll.
    /// </summary>
    [Fact]
    public async Task Recovery_does_not_resume_when_paused_first_appears_late_in_the_window()
    {
        var inner = new FakeDeviceController { Current = new DeviceFormat(48_000, 24) };
        var transport = new FakeMediaTransport();
        var callCount = 0;
        // Call 0 is ApplyTargetFormat's own "was playing" check; calls 1-7 (polls 1-7) still
        // report Playing, and Paused first appears on call 8 — poll 8 of 10.
        PlaybackState? StateFn() => callCount++ < 8 ? PlaybackState.Playing : PlaybackState.Paused;
        var controller = new PlaybackPausingDeviceController(inner, transport, StateFn, () => false, new FakeDelay());

        var switched = controller.ApplyTargetFormat(new DeviceFormat(44_100, 24));

        Assert.True(switched);
        Assert.NotNull(controller.LastRecoveryTask);
        await controller.LastRecoveryTask!;
        Assert.Empty(transport.Calls);
    }

    /// <summary>
    /// Regression test for the concurrent-watch double-toggle bug: two real switches inside the
    /// ~1.5s recovery window used to leave two watches polling the same lagging SMTC state, and a
    /// superseded watch could observe a stale <see cref="PlaybackState.Paused"/> left over from
    /// before the newer switch's own watch had already resumed things — toggling again and leaving
    /// playback paused. <see cref="FirstPollParkingDelay"/> keeps the first watch reliably parked
    /// mid-poll (instead of racing two real background threads against this test) so the second
    /// <see cref="PlaybackPausingDeviceController.ApplyTargetFormat"/> call is guaranteed to find it
    /// still in flight and cancel it.
    /// </summary>
    [Fact]
    public async Task Recovery_cancels_the_first_watch_when_a_second_switch_supersedes_it_within_the_window()
    {
        var inner = new FakeDeviceController { Current = new DeviceFormat(48_000, 24) };
        var transport = new FakeMediaTransport();
        var callCount = 0;
        // The first two calls are the two ApplyTargetFormat calls' own "was playing" checks. The
        // first watch never gets far enough to call currentPlaybackState at all — it's parked at
        // its very first poll's Wait call (see FirstPollParkingDelay) when the second switch
        // supersedes it — so every call after the first two belongs to the second watch.
        PlaybackState? StateFn() => callCount++ < 2 ? PlaybackState.Playing : PlaybackState.Paused;
        var delay = new FirstPollParkingDelay();
        var controller = new PlaybackPausingDeviceController(inner, transport, StateFn, () => false, delay);

        controller.ApplyTargetFormat(new DeviceFormat(44_100, 24));
        var firstWatch = controller.LastRecoveryTask;
        delay.WaitForFirstPoll();

        controller.ApplyTargetFormat(new DeviceFormat(48_000, 24));
        var secondWatch = controller.LastRecoveryTask;

        Assert.NotSame(firstWatch, secondWatch);
        await firstWatch!;
        await secondWatch!;

        // Exactly one resume toggle, from the surviving second watch. If the first watch weren't
        // cancelled, it would eventually wake up, observe the same stale Paused state, and toggle
        // again — undoing the second watch's correct resume and leaving playback paused.
        Assert.Equal(["toggle"], transport.Calls);
    }

    [Fact]
    public void Recovery_does_not_fire_when_the_switch_was_a_no_op()
    {
        var target = new DeviceFormat(48_000, 24);
        var inner = new FakeDeviceController { Current = target };
        var transport = new FakeMediaTransport();
        var controller = new PlaybackPausingDeviceController(
            inner, transport, () => PlaybackState.Playing, () => false, new FakeDelay());

        var switched = controller.ApplyTargetFormat(target);

        Assert.False(switched);
        Assert.Null(controller.LastRecoveryTask);
        Assert.Empty(transport.Calls);
    }

    [Fact]
    public void Recovery_does_not_fire_when_playback_was_not_playing_before_the_switch()
    {
        var inner = new FakeDeviceController { Current = new DeviceFormat(48_000, 24) };
        var transport = new FakeMediaTransport();
        var controller = new PlaybackPausingDeviceController(
            inner, transport, () => PlaybackState.Paused, () => false, new FakeDelay());

        var switched = controller.ApplyTargetFormat(new DeviceFormat(44_100, 24));

        Assert.True(switched);
        Assert.Null(controller.LastRecoveryTask);
        Assert.Empty(transport.Calls);
    }

    /// <summary>The proactive pause/resume branch already brackets the switch itself — it must
    /// not also kick off the reactive recovery watch, or a legitimate pause could get an extra,
    /// unwanted resume appended after it.</summary>
    [Fact]
    public void Recovery_does_not_also_fire_on_the_proactive_pause_during_switch_path()
    {
        var inner = new FakeDeviceController { Current = new DeviceFormat(48_000, 24) };
        var transport = new FakeMediaTransport();
        var controller = new PlaybackPausingDeviceController(
            inner, transport, () => PlaybackState.Playing, () => true, new FakeDelay());

        controller.ApplyTargetFormat(new DeviceFormat(44_100, 24));

        Assert.Null(controller.LastRecoveryTask);
        Assert.Equal(["toggle", "toggle"], transport.Calls);
    }

    /// <summary>Bounded-window regression: if playback never reports <see cref="PlaybackState.Paused"/>,
    /// the poll must still terminate on its own rather than continuing indefinitely.</summary>
    [Fact]
    public async Task Recovery_gives_up_after_the_bounded_poll_window_if_playback_never_reports_paused()
    {
        var inner = new FakeDeviceController { Current = new DeviceFormat(48_000, 24) };
        var transport = new FakeMediaTransport();
        var delay = new FakeDelay();
        var controller = new PlaybackPausingDeviceController(
            inner, transport, () => PlaybackState.Playing, () => false, delay);

        controller.ApplyTargetFormat(new DeviceFormat(44_100, 24));

        Assert.NotNull(controller.LastRecoveryTask);
        await controller.LastRecoveryTask!;
        // Mirrors PlaybackPausingDeviceController's private RecoveryPollCount (10) — the poll
        // took exactly that many waits and then stopped, rather than looping forever.
        Assert.Equal(10, delay.WaitCount);
        Assert.Empty(transport.Calls);
    }
}
