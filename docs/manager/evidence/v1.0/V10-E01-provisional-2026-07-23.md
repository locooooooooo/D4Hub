# V10-E01 Provisional Worker Report

Date: 2026-07-23 (Asia/Shanghai)

Status: **PROVISIONAL - BLOCKED ON OWNER INPUT**

This worker report is not independent Verification, Owner acceptance, a candidate freeze, or release authorization. The manager must inspect this report and rerun the relevant checks before recording any manager conclusion.

## Scope And Fence

Active slice: `V10-E01 real-frame evidence and replay gate`.

Read-only inspection was limited to the existing manager control/evidence documents, product source, tests, scripts, library, and `.artifacts/v0.2-real-frames/**`. The only tracked-intent write is this report. No source, test, script, library, PKR, tooling, package, artifact, workflow, reference-project, or publication file was changed.

## Provisional Outcome

The required target corpus does not exist. `.artifacts/v0.2-real-frames/target/` is absent, so there are zero of the required twelve raw target PNGs and no target manifest. A target replay was therefore not run: there is no truthful input on which to measure repeated-frame stability, distinguish BD-A from BD-B, or test unknown-BD and panel-absent rejection.

The only PNG under `.artifacts/v0.2-real-frames/**` is a non-gating 1404x841 smoke copy. Its manifest explicitly labels it `evidenceClass: smoke`; existing reports show a harness pass, but neither the manifest nor report establishes Owner-approved live-game provenance. Prior v0.2 manager evidence further classifies it as a copied game-content/HUD preview, not a raw target-resolution capture.

Consequently V10-E01, V02-S3, and the v0.2 recognition evidence prerequisite remain open. Candidate freeze, packaging, candidate SHA assignment, and later v1.0 gates must not begin on this evidence.

## Corpus Inventory

Target directory inventory:

- `.artifacts/v0.2-real-frames/target/`: absent.
- Raw 1920x1080 target inputs: `0/6`.
- Raw 1280x960 target inputs: `0/6`.
- Total required target inputs: `0/12`.
- Target manifest: absent.
- Target replay report: absent.

PNG inventory within the worker's permitted artifact subtree:

| File | Bytes | Dimensions | SHA-256 | Source annotation | Evidence class |
| --- | ---: | ---: | --- | --- | --- |
| `.artifacts/v0.2-real-frames/中文截图.png` | 1,975,010 | 1404x841 | `D04C6F5035ADED378C36707581CE5CC62D420364AB46353FFF1D27955CE3994F` | Referenced by `utf8-manifest.json` as the UTF-8 path replay case; no Owner-approved raw-capture provenance in the manifest/report. Prior v0.2 evidence identifies the underlying content as a copied game-content/HUD preview. | `smoke`; non-gating |

Other files in the subtree are harness manifests/reports. `smoke-manifest.json`, `utf8-manifest.json`, and their reports exercise the 1404x841 smoke path. `assertion-failure-manifest.json`, `invalid-target-manifest.json`, and `object-cases-invalid-manifest.json` are deliberate contract-negative inputs; they are not target screenshots or target evidence.

## Commands And Results

All commands were run from `E:\D4Hub`.

1. Target existence and file inventory:

```powershell
if (Test-Path -LiteralPath .artifacts\v0.2-real-frames\target -PathType Container) {
  Get-ChildItem -LiteralPath .artifacts\v0.2-real-frames\target -File -Recurse
} else {
  'TARGET_DIRECTORY_ABSENT'
}
```

Result: `TARGET_DIRECTORY_ABSENT`.

2. PNG dimensions, bytes, and hashes:

```powershell
Get-ChildItem -LiteralPath .artifacts\v0.2-real-frames -Recurse -File -Filter *.png |
  ForEach-Object {
    $sha = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    Add-Type -AssemblyName System.Drawing
    $image = [System.Drawing.Image]::FromFile($_.FullName)
    try {
      [pscustomobject]@{
        Path = $_.FullName
        Width = $image.Width
        Height = $image.Height
        Bytes = $_.Length
        SHA256 = $sha
      }
    } finally {
      $image.Dispose()
    }
  }
```

Result: one PNG, exactly as recorded in the inventory table. No 1920x1080 or 1280x960 PNG exists in the permitted target artifact subtree.

3. Target manifest invocation:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-vision-replay.ps1 `
  -Manifest .\.artifacts\v0.2-real-frames\target\manifest.json `
  -Output .\.artifacts\v0.2-real-frames\target\missing-report.json `
  -NoBuild
```

Result: exit `2`, `Manifest error: Manifest not found: E:\D4Hub\.artifacts\v0.2-real-frames\target\manifest.json`. The script exited during manifest validation and did not create the requested output path.

4. Canonical local baseline:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

Result: exit `0`; Release build succeeded with zero warnings and zero errors; all `37/37` deterministic acceptance checks passed; safety scan and `git diff --check` completed; final line `PASS repository verification`.

This baseline is tested local behavior only. It is not target-frame evidence and does not close V10-E01.

## Replay Decision Matrix

| Required observation | Current result | Reason |
| --- | --- | --- |
| 1920x1080 BD-A repeated acceptance | not tested | both raw frames absent |
| 1920x1080 BD-B repeated acceptance | not tested | both raw frames absent |
| 1920x1080 unknown-BD rejection | not tested | raw frame absent |
| 1920x1080 panel-absent rejection | not tested | raw frame absent |
| 1280x960 BD-A repeated acceptance | not tested | both raw frames absent |
| 1280x960 BD-B repeated acceptance | not tested | both raw frames absent |
| 1280x960 unknown-BD rejection | not tested | raw frame absent |
| 1280x960 panel-absent rejection | not tested | raw frame absent |
| within-BD fingerprint stability | not computable | no repeated target captures |
| cross-BD distinction | not computable | no target BD-A/BD-B captures |

The existing 1404x841 smoke acceptance must not be copied into any row above.

## Exact Owner Input Required

Owner must supply or explicitly approve the provenance of these twelve raw client-surface PNGs under `.artifacts/v0.2-real-frames/target/`:

| Resolution | Suggested case ID | Required content |
| --- | --- | --- |
| 1920x1080 | `1920x1080-bd-a-open-01` | known BD-A, character panel open, capture 1 |
| 1920x1080 | `1920x1080-bd-a-open-02` | same BD-A, character panel open, capture 2 |
| 1920x1080 | `1920x1080-bd-b-open-01` | known BD-B, character panel open, capture 1 |
| 1920x1080 | `1920x1080-bd-b-open-02` | same BD-B, character panel open, capture 2 |
| 1920x1080 | `1920x1080-unknown-open-01` | unregistered/unknown BD, character panel open |
| 1920x1080 | `1920x1080-panel-absent-01` | character panel absent |
| 1280x960 | `1280x960-bd-a-open-01` | known BD-A, character panel open, capture 1 |
| 1280x960 | `1280x960-bd-a-open-02` | same BD-A, character panel open, capture 2 |
| 1280x960 | `1280x960-bd-b-open-01` | known BD-B, character panel open, capture 1 |
| 1280x960 | `1280x960-bd-b-open-02` | same BD-B, character panel open, capture 2 |
| 1280x960 | `1280x960-unknown-open-01` | unregistered/unknown BD, character panel open |
| 1280x960 | `1280x960-panel-absent-01` | character panel absent |

For every capture:

- exact client dimensions must match the named resolution;
- D4Hub HUD must be hidden;
- no crop, resize, compositing, or re-encoding is allowed;
- UI scale must remain stable within each repeated pair;
- the skill bar must be visible;
- positive captures must include a short external mapping to their BD label;
- account and chat information must be hidden before capture;
- Owner must provide or approve provenance for the exact file hashes used by the replay.

The target manifest must use schema version `1`, `evidenceClass: target`, unique case IDs, actual image-relative paths, exact expected dimensions, an explicit panel threshold, expected accepted/rejected decisions, and reviewed panel bounds where applicable. The manifest contract's two-resolution minimum does not by itself prove the required twelve-case composition, so the manager must inspect all labels and hashes before replay.

## Failure Criteria

V10-E01 fails or remains blocked if any of the following occurs:

- any of the twelve inputs is missing, has uncertain provenance, or has the wrong dimensions;
- a capture is cropped, resized, re-encoded, composited, or includes the D4Hub HUD;
- there is only one known BD, no repeated pair, no unknown-BD negative, or no panel-absent negative at either resolution;
- repeated known-BD frames are not stable enough for the manager-approved rule;
- BD-A and BD-B are not distinguishable under the manager-approved rule;
- an unknown BD or panel-absent frame is accepted;
- smoke/fixture evidence is labeled or summarized as target evidence;
- source, tests, scripts, library, artifacts, PKR state, package output, or other fenced files are changed to manufacture a pass.

No numerical stability or cross-BD distance threshold is asserted here because no target measurements exist and the current replay schema only preserves per-case fingerprints. The manager must define and independently apply a falsifiable comparison rule when the real corpus exists; it must not be tuned to make fixtures pass.

## Evidence Classification

| State | V10-E01 worker classification |
| --- | --- |
| implemented | replay harness exists in the working tree; no target evidence implementation is claimed |
| tested | canonical deterministic baseline passed; target replay not run |
| packaged | no |
| independently verified | no; this is a worker provisional report |
| owner accepted | no |
| committed | no product candidate; candidate SHA remains none |
| pushed | no evidence and no authorization |
| published | no; formally blocked/deferred |

## Residual Risks And Unauthorized Boundary

- Real frames may expose detector, bounds, confidence-threshold, or fingerprint defects that synthetic and smoke checks do not cover.
- The current replay script asserts per-case output but does not calculate repeated-frame similarity or cross-BD separation; those comparisons still require an explicit manager review rule or a later narrowly authorized harness change after real measurements exist.
- A filename or manifest label does not prove capture provenance. Owner approval must bind the exact twelve file hashes to the declared capture conditions.
- UI/physical-device behavior, soak/resources, accessibility, corruption recovery, offline operation, clean-machine execution, reproducible builds, exact-SHA packaging, and Owner acceptance remain separate later gates.
- No signing, certificate/key work, hosting, upload, installer/updater, production channel, real-player distribution, telemetry, support/EOL commitment, push, tag, Release creation, or publication is authorized by this slice.

Manager stop condition: independently confirm the `0/12` inventory and preserve V10-E01 as blocked until the exact Owner corpus is available. Do not promote the smoke result, assign a candidate SHA, or start packaging.
