using Samklang.Domain;

namespace Samklang.Devices;

/// <summary>
/// Low-level operations against the current default Windows audio render device: reading its
/// actual shared-mode Device Format, muting/unmuting it, and writing a new Device Format.
/// Carries no policy of its own (in particular, no "should we switch" decision) — that lives in
/// <see cref="DeviceController"/>, which is tested against a fake of this interface. The
/// concrete implementation (<see cref="PolicyConfigAudioEndpoint"/>) is COM interop and is not
/// unit-testable in isolation.
/// </summary>
public interface IAudioEndpoint
{
    /// <summary>The device's current, actual Device Format, or null if it can't be read.</summary>
    DeviceFormat? GetCurrentFormat();

    /// <summary>Writes a new shared-mode Device Format to the device.</summary>
    void SetFormat(DeviceFormat format);

    /// <summary>Mutes or unmutes the device's master volume.</summary>
    void SetMuted(bool muted);
}
