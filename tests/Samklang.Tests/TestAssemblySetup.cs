using System.Runtime.CompilerServices;
using Samklang.Logging;

namespace Samklang.Tests;

/// <summary>
/// Fires once when the test assembly loads — before any test runs, and before xunit fans tests
/// out across threads — to disable <see cref="AppLog"/> for the whole run. Without this,
/// CatalogFormatResolverLayerTests, FormatResolverChainTests, and TrackSyncCoordinatorTests
/// (which exercise production code that logs as a side effect) would spray synthetic entries into
/// the real user's <c>%LOCALAPPDATA%\Samklang\logs</c> file every time <c>dotnet test</c> runs.
///
/// <para>
/// A <see cref="ModuleInitializerAttribute"/> is used instead of an xunit
/// <c>ICollectionFixture</c>/assembly fixture so that every test gets the disabled state for free
/// — no test class needs to opt in, be part of a particular collection, or remember to do
/// anything. It also means there is exactly one write to <see cref="AppLog.DisabledForTests"/>,
/// before any test can possibly run in parallel, so there is no shared-state race to reason about.
/// </para>
/// </summary>
internal static class TestAssemblySetup
{
    [ModuleInitializer]
    public static void DisableAppLogging() => AppLog.DisabledForTests = true;
}
