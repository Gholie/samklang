# Handoff: real-environment integration testing on Windows

## Why this exists

Samklang has been developed and code-reviewed from a WSL/Linux session that has no
access to a real Windows GUI, no real Apple Music for Windows installation, no real
audio hardware, and no real `%LOCALAPPDATA%` PlayCache directory. All prior "testing"
was `dotnet build` / `dotnet test` (unit tests with fakes/fixtures) run via
`"/mnt/c/program files/dotnet/dotnet.exe"` from WSL, plus static code review. **Nothing
in this project has ever actually been run and watched switch a real device's format
while a real track plays.** That's the gap this handoff is for.

If you're reading this, you have native Windows access (or at least the ability to
launch a Win32 GUI app, install/use the real Apple Music Windows app, and observe a
real audio device's format in Windows' Sound control panel). Use it — that's the whole
point of this handoff.

## Where things stand (as of this handoff)

Repo: `Gholie/samklang` — clone it wherever suits you; nothing below depends on a
particular checkout path.

Domain vocabulary you need before touching anything: read `CONTEXT.md` at repo root.
Project plan/milestones: `docs/PLAN.md`.

Merged and closed (issues #1–#9 — CI/tracer-bullet, clamp, resting-format/grace-period,
device targeting, catalog layer, tray/autostart, PlayCache layer, status dashboard):
**all of it has unit test coverage and (separately) passed static code review, but none
of it has ever been run against a real Apple Music session or real audio hardware.**
Every checklist item below is still live and unverified in the one way that actually
matters — regression-testing all of it for real is the job here, not just the two
newest pieces.

`main` is fully up to date as of this handoff:

- **PR #18** (merged, `fe3809c`) — `feat(resolver): add PlayCache layer for offline
  exact resolution` (closed #7).
- **PR #19** (merged, `506db0b`) — `feat(ui): replace placeholder window with Fluent
  status dashboard` (closed #9). This one went through a fix-and-re-review cycle before
  merging — see the AudioTierClassifier item below, it's the single highest-priority
  thing to verify for real.

Real-environment testing against #18/#19 found three confirmed, fatal bugs in the
PlayCache layer — see issue #20 and (once opened) its fix PR: wrong cache directory
name (`SubscriptionPlayCache` vs. hard-coded `PlayCache`), wrong candidate extension
(real cache files are `.m4p`, not `.m4a`/`.mp3`), and a rejected `drms` (FairPlay AAC)
sample entry type. If you're re-testing PR #18's checklist below, re-pull `main` first
to make sure you're testing the fixed version, not the original one these bugs were
found against.

Just `git pull` on `main` and test against that directly.

Remaining backlog: **#10** (Velopack packaging + release workflow) — implemented, see
this doc's own "Issue #10" section below for what still needs real-world verification.

## Setup

- Use real `git`/`dotnet` CLI directly (no `.exe` workaround needed on native Windows).
- Real Apple Music for Windows app must be installed from the Microsoft Store and
  signed in with an account that has active playback (ideally one with lossless and
  hi-res-lossless tracks in the library/catalog, to exercise all Audio Tiers).
- A real audio output device that supports multiple sample rates/bit depths in
  Windows' shared-mode "Default Format" (Sound Control Panel → device Properties →
  Advanced tab) — this is what gets switched. Note its *original* format before you
  start, since Samklang will change it and revert it (Resting Format behavior).
- Build: `dotnet build` at repo root. Run: `dotnet run --project src/Samklang` (or
  launch the built exe from `bin/Debug/net8.0-windows.../Samklang.exe`).

## What to actually verify

### PR #18 — PlayCache layer (issue #7, see also issue #20's real-world bug fixes)

The implementing agent's stated assumptions that specifically need real-world
confirmation (these were educated guesses mined from a third-party OSS project's
docs, not verified against the actual app):

1. **PlayCache directory actually exists and matches expectations.** Confirm
   `%LOCALAPPDATA%\Packages\AppleInc.AppleMusicWin_<hash>\...\AMPLibraryAgent\...` (or
   wherever it actually resolves to on this real install) exists, and that
   `PlayCachePaths`/`IPlayCacheLocator` in `src/Samklang/Resolver/PlayCache/` finds
   it. Issue #20 already found the leaf directory is `SubscriptionPlayCache`, not
   `PlayCache`, on one real install and got a fix merged for it — confirm the fix
   actually locates the directory correctly on your install too, and file a new issue
   if the layout differs again.
2. **File format assumptions.** Confirm cached files' actual extension/container
   (issue #20 found `.m4p`, FairPlay-protected MP4, not `.m4a`/`.mp3` as originally
   assumed) match what `PlayCacheAudioFormatProbe` now handles. Play a known
   hi-res-lossless track, then inspect the cache directory directly — does the
   probed sample rate/bit depth match what you'd expect from the track's real
   Audio Tier? Issue #20's real cache only had lossy 44.1kHz AAC (`drms`) entries — a
   lossless/hi-res track landing in the same cache (and what sample-entry type it
   uses) is still unverified, flag it if you can test it.
3. **File↔Track matching heuristics** (`PlayCacheFileMatcher`) — play several tracks
   back to back, confirm the layer picks the *currently playing* file, not a stale
   one from a previous track. Try pausing, skipping, and playing tracks with very
   similar/duplicate names to stress the heuristic. Note: issue #20 observed no
   `Downloads\*.tmp` folders under the real cache root at all, which may mean the
   `MatchDownloadTemp` heuristic never actually fires on this cache layout — confirm
   whether that's really dead code on real installs or just wasn't exercised.
4. **Offline behavior** — with network disabled (or catalog layer otherwise
   disabled/broken), confirm a previously-played track still resolves at **Exact**
   confidence purely from PlayCache, per issue #7's acceptance criteria.
5. **Graceful degradation** — if you can, simulate a missing/restructured cache
   directory (rename it) and confirm the app doesn't crash and just silently skips
   to the next resolver layer.
6. **PlayCacheInfo.xml parsing** — confirmed already (issue #20) to parse correctly
   against a real file, DTD-ignoring included. Just regression-test this didn't
   break with the extension/directory-name fixes.

### PR #19 — Status dashboard (issue #9)

Nothing here has been visually verified — treat it as a from-scratch UI smoke test:

1. **It renders at all.** Launch the app, confirm the `FluentWindow` opens without
   XAML binding errors (watch the Output window in Visual Studio, or console, for
   binding errors — these fail silently in WPF otherwise).
2. **Theme following** — switch Windows between light/dark mode (Settings → 
   Personalization → Colors) while the app is running, confirm it follows live via
   `SystemThemeWatcher` without needing a restart.
3. **Now-playing panel** — play a track, confirm Track (title/artist/album), Audio
   Tier badge, Target Format, Confidence + source layer, and the device's actually-
   applied format all show real, correct, live-updating values — no restart needed
   when the track changes.
4. **History list** — confirm recent format resolutions/switches accumulate with
   correct timestamps, in the right order, and (per the implementer's notes) that
   trimming behaves sanely over a long listening session rather than growing
   unbounded or losing entries.
5. **Settings page** — for each of: device targeting, tier mappings, Resting
   Format, Grace Period, autostart — change the value in the UI, restart the app,
   confirm it persisted (round-trips through `ISettingsStore`/`SettingsManager`
   correctly). For autostart specifically, confirm it actually toggles the
   `HKEY_CURRENT_USER\...\Run` registry key and that Windows actually launches the
   app on next login.
6. **AudioTierClassifier heuristic — highest priority item in this whole doc.** Code
   review caught (and a follow-up fix, now merged, addressed) exactly this class of
   bug pre-merge: the classifier originally inferred "lossless" from `Confidence`
   alone, which would've badged plain lossy AAC/MP3 files resolved via the PlayCache
   layer as **"Lossless"**, since that layer reports `Confidence.Exact` for any file
   whose header it can parse, lossy or not. The fix added a genuine
   `FormatResolution.IsLossless` signal (`src/Samklang/Domain/FormatResolution.cs`),
   set by each layer based on actual codec evidence (`CatalogFormatResolverLayer` →
   always `true`, only matches ALAC; `PlayCacheFormatResolverLayer` → `true` only if
   the probe found a real PCM bit depth, which AAC/MP3 never expose). This was only
   checked with synthetic fixtures, never a real file. **Concretely verify**: play a
   plain lossy AAC track (not a lossless/hi-res one) enough times that the PlayCache
   layer — not the catalog layer — is the one resolving it (e.g. with network/catalog
   disabled per item 4 above), and confirm the dashboard badges it "Lossy Stereo" or
   "Unknown", never "Lossless" or "Hi-Res Lossless". Also generally: watch for *any*
   other visibly wrong badge (e.g. hi-res track badged as plain Lossless), not just
   this specific fixed case.
7. **Thread-safety of live updates** — this is hard to verify by eyeballing, but if
   you see any UI freezes, crashes, or `InvalidOperationException`s about being
   called from the wrong thread when tracks change rapidly (fast skip-through), that
   points to a real threading bug in the `TrackSyncCoordinator` → `ObservableCollection`
   wiring — file it with a repro (how fast/how you skipped).

### Issue #10 — Velopack packaging, release workflow, in-app updates

**What was verified in the dev environment:** `dotnet build`/`dotnet test` pass with
Velopack referenced and `VelopackApp.Build().Run()` called from a hand-written `Main`;
`AppUpdateService`'s own unit tests confirm it constructs without throwing and reports
`NotInstalled` when run outside a real Velopack install (i.e. exactly the environment
`dotnet run`, `dotnet test`, and CI all run in). The `.github/workflows/release-please.yml`
YAML was validated for syntax but **never actually run** — nothing in the dev
environment can merge a release PR or execute a `windows-latest` GitHub Actions runner.

**How releases work (release-please):** releases are not cut by hand-pushing tags.
`release-please` watches Conventional Commits on `main` and maintains a "release PR"
(version bump in `Samklang.csproj` + `CHANGELOG.md`). Merging that PR makes it create
a *draft* GitHub Release; the same workflow run then builds the Velopack artifacts,
attests their build provenance, attaches them to the draft, and publishes it. The
draft stage is what guarantees a release is never publicly visible (including to the
app's own update check, which ignores drafts) without attested artifacts on it.

**What a human must verify on a real Windows machine, in order:**

1. **First release end-to-end.** Merge the release-please PR proposing `v0.1.0` (it
   should appear after this work lands on `main`; confirm it proposes `0.1.0` and that
   its diff bumps `<Version>` in `Samklang.csproj`), then watch
   `.github/workflows/release-please.yml`'s `build-and-publish` job run to completion.
   Confirm:
   - The build/test steps pass on the `windows-latest` runner.
   - `vpk download github` doesn't hard-fail the job on this *first* release (there's
     no prior published release to download yet — the step has
     `continue-on-error: true` for exactly this reason; confirm the rest of the job
     still produces a correct, installable package despite it).
   - The GitHub Release starts as a **draft**, gets the installer (named
     `Samklang-win-Setup.exe` or similar — note the exact name and fix the README's
     Install section if it differs) plus `.nupkg`/portable zip attached, and is then
     automatically flipped to published with the `v0.1.0` tag created.
   - The provenance attestation verifies:
     `gh attestation verify <downloaded Samklang-win-Setup.exe> --repo Gholie/samklang`.
2. **Fresh install.** Download the setup exe from that release on a clean (or
   throwaway VM) Windows 11 machine and run it. Confirm:
   - No admin elevation prompt (per-user install).
   - SmartScreen's "unknown publisher" warning appears (expected — the exe isn't
     code-signed) and "Run anyway" proceeds.
   - The app installs, launches automatically, and the tray icon appears with a
     tooltip showing `Samklang v0.1.0` plus track/format info.
   - The dashboard window's title bar reads `Samklang v0.1.0`.
   - The tray menu's "Check for Updates" item is present and labeled with the current
     version.
3. **Update detection and apply.** Once a *second* release (e.g. `v0.1.1`) has been
   published via the same release-please flow (merge another release PR), on the
   machine still running the installed `v0.1.0`:
   - Relaunch (or wait for) the app's startup update check to find, download, and
     apply the new version automatically, then confirm the app restarts on its own at
     the new version (tray tooltip/window title now read `v0.1.1`).
   - Separately, verify the tray menu's manual "Check for Updates" path: trigger it
     while already on the latest version and confirm the balloon tip says "You're on
     the latest version" instead of re-downloading anything.
   - This is the one acceptance criterion ("an installed app detects and applies an
     update from a newer release") that fundamentally cannot be faked or
     unit-tested — it requires two real, published releases and a real installed copy
     in between.
4. **Delta updates (optional, nice-to-have check).** After the second release exists,
   confirm the release workflow's `vpk download github` step successfully produced a
   delta package this time (unlike the first release) and that the update in step 3
   was reasonably fast/small, consistent with a delta rather than a full re-download.

If any of these fail, the most likely culprits (based on what couldn't be exercised
here) are: the `GithubSource`/`UpdateManager` API surface having drifted from what's
coded in `src/Samklang/Updates/AppUpdateService.cs` since Velopack 1.2.0, or `vpk`'s
CLI flags having changed since this was written — recheck against
<https://docs.velopack.io> if so.

### Cross-cutting / regression

- Tray icon tooltip, close-to-tray, single-instance guard, pause-switching menu item
  (from #8, already merged) — PR #19 rewrote `MainWindow.xaml.cs`, so regression-test
  these didn't break.
- End-to-end device switch: play a lossy track, then a hi-res-lossless track back to
  back, watch the *actual* Windows device format (Sound Control Panel) change to
  match, with the expected brief mute during the switch.
- Idle/Resting Format revert: pause playback (or close Apple Music), wait past the
  configured Grace Period, confirm the device reverts to the Resting Format.

## How to report back

- File a new GitHub issue for anything you find — be concrete about what you observed
  vs. expected (exact file paths, exact device format before/after, screenshots of the
  dashboard if something renders wrong). See issue #20 for the format/level of detail
  that's actually actionable for a follow-up fix (real paths, real hex/box dumps, what
  was confirmed working vs. not).
- These changes already passed CI and (separately) static code review; you're
  providing the first and only real functional check against the actual app and
  actual hardware. Treat a real bug you find here as more authoritative than either of
  those — if something contradicts what review/tests assumed, the real environment
  wins.
