# Handoff: fix catalog-layer token scraping (the real remaining cause of issue #31)

Written 2026-07-07 at session end; root cause was confirmed live minutes earlier. This is the
follow-up promised in PR #36's diagnosis and unblocks the "always Fallback confidence" half of
issue #31. (The "device never switches" half was a separate bug, fixed in PR #37.)

## Root cause — CONFIRMED, and smaller than feared

The anonymous developer token is **still embedded in the web player's index bundle**. It was
never relocated. What changed is the token's JWT header:

- Old header (what the code expects): `{"alg":"ES256","kid":"…"}` — base64url starts with
  `eyJhbGciOiJFUzI1NiIsImtpZCI6Ij`
- Current header (observed live 2026-07-07): `{"typ":"JWT","alg":"ES256","kid":"WebPlayKid"}` —
  base64url starts with `eyJ0eXAiOiJKV1QiLCJhbGciOiJFUzI1NiIs…`

`JwtLiteralRegex()` in `src/Samklang/Resolver/Catalog/HttpAppleMusicCatalogClient.cs` hardcodes
the old prefix, so `FetchTokenAsync` throws "Could not locate an embedded developer token", and
`CatalogFormatResolverLayer` self-disables for the session. Everything downstream (search,
manifest, `SAMPLE-RATE` parsing) is presumed fine — it never runs.

Live observations (music.apple.com fetched with a browser User-Agent — plain curl with default UA
returns an **empty 200**, see gotcha below):

- Bundle: `/assets/index~299c09aac6.js` (~3.3 MB). Note the `~` separator — the existing
  `IndexBundleScriptRegex` (`assets/index[^"]*\.js`) still matches, but the page also references
  `/assets/index-legacy~….js`, which that regex matches too. Whichever appears first in the HTML
  wins today; make discovery prefer the non-legacy bundle explicitly.
- In the bundle: `…configure({developerToken:qc…` where
  `qc="eyJ0eXAiOiJKV1QiLCJhbGciOiJFUzI1NiIsImtpZCI6IldlYlBsYXlLaWQifQ.eyJpc3MiOiJBTVBXZWJQbGF5IiwiaWF0IjoxNzgxNjY1NDYwLCJleHAi…"`
  — payload begins `{"iss":"AMPWebPlay","iat":1781665460,"exp":…}`. Minified variable names
  (`qc`) churn per build; never anchor on them.

## The fix

In `HttpAppleMusicCatalogClient`:

1. Replace the hardcoded-prefix regex with a generic JWT-literal match,
   `eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+`, then **validate by decoding** instead of
   by prefix: accept the first candidate whose header has `"alg":"ES256"` and whose payload has
   `iss == "AMPWebPlay"` (or at minimum a plausible `exp`). Base64url decoding helpers already
   exist in the class (`Base64UrlDecode`, `DecodeJwtExpiry`).
2. Make `IndexBundleScriptRegex` skip `index-legacy` (e.g. exclude `-legacy` after `index`, or
   collect all matches and prefer the non-legacy one).
3. Resilience (optional but cheap): if the index bundle yields no valid token, scan the other
   same-origin `/assets/*.js` chunk URLs referenced by the bundle (~26 of them today) before
   giving up. Keeps the layer alive through the next repackaging.

Testability: token extraction currently lives inside the deliberately-untested network adapter.
Extract a pure `TokenExtractor` (bundle string in → validated token out) and unit-test it with
fixtures for the old header, the new header, and near-miss garbage (`eyJ…` literals that aren't
ES256 tokens — the bundle contains other base64 blobs). Follow the repo pattern of testing pure
logic against fakes (see how `EnhancedHlsManifestParser` / `CatalogTrackMatcher` are tested).

## Gotchas learned live

- **User-Agent matters**: `https://music.apple.com` returns an empty 200 body to curl's default
  UA. The app's `HttpClient` — check what UA it sends (see where `HttpAppleMusicCatalogClient`'s
  `HttpClient` is constructed) — may need a browser-like UA header set for the origin fetch, or
  the index HTML comes back empty and bundle discovery fails before the regex is even tried.
  Verify whether this affects the app path, not just curl.
- `/us/browse` returns the full page; the bare origin may redirect — use `-L`/follow-redirects
  semantics when reproducing manually.
- Do not scrape musickit.js for the token — it isn't there (checked).

## Verification plan (end-to-end, on this box)

1. Unit tests for the extractor incl. a fixture with the real new header prefix.
2. Live: run Samklang, play a lossless Track (repro case: "The Dark Forest" by Muse, 24-bit/48 kHz
   ALAC — currently resolves Fallback 44.1 kHz). Expect: Confidence **Exact**, source layer
   catalog, Target Format 48 kHz, and — with PR #37 merged — an actual Device Format switch
   44.1 → 48 kHz on the FiiO K11 (probe now reports 44.1–192 kHz supported).
3. Confirm graceful degradation still holds by feeding the extractor a bundle with no token
   (layer must disable for the session, not crash) — existing behavior, keep it.

## Session state this hands off from (2026-07-07)

- Open PRs: #33 (artwork race, fixes #30), #34 (release-please tagging, fixes #32), #36
  (PlayCache encrypted-ALAC probe, refs #31), #37 (device rate probe/set via exclusive-mode +
  format-clone, refs #31 — the fix that makes switching work at all).
- Bogus release PR #29 must be closed manually, without merging (see #34's body).
- Issue #31 stays open until THIS fix lands; it is the last piece.
- `docs/HANDOFF_EXCLUSIVE_MODE_TOGGLE.md` (uncommitted, same session) is unrelated future work.
- Scratch artifacts (downloaded bundle, probe apps) live in the session scratchpad; nothing in
  the repo depends on them.
