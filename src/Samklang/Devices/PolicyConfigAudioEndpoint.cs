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
/// Also probes which sample rates a device supports in shared mode, via WASAPI's
/// <c>IAudioClient.IsFormatSupported</c> — the standard capability check, distinct from
/// <c>IPolicyConfig</c>, which only reads/writes the format already in effect. Rate-family
/// clamping itself (deciding which supported rate to apply) is a pure policy kept out of this
/// class entirely; see <see cref="Samklang.Domain.RateFamilyClamp"/>.
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
    // on both keeps the cache correct if that ever changes.
    private readonly Dictionary<string, IReadOnlySet<int>> _supportedSampleRateCache = new();

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
        var channels = TryGetChannelCount(device) ?? 2;
        var waveFormat = new WaveFormat(format.SampleRateHz, format.BitDepth, channels);
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
        var cacheKey = $"{device.ID}|{bitDepth}";
        if (_supportedSampleRateCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var supported = ProbeSupportedSampleRates(device, bitDepth);
        _supportedSampleRateCache[cacheKey] = supported;
        return supported;
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
        var supported = new HashSet<int>();

        foreach (var rateHz in CandidateSampleRatesHz)
        {
            try
            {
                var candidate = new WaveFormat(rateHz, bitDepth, channels);
                if (device.AudioClient.IsFormatSupported(AudioClientShareMode.Shared, candidate))
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

        return supported;
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
