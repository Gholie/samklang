namespace Samklang.Sessions;

/// <summary>
/// The pure string-matching heart of <see cref="AppleMusicPlaybackController"/>'s UI-automation:
/// deciding which UI-Automation element is the album row we want and which context-menu item is the
/// action we want. Split out from the raw UIA calls so this — the part most likely to need tuning
/// as Apple tweaks the app — is unit-tested, while the element-walking adapter around it stays a
/// thin untested shell (the same division <see cref="SmtcTrackWatcher"/> uses).
///
/// <para>
/// The strings this matches against are the Apple Music album view's own accessibility names,
/// captured live from the shipping Windows app: a track row is named like
/// <c>"Track 9 Ticket to Ride 3 minutes, 10 seconds"</c>, and the per-row "More" menu lists
/// <c>Play "&lt;title&gt;"</c>, <c>Play Next</c>, and <c>Play Last</c>. The "Play now" item echoes
/// the song title, which lets it be matched language-independently; "Play Next"/"Play Last" carry
/// no such anchor, so those are matched by their (English) labels — a documented limitation.
/// </para>
/// </summary>
public static class AppleMusicUiMatcher
{
    /// <summary>
    /// True when <paramref name="rowName"/> is the album row for the given track — it both spells
    /// out the <paramref name="title"/> and carries the <c>Track {number}</c> marker, so a reprise
    /// or same-titled track elsewhere on the album doesn't collide.
    /// </summary>
    public static bool IsTrackRow(string? rowName, int trackNumber, string title) =>
        RowMentionsTitle(rowName, title)
        && rowName!.Contains($"Track {trackNumber} ", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when <paramref name="rowName"/> merely mentions the <paramref name="title"/> — the
    /// fallback the adapter uses only when exactly one row matches, so a track-number mismatch
    /// (e.g. Apple numbering a multi-disc album differently than our flat list) still resolves when
    /// the title alone is unambiguous.
    /// </summary>
    public static bool RowMentionsTitle(string? rowName, string title) =>
        !string.IsNullOrWhiteSpace(rowName)
        && !string.IsNullOrWhiteSpace(title)
        && rowName.Contains(title, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the context-menu item named <paramref name="menuItemName"/> is the action for the
    /// requested <paramref name="placement"/>. <see cref="QueuePlacement.PlayNow"/> matches the
    /// title-echoing "Play “&lt;title&gt;”" item (guarded by a leading "Play" so it can't match an
    /// unrelated entry that happens to contain the title); the two queue placements match their
    /// exact labels.
    /// </summary>
    public static bool IsMenuItem(string? menuItemName, QueuePlacement placement, string title) => placement switch
    {
        QueuePlacement.PlayNext => string.Equals(menuItemName, "Play Next", StringComparison.OrdinalIgnoreCase),
        QueuePlacement.PlayLast => string.Equals(menuItemName, "Play Last", StringComparison.OrdinalIgnoreCase),
        QueuePlacement.PlayNow => !string.IsNullOrWhiteSpace(menuItemName)
            && menuItemName.StartsWith("Play", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(title)
            && menuItemName.Contains(title, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}
