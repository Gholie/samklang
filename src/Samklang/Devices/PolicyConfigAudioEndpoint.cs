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
/// This issue intentionally keeps switching simple: it writes the Target Format's sample rate
/// and pinned 24-bit depth directly, without probing the device's supported formats or clamping
/// to its best rate in the same rate family. That capability probing/clamping is broader M1 work
/// (docs/PLAN.md) tracked separately from this tracer bullet.
///
/// Not unit-testable in isolation — <see cref="DeviceController"/> carries the testable
/// switch-or-skip decision and is exercised against a fake of <see cref="IAudioEndpoint"/> instead.
/// </summary>
public sealed class PolicyConfigAudioEndpoint : IAudioEndpoint
{
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
