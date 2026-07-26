# Vision Replay Manifest Contract

Run the offline replay harness from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-vision-replay.ps1 `
  -Manifest <manifest.json> `
  -Output <report.json>
```

The harness accepts schema version `1` and preserves the manifest's `evidenceClass` as either `smoke` or `target`. A smoke result is diagnostic evidence only and is never target-resolution acceptance. Target manifests must include at least one `1920x1080` case and one `1280x960` case.

Each case requires `id`, `image`, `expectedWidth`, `expectedHeight`, `panelThreshold`, and `expectedDecision`. Relative image paths resolve from the manifest directory. `expectedDecision` is `accepted` or `rejected`. An optional `expectedPanel` contains normalized `x`, `y`, `width`, and `height` ranges, each expressed as numeric `min` and `max` values from zero through one.

`expectedWidth` and `expectedHeight` always mean the raw PNG dimensions in the declared capture coordinate space. They are never dimensions produced by cropping, scaling, DPI conversion, or normalization. Every target case must declare `captureSpace` as `client-device-pixels`: the game client area's physical pixels, excluding title bars, resize borders, and window shadows. Pixels letterboxed or pillarboxed inside the client rectangle remain part of the frame. Target cases with missing, `unknown`, or `full-window` capture space are rejected. Smoke cases may use `client-device-pixels`, `full-window`, or `unknown`, but remain non-gating regardless of that declaration.

`captureSpace` is a coordinate declaration, not provenance or privacy clearance. Target evidence separately requires the Owner-approved source, exact SHA-256, role and BD labels, HUD-hidden confirmation, and privacy clearance. The replay harness does not infer or establish those facts.

Use `-ValidationOnly` (or its `-SchemaOnly` alias) to validate the manifest contract without reading image files, building, or launching VisionProbe:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-vision-replay.ps1 `
  -Manifest <manifest.json> `
  -ValidationOnly
```

Successful validation-only output is labeled `Mode: schema-only`, `ReplayEvidenceGenerated: false`, and `EvidenceStatus: not-replay-evidence`. For target manifests, `Summary.RequiredResolutionRepresentativesPresent: true` means only that exact `1920x1080` and `1280x960` representatives are declared. It does not mean the V10-E01 twelve-case target gate is satisfied. Validation-only execution is schema validation only, does not prove replay behavior or target evidence, and never writes an `-Output` replay report.

Real screenshots and executable manifests stay under ignored `.artifacts/v0.2-real-frames/`. The tracked sample describes the contract only; its placeholder image is intentionally absent.

Exit codes are `0` for a passing replay, `2` for usage or manifest errors, `5` for replay assertion failures, and `6` for build, launch, or report-write failures.

## Client-Surface Capture Receipts

The existing explicit capture commands save a PNG at the requested local path and emit one JSON receipt on standard output after the saved file has been reopened and verified:

```powershell
D4Hub.VisionProbe --capture-title <window-title> <output.png>
D4Hub.VisionProbe --capture-screen-title <window-title> <output.png>
```

Both commands capture the matched window's client rectangle in physical device pixels. `--capture-title` uses `captureMethod: print-window-client-only`; `--capture-screen-title` uses `captureMethod: client-rect-screen-copy`. A successful receipt has exactly these fields:

- `schemaVersion`: integer `1`;
- `mode`: `client-surface-capture`;
- `captureSpace`: `client-device-pixels`;
- `captureMethod`: one of the two method values above;
- `width` and `height`: the reopened PNG's IHDR dimensions;
- `byteLength`: the reopened file length;
- `sha256`: the reopened file's uppercase SHA-256;
- `outputPath`: the absolute local PNG path.

The receipt does not contain the matched title, native handle, client origin, desktop position, pixels, or visible screenshot text. It is local integrity and coordinate evidence only: matching an arbitrary title does not establish Diablo IV provenance, Owner provenance, privacy clearance, target labels, target replay success, or candidate acceptance.

Existing overwrite behavior is unchanged: the requested output path is created or replaced by the PNG writer. A missing visible non-minimized window returns nonzero before creating the output. Capture, save, reopen, or integrity-check failures return nonzero and do not emit a success receipt.

The deterministic receipt test uses `D4Hub.GameWindowFixture` plus a repository HUD marker only as temporary visible content. It exercises both capture methods, overwrite behavior, PNG metadata and hash binding, privacy-field exclusion, missing-window failures, write failure, and `finally` cleanup without Diablo IV, `.artifacts`, network, or external capture tools:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\vision-replay\verify-client-capture-receipt.ps1
```
