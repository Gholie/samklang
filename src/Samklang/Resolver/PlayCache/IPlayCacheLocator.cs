namespace Samklang.Resolver.PlayCache;

/// <summary>Supplies the Apple Music package's PlayCache directory path, or null if it can't be located.</summary>
public interface IPlayCacheLocator
{
    string? GetPlayCacheDirectory();
}
