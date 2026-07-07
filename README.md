# Samklang

[![CI](https://github.com/Gholie/samklang/actions/workflows/ci.yml/badge.svg)](https://github.com/Gholie/samklang/actions/workflows/ci.yml)

**Bit-perfect Apple Music playback on Windows.** *Samklang* (Norwegian: consonance — literally "together-sound") is a Windows 11 tray utility that switches your audio device's format (sample rate + bit depth) to match the track currently playing in **Apple Music for Windows** — so lossless and hi-res tracks play without resampling.

> **Status: approaching first public release.** v0.1.0 lands as soon as the first release-please PR is merged — see [docs/PLAN.md](docs/PLAN.md) for the roadmap and [releases](https://github.com/Gholie/samklang/releases) for downloads once it's there.

## Why

Windows resamples every app's audio to the device's shared-mode format (the "Default Format" in the Sound control panel). If that's set to 48 kHz and Apple Music plays a 96 kHz hi-res track, you're not hearing hi-res — you're hearing a resample. This tool watches what's playing and retunes the device so track and device agree.

## Install

1. Grab the latest installer from the [Releases page](https://github.com/Gholie/samklang/releases) — download the `*-Setup.exe` asset (`Samklang-win-Setup.exe`) from the newest release. Every release's artifacts carry a [build provenance attestation](https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations) you can verify with `gh attestation verify Samklang-win-Setup.exe --repo Gholie/samklang`.
2. Run it. There's no install wizard to click through — it installs to your user profile (no admin prompt) and launches Samklang automatically when it's done.
3. Windows SmartScreen may warn that this is from an "unknown publisher" the first time, since the installer isn't code-signed yet — click **More info → Run anyway** to proceed.

### First run

- Samklang starts minimized to the **system tray** (the icon area near the clock) — there's no window that pops up on first launch. Look for its icon there; hover over it to see a tooltip with the current version, track, and applied format.
- Left-click (or double-click) the tray icon to open the dashboard window, which shows the current track, its resolved format, confidence, and recent switch history, plus a Settings tab for device targeting, tier mappings, resting format, grace period, and "Start with Windows."
- Nothing needs configuring to get going: open Apple Music, play something, and Samklang picks up the track change and switches your default audio device's format automatically. Use Settings only if you want to pin a specific device, tweak tier→sample-rate mappings, or change the resting format/grace period.

### What to expect

- **Tray icon, always running.** Closing the dashboard window (the X button) just hides it back to the tray — Samklang keeps watching in the background. Use the tray menu's **Exit** to actually quit.
- **A brief silence on format changes.** Switching a device's format live causes a short mute/rebuild hiccup — this is inherent to how Windows shared-mode audio works, not a bug.
- **Automatic updates.** Samklang checks GitHub Releases for a newer version on every startup (and via the tray menu's **Check for Updates**) and applies it automatically in the background — you don't need to manually redownload installers for future releases.
- **Settings live in `%APPDATA%`** as JSON (device targeting, tier mappings, resting format, grace period) — delete that file to reset to defaults.

## How it works

1. **Detect** — the Windows media session API (SMTC) reports track changes from the Apple Music app (and only that app; other players are ignored).
2. **Resolve** — a layered resolver decides the target format, each layer more approximate than the last:
   - **Catalog match**: look the track up in Apple's web catalog and read the exact sample rate from its enhanced-HLS manifest (*Exact* confidence);
   - **PlayCache analysis**: probe the audio files the Apple Music app caches locally (*Exact* confidence, but heuristic matching);
   - **Tier fallback**: map the track's audio tier (lossless / hi-res-lossless / …) to a user-configured rate (*Tier-derived* confidence).
   Bit depth is always pinned to 24-bit — 16-bit content pads into it losslessly.
3. **Switch** — the device's shared-mode format is changed live via Core Audio policy configuration, clamped to the rates the device actually supports. You can follow the Windows default device or pin a specific one.
4. **Rest** — when playback has been idle past a grace period, the device reverts to your configured *resting format*, so games and movies aren't left at 192 kHz.

A status window shows the current track, its resolved rate, the confidence behind that resolution, and the device's actual format — plus settings for device, tier mappings, resting format, and autostart.

The project's vocabulary is defined in [CONTEXT.md](CONTEXT.md); load-bearing decisions are recorded in [docs/adr/](docs/adr/).

## Requirements

- Windows 11 (Windows 10 build 19041+ likely works, untested)
- [Apple Music for Windows](https://apps.microsoft.com/detail/9PFHDD62MXS1) with an Apple Music subscription
- To build from source: .NET 8 SDK

## Building from source

Only needed if you're developing Samklang, not for normal use — see [Install](#install) above if you just want to run it.

```powershell
dotnet build
dotnet test
```

### Releasing

Releases are cut by [release-please](https://github.com/googleapis/release-please), not by hand: Conventional Commits on `main` accumulate into an automatically maintained release PR (semver bump + changelog). Merging that PR creates a draft GitHub Release; the same workflow then builds the Velopack installer, attaches a build provenance attestation, uploads the artifacts, and publishes the release — at which point installed copies pick the update up automatically.

## Prior art

This project stands on ideas proven by [LosslessSwitcher](https://github.com/vincentneo/LosslessSwitcher) (macOS) and [WindowsLosslessSwitcher](https://github.com/jordanmgibson/WindowsLosslessSwitcher) (Windows, GPL-3.0), independently re-imagined with a full status dashboard.

## License

[GPL-3.0-or-later](LICENSE).
