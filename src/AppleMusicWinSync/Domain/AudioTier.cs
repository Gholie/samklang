namespace AppleMusicWinSync.Domain;

/// <summary>
/// Apple's quality classification of a track (catalog <c>audioVariants</c>).
/// A tier bounds the sample rate but does not determine it.
/// </summary>
public enum AudioTier
{
    Unknown,
    LossyStereo,
    Lossless,       // ≤ 48 kHz
    HiResLossless,  // 88.2–192 kHz
    DolbyAtmos,
}
