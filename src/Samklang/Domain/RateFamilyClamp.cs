namespace Samklang.Domain;

/// <summary>
/// The pure "what do we actually switch to" policy behind capability clamping: given a requested
/// sample rate and the set of sample rates the target device's driver actually supports,
/// picks the requested rate when the device supports it outright, otherwise the highest supported
/// rate in the same rate family (44.1k vs 48k multiples) — crossing to the other family only when
/// the requested family has no supported rate at all.
///
/// No I/O and no device access — device-capability probing lives in
/// <see cref="Samklang.Devices.IAudioEndpoint"/>. Keeping this a pure data-in/data-out policy is
/// what makes it unit-testable without COM, per this issue's acceptance criteria.
/// </summary>
public static class RateFamilyClamp
{
    /// <summary>
    /// Clamps <paramref name="requested"/>'s sample rate to one the device actually supports,
    /// leaving its bit depth untouched (bit depth is always pinned to 24-bit upstream, so this
    /// policy has nothing to clamp there). Returns <paramref name="requested"/> unchanged when
    /// <paramref name="supportedSampleRatesHz"/> is empty — with nothing known about the device,
    /// there is nothing safer to fall back to.
    /// </summary>
    public static DeviceFormat Clamp(DeviceFormat requested, IReadOnlySet<int> supportedSampleRatesHz) =>
        requested with { SampleRateHz = ClampSampleRate(requested.SampleRateHz, supportedSampleRatesHz) };

    private static int ClampSampleRate(int requestedRateHz, IReadOnlySet<int> supportedRatesHz)
    {
        if (supportedRatesHz.Count == 0 || supportedRatesHz.Contains(requestedRateHz))
        {
            return requestedRateHz;
        }

        var requestedIsFortyFourFamily = requestedRateHz % 44_100 == 0;
        var sameFamilyRates = supportedRatesHz
            .Where(rate => rate % 44_100 == 0 == requestedIsFortyFourFamily)
            .ToList();

        return sameFamilyRates.Count > 0 ? sameFamilyRates.Max() : supportedRatesHz.Max();
    }
}
