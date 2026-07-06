# Samklang

[![CI](https://github.com/Gholie/samklang/actions/workflows/ci.yml/badge.svg)](https://github.com/Gholie/samklang/actions/workflows/ci.yml)

**Bit-perfect Apple Music playback on Windows.** *Samklang* (Norwegian: consonance — literally "together-sound") is a Windows 11 tray utility that switches your audio device's format (sample rate + bit depth) to match the track currently playing in **Apple Music for Windows** — so lossless and hi-res tracks play without resampling.

> **Status: pre-alpha.** Repository scaffold and domain model; no releasable build yet. See [docs/PLAN.md](docs/PLAN.md) for the roadmap.

## Why

Windows resamples every app's audio to the device's shared-mode format (the "Default Format" in the Sound control panel). If that's set to 48 kHz and Apple Music plays a 96 kHz hi-res track, you're not hearing hi-res — you're hearing a resample. This tool watches what's playing and retunes the device so track and device agree.

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
- To build: .NET 8 SDK

## Building

```powershell
dotnet build
dotnet test
```

## Prior art

This project stands on ideas proven by [LosslessSwitcher](https://github.com/vincentneo/LosslessSwitcher) (macOS) and [WindowsLosslessSwitcher](https://github.com/jordanmgibson/WindowsLosslessSwitcher) (Windows, GPL-3.0), independently re-imagined with a full status dashboard.

## License

[GPL-3.0-or-later](LICENSE).
