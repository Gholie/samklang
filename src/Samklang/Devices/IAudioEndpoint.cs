using Samklang.Domain;

namespace Samklang.Devices;

/// <summary>
/// Low-level operations against a specific Windows audio render device, addressed by its device
/// ID (see <see cref="RenderDevice.Id"/>): reading its actual shared-mode Device Format,
/// muting/unmuting it, and writing a new Device Format. Also exposes enumeration of every active
/// render device and the Windows default's ID, the two facts <see cref="Domain.DeviceTargetResolver"/>
/// needs to decide which device ID a caller should actually act on.
///
/// Carries no policy of its own (in particular, no "should we switch" and no "which device"
/// decision) — those live in <see cref="DeviceController"/>, which is tested against a fake of
/// this interface. The concrete implementation (<see cref="PolicyConfigAudioEndpoint"/>) is COM
/// interop and is not unit-testable in isolation.
/// </summary>
public interface IAudioEndpoint
{
    /// <summary>The device's current, actual Device Format, or null if it can't be read.</summary>
    DeviceFormat? GetCurrentFormat(string deviceId);

    /// <summary>Writes a new shared-mode Device Format to the device.</summary>
    void SetFormat(string deviceId, DeviceFormat format);

    /// <summary>Mutes or unmutes the device's master volume.</summary>
    void SetMuted(string deviceId, bool muted);

    /// <summary>
    /// The sample rates (Hz) the device actually accepts in shared mode at
    /// <paramref name="bitDepth"/>, probed against the device's mix pipeline rather than assumed.
    /// Implementations are expected to cache this per device, since probing every candidate rate
    /// is comparatively expensive and a device's capabilities don't change between Track changes.
    /// </summary>
    IReadOnlySet<int> GetSupportedSampleRates(string deviceId, int bitDepth);

    /// <summary>Every render device Windows currently reports as active, for the Settings device picker.</summary>
    IReadOnlyList<RenderDevice> GetActiveRenderDevices();

    /// <summary>The Windows default render device's ID, or null if none is available right now.</summary>
    string? GetDefaultRenderDeviceId();
}
