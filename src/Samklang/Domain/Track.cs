namespace Samklang.Domain;

/// <summary>
/// The song currently playing in Apple Music, identified by the metadata
/// the Windows media session exposes.
/// </summary>
public sealed record Track(string Title, string Artist, string Album);
