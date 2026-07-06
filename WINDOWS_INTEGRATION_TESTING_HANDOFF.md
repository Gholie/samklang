# Windows integration testing handoff

Some Samklang behavior can only be verified on a real Windows machine with real hardware/software
(Apple Music for Windows, Core Audio devices, an installed Velopack package, GitHub's live API) —
none of which the development environment used to build these changes could exercise directly.
This doc tracks what a human with real Windows access needs to manually verify, organized by the
issue that introduced it.

## Issue #10 — Velopack packaging, release workflow, in-app updates

**What was verified in the dev environment:** `dotnet build`/`dotnet test` pass with Velopack
referenced and `VelopackApp.Build().Run()` called from a hand-written `Main`; `AppUpdateService`'s
own unit tests confirm it constructs without throwing and reports `NotInstalled` when run outside
a real Velopack install (i.e. exactly the environment `dotnet run`, `dotnet test`, and CI all run
in). The `.github/workflows/release.yml` YAML was validated for syntax but **never actually run**
— nothing in this environment can push a tag or execute a `windows-latest` GitHub Actions runner.

**What a human must verify on a real Windows machine, in order:**

1. **First tagged release end-to-end.** Push a `v0.1.0` tag and watch `.github/workflows/release.yml`
   run to completion. Confirm:
   - The build/test steps pass on the `windows-latest` runner.
   - `vpk download github` doesn't hard-fail the job on this *first* release (there's no prior
     release to download yet — the step has `continue-on-error: true` for exactly this reason;
     confirm the rest of the job still produces a correct, installable package despite it).
   - A GitHub Release is created for the tag with `Samklang-Setup.exe` (and the accompanying
     `.nupkg`/portable zip) attached as assets.
2. **Fresh install.** Download `Samklang-Setup.exe` from that release on a clean (or throwaway VM)
   Windows 11 machine and run it. Confirm:
   - No admin elevation prompt (per-user install).
   - SmartScreen's "unknown publisher" warning appears (expected — the exe isn't code-signed) and
     "Run anyway" proceeds.
   - The app installs, launches automatically, and the tray icon appears with a tooltip showing
     `Samklang v0.1.0` plus track/format info.
   - The dashboard window's title bar reads `Samklang v0.1.0`.
   - The tray menu's "Check for Updates" item is present and labeled with the current version.
3. **Update detection and apply.** Once a *second* tag (e.g. `v0.1.1`) has been released via the
   same workflow, on the machine still running the installed `v0.1.0`:
   - Relaunch (or wait for) the app's startup update check to find, download, and apply the new
     version automatically, then confirm the app restarts on its own at the new version (tray
     tooltip/window title now read `v0.1.1`).
   - Separately, verify the tray menu's manual "Check for Updates" path: trigger it while already
     on the latest version and confirm the balloon tip says "You're on the latest version" instead
     of re-downloading anything.
   - This is the one acceptance criterion ("an installed app detects and applies an update from a
     newer release") that fundamentally cannot be faked or unit-tested — it requires two real,
     published releases and a real installed copy in between.
4. **Delta updates (optional, nice-to-have check).** After the second release exists, confirm the
   release workflow's `vpk download github` step successfully produced a delta package this time
   (unlike the first release) and that the update in step 3 was reasonably fast/small, consistent
   with a delta rather than a full re-download.

If any of these fail, the most likely culprits (based on what couldn't be exercised here) are: the
`GithubSource`/`UpdateManager` API surface having drifted from what's coded in
`src/Samklang/Updates/AppUpdateService.cs` since Velopack 1.2.0, or `vpk`'s CLI flags having
changed since this was written — recheck against <https://docs.velopack.io> if so.
