# V02-S2 Independent Verification

Date: 2026-07-22 (Asia/Shanghai)

Task: `task_7596ceb6018615b08e718ff9e1beec6b`

## Scope

- `scripts/verify-vision-replay.ps1`
- `tests/vision-replay/manifest.sample.json`
- `tests/vision-replay/README.md`
- ignored local manifests, copied smoke frame, and reports under `.artifacts/v0.2-real-frames/`
- no VisionProbe, Core, WPF, library, packaging, governance, PKR database, or reference implementation change

## Contract

The PowerShell 5.1 harness reads structured JSON, resolves image paths relative to the manifest, invokes the Release VisionProbe, checks expected dimensions/decision/panel bounds, and preserves confidence, bounds, and fingerprint output per case. Reports preserve `evidenceClass` as exactly `smoke` or `target`.

Target manifests are rejected unless they contain both 1920x1080 and 1280x960 cases. This only validates matrix shape; it does not prove live-game provenance or Owner acceptance.

Exit codes:

- `0`: all replay assertions passed.
- `2`: usage or manifest contract failure.
- `5`: replay assertion failure.
- `6`: build, process, or report-write failure.

Reviewed file hashes:

- `9A2A6AB5F5F711B9A773DE6C2CDA568834BE65E95D7A4EF26F0D54EBAFD5C22A` `scripts/verify-vision-replay.ps1`
- `C3E59E11BE5AAEC725C218477710B122A7EE8D15D3BD7DAA6F558E7D9AEADBB7` `tests/vision-replay/manifest.sample.json`
- `6E24B1E0E26D01988AFC7BC9AF100C0D3975305CE4D39E7C63846A8AA6CCA058` `tests/vision-replay/README.md`

## Manager Review And Revision

The first code review found that PowerShell 5.1 default text decoding could corrupt UTF-8 Chinese paths, and a single JSON object in `cases` was accepted despite the array contract. The worker revised the harness to read UTF-8 explicitly, require `System.Array`, and enforce case-sensitive evidence/decision enum values.

## Independent Results

Passing smoke manifest:

- exit `0`; `EvidenceClass=smoke`; overall pass.
- one 1404x841 local game-content/HUD preview whose live-game provenance is not established.
- panel confidence `0.8184222027972028`; complete fingerprint preserved.

UTF-8 Chinese ID and image path:

- exit `0`; report preserved case ID `中文路径回放` and passed.

Intentional dimension mismatch:

- exit `5`; overall fail; one case assertion failure recorded.

Target manifest missing one required resolution:

- exit `2`; error named the 1920x1080 and 1280x960 requirement.

Single object instead of a cases array:

- exit `2`; non-empty array contract enforced.

`powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1`

- PASS; Release build, 29/29 acceptance checks, safety scan, and repository verification.

Additional checks:

- new tracked-intent files have no whitespace errors under no-index checking.
- local replay manifests, reports, and copied Chinese-path smoke image are excluded by `.gitignore` through `.artifacts/`.

## Classification

- implemented: yes, current working tree.
- tested: yes, harness contract and non-gating local smoke.
- packaged: no.
- independently verified: yes, for V02-S2 harness behavior only.
- owner accepted: no.
- committed: no.
- pushed: no.
- published: no.

## Residual Risk

There are no Owner-confirmed raw target-resolution screenshots, multiple-BD captures, negative live frames, or physical-device evidence. The harness is ready to consume those inputs, but its passing smoke report cannot close V02-S3 or v0.2.
