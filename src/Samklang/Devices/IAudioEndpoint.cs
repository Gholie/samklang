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
    /// <summary>
    /// The device's current, actual Device Format, or null if it can't be read. The bit depth is
    /// the audible one (<see cref="WaveFormatBitDepth.Effective"/>) — for a WAVE_FORMAT_EXTENSIBLE
    /// device (nearly all modern DACs) that's <c>wValidBitsPerSample</c>, not the wider container
    /// size <c>wBitsPerSample</c> reports.
    /// </summary>
    DeviceFormat? GetCurrentFormat(string deviceId);

    /// <summary>Writes a new shared-mode Device Format to the device.</summary>
    void SetFormat(string deviceId, DeviceFormat format);

    /// <summary>Mutes or unmutes the device's master volume.</summary>
    void SetMuted(string deviceId, bool muted);

    /// <summary>
    /// The sample rates (Hz) the device's driver actually supports (its Device Format can be
    /// switched to), probed against the driver rather than assumed; <paramref name="bitDepth"/>
    /// is a hint used only when the device's configured format can't be read.
    /// Implementations are expected to cache this per device, since probing every candidate rate
    /// is comparatively expensive and a device's capabilities don't change between Track changes.
    /// </summary>
    IReadOnlySet<int> GetSupportedSampleRates(string deviceId, int bitDepth);

    /// <summary>
    /// Every render device Windows currently reports as active, with display names, for the
    /// Settings device picker.
    ///
    /// <para>
    /// Deliberately <b>not</b> on any polled path: reading a device's friendly name opens a Core
    /// Audio property store per device, so this scales with how many endpoints the machine has
    /// (every HDMI output, every virtual device). Callers that only need to decide which device to
    /// act on want <see cref="GetActiveRenderDeviceIds"/> instead — see its remarks.
    /// </para>
    /// </summary>
    IReadOnlyList<RenderDevice> GetActiveRenderDevices();

    /// <summary>
    /// The IDs of every render device Windows currently reports as active — the only fact
    /// <see cref="Domain.DeviceTargetResolver"/> needs, since it decides purely by comparing IDs.
    ///
    /// <para>
    /// Split out from <see cref="GetActiveRenderDevices"/> because it skips the per-device
    /// friendly-name read, which is what makes that one expensive. This is the version the poll
    /// timer's path uses: it runs every couple of seconds forever, and reading names it would only
    /// throw away pegged the UI thread at ~27% of a core indefinitely.
    /// </para>
    /// </summary>
    IReadOnlySet<string> GetActiveRenderDeviceIds();

    /// <summary>
    /// The device's display name, or null if it can't be read — for the single device a caller
    /// actually surfaces, rather than every device on the machine.
    /// </summary>
    string? GetFriendlyName(string deviceId);

    /// <summary>The Windows default render device's ID, or null if none is available right now.</summary>
    string? GetDefaultRenderDeviceId();
}
