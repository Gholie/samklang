namespace Samklang.Resolver.Catalog;

/// <summary>
/// The anonymous web-player developer token used to authenticate <c>amp-api.music.apple.com</c>
/// calls (see docs/adr/0001), plus the expiry decoded from its JWT <c>exp</c> claim so callers can
/// tell when it needs refreshing without making a failing request first.
/// </summary>
public sealed record AppleMusicToken(string Value, DateTimeOffset ExpiresAtUtc)
{
    /// <summary>
    /// True when there is more than <paramref name="refreshBuffer"/> of life left on the token at
    /// <paramref name="now"/> — i.e. it's safe to keep using without refreshing first.
    /// </summary>
    public bool IsFreshAt(DateTimeOffset now, TimeSpan refreshBuffer) => now + refreshBuffer < ExpiresAtUtc;
}
