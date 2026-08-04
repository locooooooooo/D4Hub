# D4Hub

D4Hub is a free, local Windows HUD that follows the Diablo IV character panel,
recognizes a registered build from visible skill-bar pixels, and shows the
target affixes for each equipped slot. It does not connect to the game process.

## Current slice

- D4 client-window discovery from window metadata without memory access.
- Foreground-only capture plus process-name and multilingual-title validation.
- Screen-only character-panel detection with an explicit confidence threshold.
- Three-part perceptual skill-bar fingerprints for local build matching.
- Eleven editable equipment-slot affix rules rendered over the character panel.
- One-click paste/import of D2Core planner links, including the selected `var` variant.
- Structured equipment and affix records with a bundled public library and local cache.
- A transparent, non-activating, click-through HUD that tracks the game window.
- Screenshot learning and preview for offline calibration.
- Automatic local persistence plus portable JSON library import and export.
- A lazy-loaded embedded Helltides.com workspace for the Owner-requested
  Helltide location view.

## Safety boundary

D4Hub does not read or write game memory, inject code, inspect Diablo IV traffic,
automate input, provide macros, or hide itself from detection. At the user's
request, 407 Diablo4Companion visual files are retained under
`third_party/Diablo4Companion/visual-assets` as source-only research inputs with
per-file SHA-256 evidence. They are not copied into build or publish outputs
because the upstream MIT notice does not establish redistribution rights for
underlying game-derived imagery. The only in-app data import is the explicit
D2Core build import requested by the user; the normalized result is cached and
subsequent imports resolve from the bundled public library or local cache. The
embedded Helltides page loads only after the user opens the Map Tools workspace.
Its top-level navigation is pinned to the reviewed `https://helltides.com/` host;
downloads and web permissions are blocked. The embedded page runs in a WebView2
InPrivate profile with strict tracking prevention, blocks reviewed CMP,
advertising, analytics, and bidder requests, and removes only known leftover
consent/ad UI. D4Hub does not click consent controls or write consent cookies.

The repository verifier scans source files for common process-memory,
injection, and input-automation APIs and fails if one is introduced.

## Run

Requirements: Windows 10 or 11, the .NET 8 SDK, and Microsoft Edge WebView2
Runtime for the embedded map.

```powershell
dotnet run --project .\src\D4Hub.App\D4Hub.App.csproj
```

Open directly into the embedded map workspace:

```powershell
dotnet run --project .\src\D4Hub.App\D4Hub.App.csproj -- --map
```

Open a screenshot directly in the calibration preview:

```powershell
dotnet run --project .\src\D4Hub.App\D4Hub.App.csproj -- --preview C:\path\character-panel.png
```

Run the offline combat-text probe against a local recording:

```powershell
dotnet run --project .\tools\D4Hub.CombatProbe -c Release -- `
  --video "C:\path\combat.mp4" `
  --output ".\.artifacts\combat-stats\report.json" `
  --pipeline windows `
  --fps 5 --start 0 --duration 20 --crop 100,0,1400,800 `
  --profile 1080p-zhCN-sdr
```

The probe requires FFmpeg on `PATH`; the Windows pipeline also requires the
Windows Chinese OCR language pack. It streams visible video frames through OCR and never connects to the game
process. The schema-4 JSON separates the requested and active pipelines,
runtime fallback, parsed candidates, confirmed events, rejection reasons,
folded duplicates, pending observations, coverage, suspicious small events,
damage windows, and processing time. `--pipeline paddle` enables a local
PP-OCRv5 experiment in the offline probe only. On the supplied 3-11 second
clip it was too slow for the 0.6-second live budget and produced a catastrophic
overlap merge, so it is not the application default. Dense overlapping combat
text can be missed or merged by either engine; probe output is calibration
evidence and a screen-derived estimate, not an exact combat log or production
DPS meter.

The app exposes the live estimate as a transparent in-game overlay. Under
`HUD 叠层` -> `统计 HUD`, enable `伤害统计` and choose the expanded or compact
layout. The overlay shows DPS and recent one-second damage; the expanded layout
also shows the one-second peak, total damage, and data quality. Values use the
Diablo IV Chinese units `万`, `亿`, `兆`, and `京`.

Live OCR runs only while Diablo IV is foreground, processes the calibrated
combat-text region asynchronously, and drops frames while OCR is busy. A
single frame never enters the totals: spatial and motion tracking must confirm
the same restricted damage candidate across at least two frames. The HUD labels
the current path as a baseline estimate, low coverage, or unavailable instead
of presenting a format heuristic as an accuracy percentage. The experience,
material, and key switches remain disabled placeholders until their
visible-screen collection pipelines are implemented.

Open the D4 character panel, select a local profile, then capture the visible BD
fingerprint from the game or from a screenshot. Future frames are matched only
when the panel and build confidence gates both pass. User data is stored at:

```text
%LOCALAPPDATA%\D4Hub\build-state.json
```

To import a D2Core build, copy a link such as
`https://www.d2core.com/d4/planner?bd=1Zep&var=6`, then click `粘贴并导入`.
The `var` value is one-based exactly as displayed by D2Core, so `var=6`
imports the sixth variant. The selected variant is written into the BD profile with every equipment item
and affix. A normal repeat import does not call D2Core. `刷新` is the explicit
network path when the source build has changed. Before a visual fingerprint is
captured, the selected imported BD is used as an explicit manual HUD profile;
automatic build matching starts after screenshot or game fingerprint capture.

Open `地图工具` in the left navigation to load the embedded Helltides map. The
page is not requested during application startup, and its cookies and history
are discarded with the temporary WebView session. Top-level links that leave
`helltides.com` are handed to the system browser only after confirmation, and a
browser fallback remains available when WebView2 or the site cannot load.

## Verify

The canonical repository check builds Release binaries, runs the offline
acceptance checks, scans the safety boundary, and validates the Git diff.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

## Publish

Create a framework-dependent Windows build:

```powershell
dotnet publish .\src\D4Hub.App\D4Hub.App.csproj -c Release -r win-x64 --self-contained false -o .\publish\win-x64
```

Create an unsigned, local Windows installer and Velopack update-feed prototype.
The script runs repository verification first, requires matching release notes
under `docs/release-notes`, and verifies the generated package size and SHA-256:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -Version 1.0.0
```

Online update checks are disabled by default. After an update endpoint and
release policy are approved, pass its absolute HTTPS URL with `-UpdateFeedUrl`.
Portable directory builds remain offline and do not replace their own files.
Local packaging, an unsigned installer, and a generated feed do not prove a
signed, hosted, published, or player-accepted release.

Refresh the repository public library for another D2Core build:

```powershell
dotnet run --project .\tools\D4Hub.LibraryTool\D4Hub.LibraryTool.csproj -- `
  "https://www.d2core.com/d4/planner?bd=1Zep&var=6" .\library
```

## Architecture

- `src/D4Hub.Core`: BD profiles, D2Core URL/API parsing, structured affixes, library resolution, image hashes, panel detection, and atomic JSON persistence.
- `src/D4Hub.App`: screen capture, D4 window tracking, WPF workbench, and click-through HUD.
- `tests/D4Hub.AcceptanceTests`: dependency-free executable acceptance checks.
- `tools/D4Hub.LibraryTool`: explicit public-library refresh command.
- `tools/D4Hub.CombatProbe`: offline screen-OCR combat recording probe.
- `library`: repository-backed normalized D2Core records (`index.json` plus per-build JSON).
- `tests/D4Hub.VisionProbe`: read-only diagnostics for supplied screenshots.
- `tests/D4Hub.GameWindowFixture`: screenshot-hosting window used only for end-to-end HUD acceptance.
- `scripts/verify.ps1`: canonical local and CI verification command.

## License

[MIT](LICENSE)
