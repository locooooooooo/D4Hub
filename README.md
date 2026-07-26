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

## Safety boundary

D4Hub does not read or write game memory, inject code, inspect Diablo IV traffic,
automate input, provide macros, hide itself from detection, or copy assets from
the downloaded reference package. The only network request is the explicit
D2Core build import requested by the user; the normalized result is cached and
subsequent imports resolve from the bundled public library or local cache.

The repository verifier scans source files for common process-memory,
injection, and input-automation APIs and fails if one is introduced.

## Run

Requirements: Windows 10 or 11 and the .NET 8 SDK.

```powershell
dotnet run --project .\src\D4Hub.App\D4Hub.App.csproj
```

Open a screenshot directly in the calibration preview:

```powershell
dotnet run --project .\src\D4Hub.App\D4Hub.App.csproj -- --preview C:\path\character-panel.png
```

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
- `library`: repository-backed normalized D2Core records (`index.json` plus per-build JSON).
- `tests/D4Hub.VisionProbe`: read-only diagnostics for supplied screenshots.
- `tests/D4Hub.GameWindowFixture`: screenshot-hosting window used only for end-to-end HUD acceptance.
- `scripts/verify.ps1`: canonical local and CI verification command.

## License

[MIT](LICENSE)
