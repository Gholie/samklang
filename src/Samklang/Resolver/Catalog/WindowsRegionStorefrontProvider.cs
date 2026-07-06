using System.Globalization;

namespace Samklang.Resolver.Catalog;

/// <summary>
/// Auto-detects the Apple Music storefront from the Windows region, overridable via
/// <see cref="SettingsManagement.Settings.StorefrontOverride"/>. The override always wins when
/// present; otherwise the storefront is the current Windows region's two-letter code lower-cased
/// (Apple's storefront ids match ISO 3166-1 alpha-2 for the vast majority of regions), falling
/// back to <see cref="DefaultStorefront"/> if the region can't be read for any reason.
/// </summary>
public sealed class WindowsRegionStorefrontProvider : IStorefrontProvider
{
    public const string DefaultStorefront = "us";

    private readonly Func<string?> _storefrontOverrideAccessor;
    private readonly Func<string> _currentRegionCodeAccessor;

    /// <param name="storefrontOverrideAccessor">Reads the current settings override, e.g. <c>() => settingsManager.Current.StorefrontOverride</c>.</param>
    /// <param name="currentRegionCodeAccessor">
    /// Reads the current Windows region's two-letter code. Defaults to
    /// <see cref="RegionInfo.CurrentRegion"/>; overridable for tests so this class never depends
    /// on the real OS region in unit tests.
    /// </param>
    public WindowsRegionStorefrontProvider(
        Func<string?> storefrontOverrideAccessor,
        Func<string>? currentRegionCodeAccessor = null)
    {
        _storefrontOverrideAccessor = storefrontOverrideAccessor;
        _currentRegionCodeAccessor = currentRegionCodeAccessor ?? (() => RegionInfo.CurrentRegion.TwoLetterISORegionName);
    }

    public string GetStorefront()
    {
        var overrideValue = _storefrontOverrideAccessor();
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            return overrideValue.Trim().ToLowerInvariant();
        }

        try
        {
            var regionCode = _currentRegionCodeAccessor();
            return string.IsNullOrWhiteSpace(regionCode) ? DefaultStorefront : regionCode.ToLowerInvariant();
        }
        catch
        {
            // Region lookup can fail in unusual environments (invariant globalization mode,
            // no regional settings configured); the catalog layer should still get a usable
            // storefront rather than throwing out of the resolver chain over this.
            return DefaultStorefront;
        }
    }
}
