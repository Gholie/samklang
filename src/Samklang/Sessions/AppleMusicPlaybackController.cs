using System.Diagnostics;
using System.Windows.Automation;
using Samklang.Logging;

namespace Samklang.Sessions;

/// <summary>
/// Real <see cref="IAppleMusicPlaybackController"/>: plays/queues an album track by driving the
/// Apple Music Windows app through UI Automation. Two steps, both best-effort:
/// <list type="number">
///   <item>hand the deep-link <paramref name="navigator"/> the track so the app opens its album
///   (this is also the graceful fallback — see the interface doc); then</item>
///   <item>on a background (MTA) thread, find that album's row for the track, open its "More" menu,
///   and invoke the "Play …" / "Play Next" / "Play Last" item (<see cref="AppleMusicUiMatcher"/>
///   decides which element is which).</item>
/// </list>
///
/// <para>
/// Like <see cref="SmtcTrackWatcher"/> and <see cref="AppleMusicUriLauncher"/>, this is a thin
/// adapter over an OS surface (UIA + the shell) and is not unit-tested directly — the pure matching
/// it delegates to <see cref="AppleMusicUiMatcher"/> is. The UIA calls run under <c>Task.Run</c>
/// (thread-pool threads are MTA, which the UIA client wants) and off the WPF dispatcher, since the
/// row/menu polling blocks; every failure mode degrades to "album left open", never an exception
/// surfaced to the UI. Timeouts are generous because the app navigates asynchronously after the
/// deep link and virtualized rows realize lazily.
/// </para>
///
/// <para>
/// <paramref name="isAppControlEnabled"/> gates the intrusive step-2 UI automation behind the
/// user's opt-in (<see cref="SettingsManagement.Settings.ControlAppleMusicApp"/>). When it returns
/// false the controller does step 1 only — it navigates to the album and stops, i.e. the plain
/// deep-link behavior — so a user who hasn't opted in is never surprised by the app being driven.
/// It's read on each call (a live <c>Func&lt;bool&gt;</c>, not a captured value) so toggling the
/// setting takes effect immediately.
/// </para>
/// </summary>
public sealed class AppleMusicPlaybackController(IAppleMusicTrackLauncher navigator, Func<bool> isAppControlEnabled)
    : IAppleMusicPlaybackController
{
    private const string AppleMusicProcessName = "AppleMusic";

    /// <summary>How long to wait for the album's row to appear after the deep-link navigation fires.</summary>
    private static readonly TimeSpan RowTimeout = TimeSpan.FromSeconds(6);

    /// <summary>How long to wait for a (possibly off-screen, virtualized) row's "More" button to render.</summary>
    private static readonly TimeSpan RowMoreTimeout = TimeSpan.FromSeconds(3);

    /// <summary>How long to wait for the "More" context menu to populate after it's invoked.</summary>
    private static readonly TimeSpan MenuTimeout = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// A beat to let the deep-link navigation bring Apple Music to the foreground before we drive
    /// its UI — clicking a track in Samklang means the app is usually in the background, and opening
    /// the row menu while it's still coming forward can race and drop the menu.
    /// </summary>
    private static readonly TimeSpan ForegroundSettle = TimeSpan.FromMilliseconds(400);

    /// <summary>How many times to open the row menu and look for the item before giving up (a foreground/focus change can eat the first open).</summary>
    private const int MenuOpenAttempts = 2;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(150);

    public async Task PlayAlbumTrackAsync(AlbumTrackTarget target, QueuePlacement placement)
    {
        // Step 1: get the album on screen (and the fallback if step 2 can't complete).
        await navigator.PlayTrackAsync(target.CatalogId, target.AlbumId);

        // Step 2 is intrusive (it drives the app's UI and foregrounds it) — only do it when the
        // user has opted in. Without the opt-in we stop at step 1: the album is open for the user to
        // press play themselves, exactly the pre-feature behavior.
        if (!isAppControlEnabled())
        {
            return;
        }

        // Step 2: drive the app's UI to actually play/queue the exact row.
        try
        {
            await Task.Run(() => DriveAlbumRow(target, placement));
        }
        catch (Exception ex)
        {
            // The album is still open from step 1, so this is a soft failure — log and move on.
            AppLog.Warn(
                $"UI-automation play/queue failed for '{target.Title}' ({placement}): {ex.GetType().Name}: {ex.Message}",
                category: "MediaTransport");
        }
    }

    private static void DriveAlbumRow(AlbumTrackTarget target, QueuePlacement placement)
    {
        // Let the navigation foreground the app and settle before poking its UI.
        Thread.Sleep(ForegroundSettle);

        var (window, row) = WaitForRow(target.TrackNumber, target.Title);
        if (window is null || row is null)
        {
            AppLog.Warn($"UI-automation: album row not found for Track {target.TrackNumber} '{target.Title}'.", category: "MediaTransport");
            return;
        }

        var moreButton = WaitForRowMoreButton(window, target.TrackNumber, target.Title, row);
        if (moreButton is null)
        {
            AppLog.Warn($"UI-automation: row 'More' button not found for '{target.Title}'.", category: "MediaTransport");
            return;
        }

        // Opening the menu can be lost to a focus/foreground change the first time; retry the
        // open-and-pick before giving up. Each miss dismisses the (possibly-open) menu first.
        for (var attempt = 1; attempt <= MenuOpenAttempts; attempt++)
        {
            if (!TryInvoke(moreButton))
            {
                break;
            }

            var menuItem = WaitForMenuItem(window.Current.ProcessId, placement, target.Title);
            if (menuItem is not null)
            {
                if (!TryInvoke(menuItem))
                {
                    DismissMenu();
                }

                return;
            }

            DismissMenu();
        }

        AppLog.Warn($"UI-automation: '{placement}' menu item not found for '{target.Title}'.", category: "MediaTransport");
    }

    /// <summary>
    /// Polls for the row's own "More" button, re-finding and re-realizing the row each pass. A
    /// virtualized off-screen row (e.g. a track far down a long album) only materializes its
    /// per-row controls once it has scrolled into view and laid out, which takes a few frames after
    /// <see cref="Realize"/> asks for it — so a single lookup races the render and misses the
    /// button; polling lets the button appear.
    /// </summary>
    private static AutomationElement? WaitForRowMoreButton(AutomationElement window, int trackNumber, string title, AutomationElement initialRow)
    {
        var moreCondition = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.NameProperty, "More"));
        var deadline = DateTime.UtcNow + RowMoreTimeout;
        while (true)
        {
            // The row's container is re-created when a virtualized item realizes, invalidating the
            // handle; re-find it each pass so the lookup runs against the live element.
            var row = FindRow(window, trackNumber, title) ?? initialRow;
            Realize(row);
            var more = row.FindFirst(TreeScope.Descendants, moreCondition);
            if (more is not null)
            {
                return more;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            Thread.Sleep(PollInterval);
        }
    }

    /// <summary>Polls until the target album row is present (the app navigates asynchronously after the deep link).</summary>
    private static (AutomationElement? Window, AutomationElement? Row) WaitForRow(int trackNumber, string title)
    {
        var deadline = DateTime.UtcNow + RowTimeout;
        while (true)
        {
            var window = FindAppleMusicWindow();
            var row = window is null ? null : FindRow(window, trackNumber, title);
            if (row is not null)
            {
                return (window, row);
            }

            if (DateTime.UtcNow >= deadline)
            {
                return (window, null);
            }

            Thread.Sleep(PollInterval);
        }
    }

    private static AutomationElement? FindAppleMusicWindow()
    {
        foreach (var process in Process.GetProcessesByName(AppleMusicProcessName))
        {
            try
            {
                var window = AutomationElement.RootElement.FindFirst(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));
                if (window is not null)
                {
                    return window;
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }

    /// <summary>
    /// Picks the album row for the track: preferring the exact <c>Track {n} {title}</c> match, and
    /// falling back to a title-only match only when it's unambiguous (exactly one row mentions it),
    /// so a track-number discrepancy still resolves without risking the wrong row.
    /// </summary>
    private static AutomationElement? FindRow(AutomationElement window, int trackNumber, string title)
    {
        var rows = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));

        AutomationElement? titleOnlyMatch = null;
        var titleOnlyCount = 0;
        foreach (AutomationElement row in rows)
        {
            string name;
            try
            {
                name = row.Current.Name;
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            if (AppleMusicUiMatcher.IsTrackRow(name, trackNumber, title))
            {
                return row;
            }

            if (AppleMusicUiMatcher.RowMentionsTitle(name, title))
            {
                titleOnlyMatch = row;
                titleOnlyCount++;
            }
        }

        return titleOnlyCount == 1 ? titleOnlyMatch : null;
    }

    /// <summary>
    /// Polls the just-opened context menu (a process-owned popup) for the item matching the
    /// placement. Deliberately searches only Apple Music's own top-level windows — the popup is one
    /// of them — rather than the whole desktop: a <c>RootElement.FindAll(Descendants, …)</c> over
    /// every window can stall for many seconds when a heavy app (a browser's huge accessibility
    /// tree) is open, which would freeze the play/queue action.
    /// </summary>
    private static AutomationElement? WaitForMenuItem(int processId, QueuePlacement placement, string title)
    {
        var deadline = DateTime.UtcNow + MenuTimeout;
        var processWindows = new PropertyCondition(AutomationElement.ProcessIdProperty, processId);
        var menuItems = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem);
        while (true)
        {
            // Children scope on the desktop is cheap (top-level windows only); the pid filter keeps
            // it to Apple Music's windows, one of which hosts the open context menu.
            foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, processWindows))
            {
                foreach (AutomationElement item in window.FindAll(TreeScope.Descendants, menuItems))
                {
                    try
                    {
                        if (AppleMusicUiMatcher.IsMenuItem(item.Current.Name, placement, title))
                        {
                            return item;
                        }
                    }
                    catch (ElementNotAvailableException)
                    {
                    }
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            Thread.Sleep(PollInterval);
        }
    }

    private static void Realize(AutomationElement row)
    {
        try
        {
            if (row.TryGetCurrentPattern(VirtualizedItemPattern.Pattern, out var virtualized))
            {
                ((VirtualizedItemPattern)virtualized).Realize();
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
        {
        }

        try
        {
            if (row.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var scroll))
            {
                ((ScrollItemPattern)scroll).ScrollIntoView();
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
        {
        }

        // Give the realized/scrolled row a beat to lay out before its "More" button is queried.
        Thread.Sleep(PollInterval);
    }

    private static bool TryInvoke(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            {
                ((InvokePattern)pattern).Invoke();
                return true;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
        {
        }

        return false;
    }

    /// <summary>
    /// Best-effort close of a context menu we opened but couldn't act on, so it doesn't linger.
    /// Uses <c>keybd_event</c> rather than <c>SendKeys</c> because this runs on a thread-pool thread
    /// with no message pump, where <c>SendKeys.SendWait</c> refuses to run.
    /// </summary>
    private static void DismissMenu()
    {
        try
        {
            keybd_event(VkEscape, 0, 0, UIntPtr.Zero);
            keybd_event(VkEscape, 0, KeyUp, UIntPtr.Zero);
        }
        catch
        {
            // Leaving the menu open is harmless — the user's next click closes it.
        }
    }

    private const byte VkEscape = 0x1B;
    private const uint KeyUp = 0x0002;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
