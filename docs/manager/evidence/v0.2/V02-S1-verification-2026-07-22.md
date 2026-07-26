# V02-S1 Independent Verification

Date: 2026-07-22 (Asia/Shanghai)

Task: `task_9ad4383b6146061953b01716a3221af8`

## Scope

- `tests/D4Hub.VisionProbe/Program.cs`
- recognition-focused additions in `tests/D4Hub.AcceptanceTests/Program.cs`
- no product Core, WPF, library, packaging, governance, PKR database, artifact, tool-cache, or reference changes

## Worker Result

- VisionProbe reports the default panel threshold (`0.55`), decision status, boolean acceptance, and reason in JSON.
- A panel below threshold produces `fingerprint: null` and exit code `4`.
- The optional threshold is finite and limited to `0.35..0.95`; invalid input exits `2` with usage.
- Acceptance coverage was added for no registered fingerprint, below-threshold match, near-tie ambiguity, and clearly distinct winner.
- Existing `BuildFingerprintService` behavior passed those cases; `src/D4Hub.Core/HudModels.cs` was not changed.

## Manager Review And Revision

The first worker result was rejected. A relative screenshot path passed `File.Exists` but `ScreenFrameService.Load` required an absolute URI, causing `UriFormatException`, exit `1`, and no JSON. The worker normalized the validated path with `Path.GetFullPath`; the manager then reran the real local command successfully.

Reviewed file hashes after the revision:

- `6B0D8F7091070EDC4037087B6F6BBE1169FF4745A0EB51158DE124FAC64A9665` `tests/D4Hub.VisionProbe/Program.cs`
- `9B193DF888D739754BB6DB0864041EBA69DD112609A888A1A5C6858958C852A1` `tests/D4Hub.AcceptanceTests/Program.cs`
- `9F497B39CA8F57168D61BCB1E66148AB8BAAACFC9B06C38AEF57D34947F2A638` `src/D4Hub.Core/HudModels.cs` (review reference; not worker-modified)

## Independent Commands

`dotnet build .\D4Hub.sln -c Release --nologo`

- PASS; 0 warnings, 0 errors.

`dotnet run --project .\tests\D4Hub.AcceptanceTests\D4Hub.AcceptanceTests.csproj --configuration Release --no-build`

- PASS; 29/29 checks.

`dotnet run --project .\tests\D4Hub.VisionProbe\D4Hub.VisionProbe.csproj --configuration Release --no-build -- '.artifacts\d4hub-hud-preview-final.png'`

- PASS; relative path accepted.
- Observed image: 1404x841.
- Observed panel X: `0.6182336182`.
- Observed confidence: `0.8184222028` against threshold `0.55`.
- Decision: accepted; fingerprint emitted.

The same command with `--panel-threshold 0.95`:

- PASS rejection contract; exit `4`, status `rejected`, reason `panel-confidence-below-threshold`, fingerprint null.

The same command with `--panel-threshold NaN`:

- PASS usage contract; exit `2`, finite-range error and usage emitted.

`powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1`

- PASS; Release build, 29/29 acceptance checks, safety scan, and repository verification.

Additional checks:

- no forbidden memory/injection/input-automation API match under `src`.
- no whitespace error in the two worker files or manager board using `git diff --no-index --check` against an empty input.

## Classification

- implemented: yes, current working tree.
- tested: yes, deterministic synthetic decisions plus one local 1404x841 game-content/HUD preview smoke whose live-game provenance is not established.
- packaged: no new V02-S1 package.
- independently verified: yes, for this slice and these commands only.
- owner accepted: no.
- committed: no; product source remains untracked against HEAD `8e944d23bead51ecc1e2a386b8e088f0ceb0e722`.
- pushed: no.
- published: no.

## Residual Risk

The accepted smoke image is not a raw target-resolution matrix case and contains a visible HUD overlay. No 1920x1080 or 1280x960 corpus, multi-BD stability run, negative real frame, physical-device run, exact product SHA, or Owner acceptance exists. V02-S1 therefore closes only the diagnostic/rejection contract, not v0.2.
