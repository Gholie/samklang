namespace Samklang.Sessions;

/// <summary>
/// Serializes "latest wins" for overlapping async refreshes. SMTC fires
/// <c>MediaPropertiesChanged</c> several times per track change (Apple Music updates the title
/// first and the thumbnail a beat later), and each event starts an async refresh with multiple
/// awaits in it. Without a guard, an older refresh whose slow thumbnail read completes <em>after</em>
/// a newer refresh has already applied its results overwrites the current track's artwork with the
/// previous track's (issue #30).
///
/// <para>
/// Usage: call <see cref="Begin"/> when a refresh starts and keep the returned token. After the
/// awaits, apply the results via <see cref="TryApply"/> — it runs the apply action only if no newer
/// refresh has begun since, and runs it under a lock so a stale check-then-apply can never
/// interleave with a fresh one.
/// </para>
/// </summary>
public sealed class StaleRefreshGuard
{
    private readonly object _gate = new();
    private int _version;

    /// <summary>
    /// Marks the start of a new refresh, superseding all earlier ones. Returns a token that
    /// identifies this refresh as the newest until <see cref="Begin"/> is called again.
    /// </summary>
    public int Begin()
    {
        lock (_gate)
        {
            return ++_version;
        }
    }

    /// <summary>
    /// Runs <paramref name="apply"/> only if <paramref name="token"/> still identifies the newest
    /// refresh (no <see cref="Begin"/> since). Returns whether it ran. The action executes under
    /// the guard's lock, so a stale refresh can never apply between a newer refresh's check and
    /// its apply.
    /// </summary>
    public bool TryApply(int token, Action apply)
    {
        lock (_gate)
        {
            if (token != _version)
            {
                return false;
            }

            apply();
            return true;
        }
    }

    /// <summary>
    /// Supersedes all in-flight refreshes and applies <paramref name="apply"/> immediately —
    /// for synchronous state changes (e.g. the session disappearing) that must both take effect
    /// now and prevent any still-running refresh from resurrecting stale state afterwards.
    /// </summary>
    public void Supersede(Action apply)
    {
        lock (_gate)
        {
            _version++;
            apply();
        }
    }
}
