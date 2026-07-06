# Anonymous web-player token for catalog access

The catalog layer of the resolver needs exact per-track sample rates, which the *documented* Apple Music API does not expose at any price — its `audioVariants` attribute only gives a quality tier. We therefore authenticate with the anonymous developer token embedded in the music.apple.com web player's JS bundle, call `amp-api.music.apple.com` with `extend=extendedAssetUrls`, and parse the track's enhanced-HLS manifest, whose ALAC variant entries carry literal `SAMPLE-RATE` and bit-depth attributes.

## Considered options

- **Official API with a user-supplied MusicKit key** — requires every user to hold a $99/yr Apple Developer Program membership, and still only guarantees tier-level data. Rejected: setup burden without the data we need.
- **No catalog layer (local PlayCache analysis only)** — ToS-clean and offline, but the cache only materializes after a track starts streaming, so first plays always switch late or not at all. Rejected as the *primary* source; kept as layer 2.
- **Anonymous web token (chosen)** — zero user setup, exact rates, proven for years by LosslessSwitcher-family tools. Unofficial and revocable by Apple at any time.

## Consequences

- Apple can break token scraping or `amp-api` access without notice; the resolver must degrade gracefully to PlayCache analysis and tier fallback, never hard-fail.
- Access is read-only public catalog metadata — the same requests the logged-out web player makes — but it remains ToS-gray, and the README should not oversell stability of Exact-confidence resolution.
