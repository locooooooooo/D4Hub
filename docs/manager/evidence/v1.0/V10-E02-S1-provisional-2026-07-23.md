# V10-E02-S1 Client-Surface Evidence Contract - Provisional Worker Report

Date: 2026-07-23 (Asia/Shanghai)

PKR Task: `task_270af1018119e61c03fefe5ca07cef68`, revision `1`

This delivery is provisional until the manager inspects the complete file set and independently reruns the commands below. It implements only the requirements card in `docs/manager/2026-07-23-v1.0-release-readiness-control.md`; it does not create target evidence, a candidate, or release authorization.

## Outcome

The offline replay manifest now makes the existing D4HUD-D002 coordinate boundary machine-checkable:

- a target case must explicitly declare `captureSpace: client-device-pixels`;
- `expectedWidth` and `expectedHeight` remain the raw PNG dimensions in that declared coordinate space, with no crop, scale, DPI conversion, or normalization;
- missing, `unknown`, or `full-window` target capture space exits `2` during manifest parsing, before image access, build, or VisionProbe launch;
- a target manifest still requires exact `1920x1080` and `1280x960` representatives, so near-size inputs do not bypass V10-E01;
- smoke cases may explicitly declare `unknown` or `full-window` and remain non-gating;
- `-ValidationOnly` and its `-SchemaOnly` alias validate schema/contract without reading images or launching the probe;
- successful validation-only output states `Mode: schema-only`, `ReplayEvidenceGenerated: false`, and `EvidenceStatus: not-replay-evidence`, and never writes an `-Output` replay report.
- `Summary.RequiredResolutionRepresentativesPresent: true` means only that the two required resolution representatives are declared; it never claims that the V10-E01 twelve-case target gate is satisfied.

`captureSpace` is only a coordinate declaration. It does not establish Owner-approved provenance, SHA-256 binding, BD/role labels, HUD-hidden status, or privacy clearance.

Manager-review correction: the initial worker draft called the two-resolution summary `TargetResolutionGateSatisfied`, which could be confused with the V10-E01 twelve-case gate. The field was replaced with the narrower `RequiredResolutionRepresentativesPresent`; the deterministic test now requires the narrow field and rejects reintroduction of the old field. No target-gate logic changed.

## Complete Changed-File Diff

- `scripts/verify-vision-replay.ps1`
  - added `-ValidationOnly` with `-SchemaOnly` alias;
  - added the `client-device-pixels`, `full-window`, and `unknown` capture-space enum;
  - requires every target case to use `client-device-pixels`;
  - performs all target coordinate and exact-resolution checks before image existence checks;
  - returns an explicit schema-only/non-evidence JSON result without calling the report writer;
  - reports only `RequiredResolutionRepresentativesPresent` and never exposes the misleading `TargetResolutionGateSatisfied` field;
  - retains normal replay image validation and includes `CaptureSpace` in each replay case report.
- `scripts/verify.ps1`
  - invokes the deterministic client-surface contract test and fails the canonical verifier if that test fails.
- `tests/vision-replay/README.md`
  - documents client-area device pixels, border/chrome exclusion, internal letterbox handling, no post-processing, capture-space values, validation-only semantics, and provenance/privacy limits.
- `tests/vision-replay/manifest.sample.json`
  - explicitly marks the placeholder smoke case as `captureSpace: unknown`; it remains non-gating and its image remains intentionally absent.
- `tests/vision-replay/verify-coordinate-contract.ps1` (new)
  - creates only synthetic manifests under a unique system temporary directory;
  - covers exact target schema validation, missing/unknown/full-window target rejection, near-size rejection, explicit non-client smoke validation, no-report behavior, and normal replay missing-image rejection;
  - removes the temporary directory in `finally` and has no third-party dependency.
- `docs/manager/evidence/v1.0/V10-E02-S1-provisional-2026-07-23.md` (new)
  - this provisional delivery record.

No `src/**`, PKR, `.artifacts`, image, library, tool, publish, reference, or control-board file was modified. No target manifest was created. The 17 exploratory PNGs were not opened, modified, cropped, scaled, re-encoded, renamed, or deleted by this worker.

## Deterministic Acceptance Matrix

| Case | Invocation path | Expected | Worker result |
| --- | --- | --- | --- |
| target, exact `1920x1080` and `1280x960`, both `client-device-pixels`, nonexistent placeholder images | validation-only | exit `0`; schema-only disclaimer; representatives-present field true; old target-gate field absent; no report/image/probe access | pass |
| target with missing capture space | normal replay entry | exit `2` before image/probe; no report | pass |
| target with `unknown` capture space | normal replay entry | exit `2` before image/probe; no report | pass |
| target with `full-window` capture space | normal replay entry | exit `2` before image/probe; no report | pass |
| target containing only `1922x1112` and `1282x992` | validation-only | exit `2` at existing exact-resolution gate | pass |
| smoke with explicit `unknown` and `full-window` cases | `-SchemaOnly` alias | exit `0`; remains smoke; no replay evidence | pass |
| smoke normal replay with absent image | normal replay entry | exit `2` before build/probe | pass |
| validation-only invoked with `-Output` | validation-only | output path remains absent | pass |

## Commands And Results

1. `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\vision-replay\verify-coordinate-contract.ps1`
   - Exit: `0`
   - Output: `PASS vision replay client-surface coordinate contract`
2. `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1`
   - Exit: `0`
   - Release build: `0` warnings, `0` errors
   - Acceptance executable: `PASS all 44 acceptance checks`
   - Contract test: `PASS vision replay client-surface coordinate contract`
   - Final: `PASS repository verification`
3. `git diff --check`
   - Exit: `0`
   - Output: none
4. trailing-whitespace scan over all five implementation/test/documentation files
   - Exit: `0`
   - Matches: none
5. temporary-directory inventory for `d4hub-v10-e02-*` after test completion
   - Exit: `0`
   - Entries: none

## Implementation File SHA-256

- `FCABD91EEF75DB553ECE4AFE43566A5A8EFE6F3F059EE6953F4C1A0D87CDD257` `scripts/verify-vision-replay.ps1`
- `B7746752368C03FBD381F2DDD82261835577A6D182048FB6D0FE644D8E0D94FB` `scripts/verify.ps1`
- `87B17EC38F71A601969BC0C924B5BE880ED90352540AB8E835856C89D3DC5DAD` `tests/vision-replay/README.md`
- `56D9B23DC278692F5F5418CE43F1D96B9E077CDA82BECEE541A1A0B2764F9BA2` `tests/vision-replay/manifest.sample.json`
- `E8A3646E5459CEA1864A3B37D2653C7F6C3F33FE9AC71B35BC6637825EF2924F` `tests/vision-replay/verify-coordinate-contract.ps1`

## Residual Risks And Gates

- These tests use synthetic manifests and intentionally absent placeholder images. They prove contract ordering and labeling only, not detector behavior on real images.
- `RequiredResolutionRepresentativesPresent` proves only two declared dimensions, not the V10-E01 twelve-case evidence matrix or any target acceptance.
- The schema trusts a declared `captureSpace`; it cannot prove that a PNG was actually captured from the client rectangle. Target provenance and privacy gates remain separate Owner evidence.
- V10-E01 remains blocked. This slice does not supply the twelve exact target cases, known BD-A/BD-B repeated pairs, unknown-BD/panel-absent roles, labels, provenance, HUD-hidden confirmation, or privacy clearance.
- The 17 current exploratory PNGs remain smoke/exploratory only and are not target evidence.
- No candidate SHA exists, and no packaging or release action is authorized.

## Status Classification

- requirements defined: `yes`, by the active manager card and existing D4HUD-D002 direction
- implemented: `yes`, provisional worker delivery within the file fence
- tested: `yes`, deterministic worker-local checks above
- independently verified: `no`, manager rerun and diff review pending
- Owner accepted: `no` for this delivery; D4HUD-D002 supplies direction only
- committed: `no`
- pushed: `no`
- packaged: `no`
- signed: `no`
- uploaded/hosted: `no`
- published: `no`
- candidate SHA: `none`

No game-memory access, injection, automated input, macro, evasion, reference asset copying, or new network integration was introduced.
