# App icon

Samklang's icon is an eighth note whose head is a circular sync arrow, white on an
Apple Music-style gradient tile (`#FB5C74` → `#FA233B`). It deliberately shares Apple
Music's color language (it's a companion app) while the sync-arrow head keeps it
distinguishable from the real Apple Music icon sitting in the same tray.

## Files

| File | Purpose |
| --- | --- |
| `icon.svg` | Master artwork, used for the 48-256 px ICO entries |
| `icon-small.svg` | Bold-weight variant with exaggerated strokes; used for the 16/24/32 px ICO entries so lines survive tray rendering |
| `icon.ico` | Multi-resolution icon (16, 24, 32, 48, 64, 128, 256 px, PNG-compressed entries) referenced by `<ApplicationIcon>` in `src/Samklang/Samklang.csproj` |

## How it reaches the UI

`<ApplicationIcon>` embeds the ICO in `Samklang.exe`. From there:

- **Title bar / taskbar / Alt-Tab:** WPF windows fall back to the exe icon automatically —
  no `Icon=` needed in XAML.
- **Tray:** `MainWindow.TryGetAppIcon()` calls `Icon.ExtractAssociatedIcon(processPath)`,
  which now finds the embedded icon instead of falling back to `SystemIcons.Application`.

## Regenerating

After editing the SVG masters, from the repo root:

```powershell
dotnet run --project tools/IconGen -- ico assets/icon.ico `
  16:assets/icon-small.svg 24:assets/icon-small.svg 32:assets/icon-small.svg `
  48:assets/icon.svg 64:assets/icon.svg 128:assets/icon.svg 256:assets/icon.svg
```

`tools/IconGen` can also spot-check how a design rasterizes before committing to it:

```powershell
# individual PNGs at given sizes
dotnet run --project tools/IconGen -- png assets/icon-small.svg out/ 16 24 32
# side-by-side contact sheet on light and dark rows
dotnet run --project tools/IconGen -- sheet out/sheet.png 32 assets/icon.svg assets/icon-small.svg
```

Keep the two masters visually in sync: `icon-small.svg` is the same geometry with
thicker strokes (ring 24 vs 16, stem 20 vs 14), a larger arrowhead, and a slightly
bigger tile (less padding) — legibility at 16 px beats fidelity.
