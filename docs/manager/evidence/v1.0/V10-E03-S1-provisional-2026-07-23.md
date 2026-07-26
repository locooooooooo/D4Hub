# V10-E03-S1 Client-Surface Capture Receipt

Date: 2026-07-23 (Asia/Shanghai)

Status: **PROVISIONAL - IMPLEMENTED AND WORKER-TESTED; MANAGER VERIFICATION PENDING**

PKR Task: `task_d4f87aa873cc20099f22587e29bf6ee9`, revision `1`

This delivery implements only the active V10-E03-S1 diagnostic-tool card. It preserves the existing explicit capture command names, client-rectangle capture paths, and overwrite behavior while replacing the prior path-only success output with a privacy-minimized JSON receipt.

## Complete Changed Files

1. `tests/D4Hub.VisionProbe/Program.cs`
   - emits a versioned capture receipt for both existing capture commands;
   - distinguishes PrintWindow client-only capture from client-rectangle screen copy;
   - reopens and verifies the saved PNG before success output;
   - preserves missing-window exit `3` and uses exit `5` for capture, save, reopen, or receipt-integrity failure.
2. `tests/vision-replay/verify-client-capture-receipt.ps1` (new)
   - provides deterministic Windows fixture coverage for both methods, receipt integrity, privacy fields, overwrite behavior, failure paths, and cleanup.
3. `scripts/verify.ps1`
   - invokes the new receipt contract test after the existing coordinate contract test.
4. `tests/vision-replay/README.md`
   - documents the command-compatible receipt schema, methods, local coordinate boundary, failures, overwrite behavior, and evidence limits.
5. `docs/manager/evidence/v1.0/V10-E03-S1-provisional-2026-07-23.md` (new)
   - this provisional evidence report.

No other source, test, script, documentation, library, PKR, `.tools`, `.artifacts`, publish, package, image, technical-reference, product, detector, fingerprint, algorithm, or threshold file was changed by this slice. Required builds refreshed only ordinary ignored build outputs. No capture PNG or temporary fixture directory was retained.

## Receipt Contract

The command names and their existing capture behavior remain:

| Command | Existing capture path | Receipt `captureMethod` |
| --- | --- | --- |
| `--capture-title` | `PrintWindow` with client-only/full-content flags against the matched `GameClientWindow` | `print-window-client-only` |
| `--capture-screen-title` | screen copy of the matched `GameClientWindow` client rectangle | `client-rect-screen-copy` |

Both successful commands emit exactly nine JSON properties:

| Property | Value/binding |
| --- | --- |
| `schemaVersion` | integer `1` |
| `mode` | `client-surface-capture` |
| `captureSpace` | `client-device-pixels` |
| `captureMethod` | one of the two method values above |
| `width` | reopened PNG width |
| `height` | reopened PNG height |
| `byteLength` | reopened file length |
| `sha256` | reopened file SHA-256 as uppercase hexadecimal |
| `outputPath` | absolute local PNG path |

The success sequence is capture, save using the existing `File.Create` overwrite path, reopen/decode, measure bytes and hash, compare saved dimensions with the captured client surface, build the receipt, reopen and hash a second time, compare every receipt-bound value, then serialize to standard output. Serialization is also inside the failure boundary, so no partial success JSON is written if it fails.

The receipt has no title, window-title, native-handle, left/top, x/y, client-origin, desktop-position, pixel, screenshot-content, account/player, or visible-text field. It adds no upload, clipboard, telemetry, network, or input behavior.

## Deterministic Test Matrix

The test uses only `D4Hub.GameWindowFixture` and the existing non-sensitive repository HUD marker `src/D4Hub.App/Assets/Hud/Source/marker-masterworked-source.png`. The fixture is a temporary visible, topmost WPF window. Outputs use a GUID-named system temporary directory.

| Case | Expected | Worker result |
| --- | --- | --- |
| Release build for fixture and Probe | exit `0` | pass |
| `--capture-title` against visible fixture | exit `0`; `print-window-client-only` receipt | pass |
| `--capture-screen-title` against visible fixture | exit `0`; `client-rect-screen-copy` receipt | pass |
| common coordinate boundary | both methods report the same positive client width/height and `client-device-pixels` | pass |
| existing output file | both commands replace placeholder bytes with a valid PNG | pass |
| receipt schema | exactly the nine documented fields and stable values | pass |
| saved artifact binding | PNG signature/IHDR, dimensions, byte length, SHA-256, and absolute output path all match receipt | pass |
| privacy minimization | no title value or forbidden title/handle/position/pixel/content property | pass |
| missing window, `--capture-title` | exit `3`; empty standard output; no PNG | pass |
| missing window, `--capture-screen-title` | exit `3`; empty standard output; no PNG | pass |
| invalid output parent after successful screen capture | exit `5`; empty standard output; no PNG | pass |
| cleanup | fixture exits; output files and GUID temporary directory removed in `finally` | pass |

After focused and canonical runs, the independent cleanup check found `0` `d4hub-v10-e03-*` temporary entries and `0` `D4Hub.GameWindowFixture` processes.

## Commands And Results

All commands were run from `E:\D4Hub` against the final worker tree.

1. Focused receipt contract:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\vision-replay\verify-client-capture-receipt.ps1
```

- Exit: `0`.
- Output: `PASS VisionProbe client capture receipt contract`.
- Both PrintWindow client-only and client-rectangle screen-copy were available and passed on this host.

2. Canonical verifier:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

- Exit: `0`.
- Release build: `0` warnings, `0` errors.
- Acceptance executable: `PASS all 44 acceptance checks`.
- Existing replay coordinate contract: pass.
- New client capture receipt contract: pass.
- Forbidden capability scan: pass.
- Final: `PASS repository verification`.

3. Final whitespace and fence checks:

```powershell
git diff --check
```

- Exit: `0`; output: none.
- Direct trailing-whitespace scan covered all five files because the current repository product tree is otherwise untracked: `0` matches.
- Targeted status contained only the five allowed files for this slice.

## Host Difference And Test Iterations

The current Codex process environment had `SystemRoot=C:\Windows` but no `WINDIR`. The first fixture execution after the PowerShell 5.1 parser fix failed before creating a window with WPF FontCache `UriFormatException`; neither capture command ran in that attempt. The deterministic test now supplies `WINDIR=$env:SystemRoot` only to the fixture child process when the inherited value is missing. It does not mutate the parent/global environment or product code.

After that child-only environment normalization, both capture methods passed repeatedly. PrintWindow is therefore available on this host; there is no PrintWindow skip, fallback, weakened assertion, or platform exemption. The first draft also used a multiline `while` expression that Windows PowerShell 5.1 rejected; the final test uses a compatible single-line condition.

## Implementation File SHA-256

- `AEF1F99FD3ED497158B97C523FD72ACCDD47BD8D951845EF1802C9769BD3BB2C` `tests/D4Hub.VisionProbe/Program.cs`
- `694F78FFF51B7D580CE7BDB56926F5705A388F786CA9E1741165A59D1A3DA70A` `tests/vision-replay/verify-client-capture-receipt.ps1`
- `1E2EEC55122160C168077C2195C44101274F95BE3EB0EC68069E8406FC433FD4` `scripts/verify.ps1`
- `9ED686145081BDAC91FA7AB999AD0C1BA48BC87481EA7E9775425FEA26F36F35` `tests/vision-replay/README.md`

This report does not embed its own unstable hash.

## Residual Boundary

- A receipt binds a locally saved PNG to a capture method, client-device-pixel dimensions, byte length, hash, and path. It does not establish that an arbitrary matched title belongs to Diablo IV.
- The receipt does not establish Owner-approved provenance, privacy clearance, HUD-hidden status, target role labels, BD identity, repeated-pair binding, panel-absent status, target replay success, or candidate acceptance.
- PrintWindow success here proves only this fixture/host execution. Other Windows compositions, protected surfaces, minimized windows, or applications may reject or render differently; failures remain explicit and do not fall back silently.
- Screen copy requires the client rectangle to be visible and unobscured on the desktop; the receipt does not inspect or certify screenshot content.
- Existing overwrite behavior remains destructive at the explicit output path. This task did not add atomic replacement, no-clobber policy, or recovery for a failed overwrite.
- The test uses a WPF fixture and repository marker, not Diablo IV, `.artifacts`, a physical-device Owner capture, network, or an external capture tool.
- V10-E01 remains blocked on its separate twelve-case exact-resolution target corpus and Owner-approved evidence.

## Status Classification

- requirements defined: `yes`
- implemented: `yes, provisional worker delivery within the diagnostic-tool fence`
- tested: `yes, focused and canonical local tests`
- independently verified: `no; manager review and rerun pending`
- Owner accepted: `no`
- target evidence: `no`
- candidate: `no`
- candidate SHA: `none`
- committed: `no`
- pushed: `no`
- packaged/signed/uploaded/hosted/published: `no`

No Assignment, Lease, Verification, Acceptance, commit, push, package, signature, upload, hosting, publication, or PKR/control-board mutation was created by this worker.
