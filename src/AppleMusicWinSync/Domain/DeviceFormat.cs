namespace AppleMusicWinSync.Domain;

/// <summary>
/// The (sample rate, bit depth) pair a Windows audio device operates at in shared mode —
/// what the Sound control panel calls "Default Format".
/// </summary>
public readonly record struct DeviceFormat(int SampleRateHz, int BitDepth)
{
    public override string ToString() =>
        $"{BitDepth}-bit/{SampleRateHz / 1000.0:0.#} kHz";

    /// <summary>True when both rates sit in the same integer-multiple family (44.1k vs 48k multiples).</summary>
    public bool SharesRateFamilyWith(DeviceFormat other) =>
        SampleRateHz % 44100 == 0 == (other.SampleRateHz % 44100 == 0);
}
