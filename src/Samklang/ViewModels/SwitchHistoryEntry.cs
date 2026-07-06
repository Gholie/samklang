namespace Samklang.ViewModels;

/// <summary>
/// One row of the dashboard's recent-switch history: a snapshot of a Format Resolution as it was
/// applied (or corrected — see <see cref="TrackSyncCoordinator.ApplyLateResolution"/>), for
/// display only. Immutable once created, since history entries describe something that already
/// happened.
/// </summary>
public sealed record SwitchHistoryEntry(
    DateTimeOffset TimestampUtc,
    string TrackDisplay,
    string TargetFormatDisplay,
    string AudioTierDisplay,
    string ConfidenceDisplay,
    string SourceLayer)
{
    /// <summary>Local wall-clock time, formatted for the history list.</summary>
    public string TimestampDisplay => TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
}
