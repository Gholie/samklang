using NAudio.CoreAudioApi;
using NAudio.Wave;
using Samklang.Domain;

namespace Samklang.Devices;

/// <summary>
/// The concrete, COM-backed <see cref="IAudioEndpoint"/>: talks to the current default render
/// device via <c>IPolicyConfig</c> (shared-mode Device Format) and NAudio's
/// <c>AudioEndpointVolume</c> (mute), both standard — if undocumented — Core Audio interop also
/// used by tools like WindowsLosslessSwitcher.
///
/// Also probes which sample rates the device supports in shared mode, via WASAPI's
/// <c>IAudioClient.IsFormatSupported</c> — the standard capability check, distinct from
/// <c>IPolicyConfig</c>, which only reads/writes the format already in effect. Rate-family
/// clamping itself (deciding which supported rate to apply) is a pure policy kept out of this
/// class entirely; see <see cref="Samklang.Domain.RateFamilyClamp"/>.
///
/// Not unit-testable in isolation — <see cref="DeviceController"/> carries the testable
/// switch-or-skip decision and is exercised against a fake of <see cref="IAudioEndpoint"/> instead.
/// </summary>
public sealed class PolicyConfigAudioEndpoint : IAudioEndpoint
{
    /// <summary>Every sample rate CONTEXT.md's vocabulary recognizes, across both rate families.</summary>
    private static readonly int[] CandidateSampleRatesHz = [44_100, 48_000, 88_200, 96_000, 176_400, 192_000];

    // Keyed by "{device id}|{bit depth}" — probing is per-device (and, in principle, per bit
    // depth), and this process only ever runs against one bit depth (24-bit, pinned), but keying
    // on both keeps the cache correct if that ever changes.
    private readonly Dictionary<string, IReadOnlySet<int>> _supportedSampleRateCache = new();

    public DeviceFormat? GetCurrentFormat()
    {
        try
        {
            using var device = GetDefaultRenderDevice();
            var format = PolicyConfigInterop.GetDeviceFormat(device.ID);
            return format is null ? null : new DeviceFormat(format.SampleRate, format.BitsPerSample);
        }
        catch
        {
            return null;
        }
    }

    public void SetFormat(DeviceFormat format)
    {
        using var device = GetDefaultRenderDevice();
        var channels = TryGetChannelCount(device) ?? 2;
        var waveFormat = new WaveFormat(format.SampleRateHz, format.BitDepth, channels);
        PolicyConfigInterop.SetDeviceFormat(device.ID, waveFormat);
    }

    public void SetMuted(bool muted)
    {
        using var device = GetDefaultRenderDevice();
        device.AudioEndpointVolume.Mute = muted;
    }

    public IReadOnlySet<int> GetSupportedSampleRates(int bitDepth)
    {
        using var device = GetDefaultRenderDevice();
        var cacheKey = $"{device.ID}|{bitDepth}";
        if (_supportedSampleRateCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var supported = ProbeSupportedSampleRates(device, bitDepth);
        _supportedSampleRateCache[cacheKey] = supported;
        return supported;
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
