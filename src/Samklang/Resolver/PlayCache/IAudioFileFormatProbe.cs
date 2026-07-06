namespace Samklang.Resolver.PlayCache;

/// <summary>
/// Stream properties read straight out of an audio file's container header — never decoded from
/// the audio itself. <see cref="BitDepth"/> is null when the container doesn't expose one (lossy
/// codecs such as AAC/MP3 carry no PCM bit depth); callers fall back to the app's pinned bit depth
/// in that case (see <see cref="FallbackFormatResolverLayer.PinnedBitDepth"/>).
/// </summary>
public sealed record AudioFileFormat(int SampleRateHz, int? BitDepth);

/// <summary>Extracts <see cref="AudioFileFormat"/> from a file on disk by reading its container header.</summary>
public interface IAudioFileFormatProbe
{
    /// <summary>
    /// Probes the file, returning null when the format is unsupported, unrecognized, or the
    /// container structure is corrupt/unexpected. Throws <see cref="System.IO.IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when the file cannot be opened at all (e.g. held
    /// exclusively by another process), so callers can distinguish "try a different candidate or
    /// retry later" from "this file just isn't a usable audio file."
    /// </summary>
    AudioFileFormat? Probe(string filePath);
}
