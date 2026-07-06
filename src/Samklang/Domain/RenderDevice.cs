namespace Samklang.Domain;

/// <summary>An active Windows render (playback) device, as offered by the device picker in Settings.</summary>
public sealed record RenderDevice(string Id, string FriendlyName);
