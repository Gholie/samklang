using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Samklang.Domain;

namespace Samklang.Devices;

/// <summary>
/// The concrete, COM-backed <see cref="IAudioEndpoint"/>: talks to a render device (addressed by
/// device ID) via <c>IPolicyConfig</c> (shared-mode Device Format) and NAudio's
/// <c>AudioEndpointVolume</c> (mute), both standard — if undocumented — Core Audio interop also
/// used by tools like WindowsLosslessSwitcher. Also enumerates active render devices and reads
/// the Windows default's ID via NAudio's <c>MMDeviceEnumerator</c>, the two facts device targeting
/// (<see cref="Domain.DeviceTargetResolver"/>) needs.
///
/// Also probes which sample rates a device supports, via WASAPI's
/// <c>IAudioClient.IsFormatSupported</c> in EXCLUSIVE mode — the mode that reflects the driver's
/// actual capabilities (the Sound control panel's format list). Shared mode is useless for this:
/// it answers yes only for the format matching the device's current mix format, so a shared-mode
/// probe merely echoes the rate the device is already set to (verified live: a FiiO K11 at 48 kHz
/// answered yes to 48 kHz alone in shared mode, and to every rate from 44.1 to 384 kHz in
/// exclusive mode). Candidates are rate-patched clones of the device's own configured Device
/// Format (see <see cref="WaveFormatRatePatcher"/>), because drivers reject hand-built layouts
/// that differ from their exposed ones. Rate-family clamping itself (deciding which supported
/// rate to apply) is a pure policy kept out of this class entirely; see
/// <see cref="Samklang.Domain.RateFamilyClamp"/>.
///
/// Not unit-testable in isolation — <see cref="DeviceController"/> carries the testable
/// switch-or-skip and device-targeting decisions and is exercised against a fake of
/// <see cref="IAudioEndpoint"/> instead.
/// </summary>
public sealed class PolicyConfigAudioEndpoint : IAudioEndpoint
{
    /// <summary>Every sample rate CONTEXT.md's vocabulary recognizes, across both rate families.</summary>
    private static readonly int[] CandidateSampleRatesHz = [44_100, 48_000, 88_200, 96_000, 176_400, 192_000];

    // Keyed by "{device id}|{bit depth}" — probing is per-device (and, in principle, per bit
    // depth), and this process only ever runs against one bit depth (24-bit, pinned), but keying
    // on both keeps the cache correct if that ever changes. Concurrent because callers reach this
    // from both SMTC callback threads (track-change switches) and the UI thread (poll timer,
    // Dispatcher-marshaled late resolutions).
    private readonly ConcurrentDictionary<string, IReadOnlySet<int>> _supportedSampleRateCache = new();

    public DeviceFormat? GetCurrentFormat(string deviceId)
    {
        try
        {
            using var device = GetDevice(deviceId);
            var format = PolicyConfigInterop.GetDeviceFormat(device.ID);
            return format is null ? null : new DeviceFormat(format.SampleRate, format.BitsPerSample);
        }
        catch
        {
            return null;
        }
    }

    public void SetFormat(string deviceId, DeviceFormat format)
    {
        using var device = GetDevice(deviceId);

        // Preserve the device's configured container layout (extensible header, channel mask,
        // container/valid bits) and change only the rate — drivers reject hand-built layouts,
        // and DeviceController's policy never switches for bit depth alone anyway.
        var current = TryGetDeviceFormat(device.ID);
        var waveFormat = current is null
            ? new WaveFormat(format.SampleRateHz, format.BitDepth, TryGetChannelCount(device) ?? 2)
            : WaveFormatRatePatcher.WithSampleRate(current, format.SampleRateHz);

        PolicyConfigInterop.SetDeviceFormat(device.ID, waveFormat);
    }

    public void SetMuted(string deviceId, bool muted)
    {
        using var device = GetDevice(deviceId);
        device.AudioEndpointVolume.Mute = muted;
    }

    public IReadOnlySet<int> GetSupportedSampleRates(string deviceId, int bitDepth)
    {
        using var device = GetDevice(deviceId);
        return _supportedSampleRateCache.GetOrAdd(
            $"{device.ID}|{bitDepth}",
            _ => ProbeSupportedSampleRates(device, bitDepth));
    }

    public IReadOnlyList<RenderDevice> GetActiveRenderDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            var devices = new List<RenderDevice>(collection.Count);
            foreach (var device in collection)
            {
                devices.Add(new RenderDevice(device.ID, device.FriendlyName));
                device.Dispose();
            }

            return devices;
        }
        catch
        {
            // Enumeration can fail very early at startup, or if the audio service is
            // unavailable; an empty list is a safe "nothing known" answer rather than crashing.
            return [];
        }
    }

    public string? GetDefaultRenderDeviceId()
    {
        try
        {
            using var device = GetDefaultRenderDevice();
            return device.ID;
        }
        catch
        {
            // No default render device exists (e.g. all devices disabled/unplugged).
            return null;
        }
    }

    private static IReadOnlySet<int> ProbeSupportedSampleRates(MMDevice device, int bitDepth)
    {
        var channels = TryGetChannelCount(device) ?? 2;
        var deviceFormat = TryGetDeviceFormat(device.ID);
        var supported = new HashSet<int>();

        foreach (var rateHz in CandidateSampleRatesHz)
        {
            try
            {
                var candidate = deviceFormat is null
                    ? new WaveFormat(rateHz, bitDepth, channels)
                    : WaveFormatRatePatcher.WithSampleRate(deviceFormat, rateHz);
                if (device.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, candidate))
                {
                    supported.Add(rateHz);
                }
            }
            catch
            {
                // A single candidate rate failing to probe (e.g. a transient AudioClient error)
                // shouldn't fail the whole probe — just treat that rate as unsupported.
            }
        }

        // The configured format's own rate works by definition. This also keeps the set
        // non-degenerate for virtual devices whose drivers reject exclusive mode outright.
        if (deviceFormat is not null)
        {
            supported.Add(deviceFormat.SampleRate);
        }

        return supported;
    }

    private static WaveFormat? TryGetDeviceFormat(string deviceId)
    {
        try
        {
            return PolicyConfigInterop.GetDeviceFormat(deviceId);
        }
        catch
        {
            return null;
        }
    }

    private static MMDevice GetDevice(string deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDevice(deviceId);
    }

    private static MMDevice GetDefaultRenderDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private static int? TryGetChannelCount(MMDevice device)
    {
        try
        {
            return device.AudioClient.MixFormat.Channels;
        }
        catch
        {
            return null;
        }
    }
}
