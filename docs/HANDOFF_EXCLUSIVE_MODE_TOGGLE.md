# Handoff: "Exclusive Mode" toggle — feasibility and recommended scope

Written 2026-07-07, following PR #37 (exclusive-mode capability probing). Live findings below were
verified on the project's real-environment test box (FiiO K11 over USB, Windows 11 Pro 26200).

## The question

PR #37 made the rate probe use WASAPI exclusive mode *as a query*. The follow-up idea: could
Samklang offer a user-toggleable exclusive mode for playback, like foobar2000/TIDAL/Qobuz offer?

## Feasibility: the blunt part first

**A toggle that makes Apple Music play through WASAPI exclusive mode is not buildable — by
Samklang or anyone else — until Apple builds it.** Exclusive-mode streams are opened by the
rendering application itself (`IAudioClient.Initialize(AudioClientShareMode.Exclusive, …)`).
Samklang renders nothing; Apple Music renders, and the Apple Music Windows app has no exclusive
output option. Windows provides no mechanism to force another process's streams into exclusive
mode. Any feature under this name must therefore be honest about being something adjacent, not
actual exclusive playback.

Also worth internalizing: **rate-matched shared mode is already nearly transparent.** When the
Device Format's sample rate equals the Track's rate (Samklang's whole job), the mixer does no
resampling. The mixer's float32 round-trip of 24-bit integer samples is exact (24-bit values fit
float32's mantissa). What remains between shared mode and bit-perfect:

1. System/app volume below 100% (rescales samples),
2. Audio enhancements / APOs on the endpoint (driver or Windows effects),
3. Other applications' sounds mixing in,
4. Communications ducking.

Items 1 and 3 are user behavior; item 4 is a Windows setting. Item 2 is the one Samklang could
actually control — and it is the substance of what "exclusive mode" buys audiophiles in practice
on a device like the K11.

## The realistic feature menu

### Option A — "Disable audio enhancements" per target device (recommended)

Toggle the endpoint's system-effects (APO) processing off, the same as Sound control panel →
device properties → "Audio enhancements: Off". Combined with rate matching this yields the
closest shared-mode approximation of exclusive-mode output.

- Well-known (undocumented but stable) property key: `PKEY_AudioEndpoint_Disable_SysFx` =
  `{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5` (DWORD, 0 = enhancements on, 1 = off).
- Writable through `IPolicyConfig::SetPropertyValue` — the vtable in
  `src/Samklang/Devices/PolicyConfigInterop.cs` already declares this slot; it just needs a
  public wrapper. Going through IPolicyConfig (AudioSrv) rather than raw HKLM registry writes
  should avoid elevation; **verify this unelevated on the test box before building UI on it.**
- Caveat: on endpoints with no APOs installed the toggle is a no-op. The K11's TUSBAUDIO driver
  may well expose none — detect and gray out rather than pretending.

### Option B — "Allow exclusive control" checkboxes (optional, advanced)

The two Sound-control-panel checkboxes ("Allow applications to take exclusive control", "Give
exclusive mode applications priority") are per-endpoint properties. Samklang could surface them
so a user who *also* runs an exclusive-capable player doesn't have to dig through control panel.
Marginal value for Samklang's mission (it does nothing for Apple Music playback) — build only if
cheap after Option A.

- Live finding: on this box the K11 endpoints carry **no** override values for these yet —
  the defaults (both enabled) are implicit, and the property only materializes in
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{endpoint}\Properties`
  after the checkbox is first toggled. So the exact PROPERTYKEYs must be discovered empirically:
  flip each checkbox in control panel, diff the Properties subkey, record GUID+PID. (Community
  references suggest the `{b3f8fa53-0004-438e-9003-51a46e139bfc}` family, but the observed K11
  endpoints only show unrelated PIDs 0/2/6/9/15/24/27/37/43 — do not trust folklore, diff it.)
- K11 endpoint GUIDs observed (multiple from re-enumeration; enumerate live rather than
  hardcoding): `{50f5c80c-d9e8-4294-b3db-dbced00c9c1b}`, `{87484629-1c1e-4039-9042-1e83747c83e3}`,
  `{a4690487-87c1-47a5-a9b6-e9e7340d05d5}`.

### Option C — Samklang becomes an exclusive-mode renderer

Out of scope entirely. It would mean becoming the player (decrypting/decoding Apple Music
content), which is neither feasible nor appropriate.

## Recommended scope

Ship Option A as a Settings toggle named for what it does — e.g. **"Disable Windows audio
enhancements on the target device"** — not "exclusive mode". Explicitly do not promise
bit-perfect. Option B behind it if trivial. Decline C.

## Implementation sketch (Option A)

Follow the existing layering exactly (see `DeviceControllerTests` for the pattern):

1. `PolicyConfigInterop`: add `SetPropertyValue`/`GetPropertyValue` wrappers (PROPVARIANT
   marshaling for a DWORD is the only fiddly part).
2. `IAudioEndpoint`: add `bool? GetEnhancementsDisabled(string deviceId)` and
   `void SetEnhancementsDisabled(string deviceId, bool disabled)` (null = endpoint has no APOs /
   property unreadable).
3. `DeviceController`: apply-on-toggle plus re-apply when the effective target device changes
   (Follow mode can move between devices). Decide and document whether Samklang reverts the
   property on exit — recommendation: no auto-revert; it's a user-visible Windows setting, treat
   it like the control panel does. (Contrast with Resting Format, which *is* auto-reverted —
   state the asymmetry in the setting's tooltip.)
4. `Settings`/`SettingsManager` + `SettingsViewModel`: one nullable-tristate-aware checkbox,
   grayed out when the endpoint reports no APOs.

## Verification plan (on this box)

1. Unelevated: write `Disable_SysFx = 1` on the K11 via `IPolicyConfig::SetPropertyValue`; confirm
   no elevation prompt/failure, confirm the control panel reflects "Off", confirm audio still
   plays, confirm the value survives device re-plug.
2. Confirm whether the K11 endpoint exposes any APOs at all (`Properties` subkey `{d04e05a6-...}`
   FX keys, or the enhancements tab's presence in control panel). If none, find a second endpoint
   (the NVIDIA HDMI one has enhancements) to prove the write path works.
3. For Option B: checkbox-flip + registry-diff recipe described above.

## Risks

- All of IPolicyConfig and these property keys are undocumented; they've been stable for a decade
  (EarTrumpet, WindowsLosslessSwitcher rely on them) but each Windows feature update deserves a
  smoke test.
- Some third-party drivers cache APO state; a device re-enable may be needed for the change to
  bite. Verify on the K11, note behavior in the setting's tooltip if so.
- None of this changes what Apple Music itself does — set expectations in UI copy.
