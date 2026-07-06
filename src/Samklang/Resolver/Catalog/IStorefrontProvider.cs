namespace Samklang.Resolver.Catalog;

/// <summary>Supplies the two-letter Apple Music storefront code (e.g. "us", "gb", "no") to query.</summary>
public interface IStorefrontProvider
{
    string GetStorefront();
}
