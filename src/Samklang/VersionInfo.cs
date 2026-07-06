using System.Reflection;

namespace Samklang;

/// <summary>
/// Formats the app's version for display in the tray/UI (issue #10's "version visible" acceptance
/// criterion). The csproj's <c>&lt;Version&gt;</c> is the single source of truth — it's what the
/// release workflow overrides via <c>dotnet publish -p:Version=...</c> to match a pushed tag, and
/// what <c>vpk pack --packVersion</c> should be given to match, so the running app's displayed
/// version, its assembly version, and the Velopack package it came from all agree.
/// </summary>
public static class VersionInfo
{
    /// <summary>The running app's version, formatted for display (e.g. <c>"v0.1.0"</c>).</summary>
    public static string CurrentDisplay { get; } = Format(Assembly.GetExecutingAssembly().GetName().Version);

    /// <summary>
    /// Formats a <see cref="Version"/> as <c>"vMAJOR.MINOR.PATCH"</c>. .NET's default assembly
    /// versioning always populates all four <see cref="Version"/> fields (defaulting unset ones to
    /// 0), so this drops the fourth (Revision) field — end users expect to see the three-part
    /// version they'd type as a release tag, not a build-tooling implementation detail.
    /// </summary>
    public static string Format(Version? version)
    {
        if (version is null)
        {
            return "v0.0.0";
        }

        return $"v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }
}
