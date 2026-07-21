# Samklang

A Windows 11 tray utility that switches an audio output device's format (sample rate, bit depth) to match the track currently playing in the Apple Music Windows app, avoiding resampling for lossless playback.

## Language

**Track**:
The song currently playing in Apple Music, identified by the metadata the Windows media session exposes (title, artist, album).
_Avoid_: Song (when referring to the playing item), media item

**Device Format**:
The (sample rate, bit depth) pair a Windows audio device operates at in shared mode — what the Sound control panel calls "Default Format".
_Avoid_: Bitrate, quality, Hz setting

**Sample Rate**:
Samples per second of the device or track, in Hz (44100, 48000, 88200, 96000, 176400, 192000).

**Bit Depth**:
Bits per sample (16 or 24).
_Avoid_: Bitrate

**Audio Tier**:
Apple's quality classification of a track, from the catalog `audioVariants` attribute: lossy-stereo, lossless (≤48 kHz), hi-res-lossless (88.2–192 kHz), dolby-atmos. A tier bounds the sample rate but does not determine it.
_Avoid_: Quality, variant

**Format Resolution**:
The process of deciding the target Device Format for a Track. Performed by the Layered Resolver; every resolution carries a Confidence.

**Layered Resolver**:
The chain that performs Format Resolution: catalog match → local cache analysis → tier fallback. Each layer is tried in order until one produces an answer.

**Confidence**:
How certain a Format Resolution is: Exact (true rate known), Tier-derived (only the Audio Tier is known; rate comes from user's per-tier mapping), Fallback (nothing known; default applies).

**Target Format**:
The Device Format a Format Resolution decides the device should switch to. Bit depth is pinned to 24-bit when applying: a 16-bit source still targets 24-bit (bit-perfect), and a bit-depth-only difference never triggers a switch — only the sample rate does.

**Next Track Buffer**:
The catalog layer's one-slot buffer holding the predicted next Track's already-resolved Target Format. SMTC exposes no play queue, so the prediction is album order: after each successful catalog match, the matched song's album track list is fetched and the following track's format is resolved in the background, making the switch at the track boundary instant when the prediction holds.
_Avoid_: Prefetch cache, queue lookahead

**Resting Format**:
The user-configured Device Format the tool reverts to once playback has been idle for the Grace Period. Seeded from the device's format when the tool first runs.
_Avoid_: Default format (ambiguous with Windows' own term)

**Grace Period**:
How long playback must be idle (paused, stopped, or Apple Music closed) before the tool reverts to the Resting Format.
_Avoid_: Timeout, idle delay

## Conventions

**PR titles**: Conventional-commit style — `type: description` or `type(scope): description` (`feat`, `fix`, `chore`, `docs`, ...). Add a scope when the PR is confined to one area (e.g. `feat(playback)`, `fix(settings)`); omit it when the change spans multiple areas.
