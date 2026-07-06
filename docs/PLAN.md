# Plan

Decisions below were settled in the design interview of 2026-07-06. Vocabulary: [CONTEXT.md](../CONTEXT.md). Catalog-auth rationale: [ADR-0001](adr/0001-anonymous-web-token-for-catalog-access.md).

## Settled decisions

| Decision | Choice |
|---|---|
| Relationship to prior art | Build fresh; mine WindowsLosslessSwitcher (GPL-3.0) for ideas and, where right, code |
| Language / runtime | C# / .NET 8, `net8.0-windows10.0.19041.0` |
| GUI | WPF + WPF-UI (Fluent); tray icon always, one window = status dashboard + settings |
| Sample-rate resolution | Layered Resolver: catalog match → PlayCache analysis → tier fallback |
| Bit depth | Pinned to 24-bit |
| Catalog auth | Anonymous web-player token (ADR-0001) |
| Session filtering | Apple Music package identity only |
| Unsupported rates | Clamp to device's best rate in the same rate family (44.1k vs 48k multiples) |
| Idle behavior | Revert to user-configured Resting Format after Grace Period (default ~30 s; resting format seeded from device's original setting) |
| Distribution | Public from day one: GitHub releases, Velopack installer + auto-update, GitHub Actions CI |
| License | GPL-3.0-or-later |

## Milestones

### M1 — Working core (tier-based)
A usable product with no catalog calls at all.
- SMTC watcher: track-change events, filtered to Apple Music's package identity
- Tier fallback resolver (catalog `audioVariants` unavailable at this layer, so tier comes from user mapping defaults + track heuristics; Confidence = Tier-derived/Fallback)
- Device switching: `IPolicyConfig` interop, device-capability probing, rate-family clamping, mute-around-switch
- Device targeting: follow default / pinned device (device picker in settings)
- Resting Format + Grace Period revert logic
- Tray icon with status tooltip; minimal window (current track, target format, device format)
- Settings persistence (JSON in `%APPDATA%`), start-with-Windows toggle
- NuGet: WPF-UI, NAudio, H.NotifyIcon.Wpf

### M2 — Catalog layer (Exact confidence)
- Anonymous token scrape from music.apple.com JS bundle, with expiry-aware caching
- Storefront auto-detect from Windows region, override in settings
- `amp-api` song search + `extend=extendedAssetUrls`; enhanced-HLS manifest parsing for SAMPLE-RATE/bit depth
- Confidence surfaced in the UI; graceful degradation when the token or API breaks

### M3 — PlayCache layer
- Probe `%LOCALAPPDATA%\Packages\AppleInc.AppleMusicWin_…\LocalCache\Local\Apple\AMPLibraryAgent\PlayCache`
- File↔track matching heuristics: file locked by Apple Music, write freshness, cloud-id via preferences plist
- Acts as tie-breaker/fallback when catalog is unavailable or ambiguous

### M4 — Public release
- Dashboard polish: tier badge, confidence indicator, switch history
- Velopack packaging + auto-update; GitHub Actions build/test/release pipeline
- README for end users, first tagged release

## Known risks

- **Token/amp-api breakage** — unofficial surface; mitigated by resolver layering (ADR-0001).
- **PlayCache fragility** — undocumented app internals; heuristics may silently rot with Apple Music app updates.
- **Switch hiccup** — a track that needs a format change starts with a brief mute/rebuild silence; inherent to live shared-mode format changes.
- **Incumbent** — WindowsLosslessSwitcher already serves this niche; our differentiators are the status dashboard and UX.
