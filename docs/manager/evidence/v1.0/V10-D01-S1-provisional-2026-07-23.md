# V10-D01-S1 Provisional Worker Report

Date: 2026-07-23 (Asia/Shanghai)

Slice: V10-D01-S1 Corrupt/Incomplete Import Preservation

Status: provisional worker delivery. This report is not Owner acceptance, candidate verification, a commit, a push, a package, a signature, or publication.

## Scope

The slice preserves startup recovery for missing or damaged local state while adding an explicit strict import path. Strict import rejects a missing file, malformed JSON, empty/null JSON, unsupported schema, incomplete root model, invalid profile identity, and other incomplete top-level state before the document is accepted. MainViewModel and HudViewModel now validate and persist the imported document before replacing the active in-memory document; import exceptions become visible status text and do not escape the UI entry points.

No network, game-memory access, injection, automated input, macro, evasion, migration beyond schema 1, automatic backup policy, dependency, UI redesign, packaging, signing, hosting, upload, publication, PKR mutation, or reference-asset change was made.

## Changed Files

- `src/D4Hub.Core/JsonStateStore.cs`
  - Added `LoadStrict()` with JSON shape checks, schema-1 enforcement, profile identity checks, and model validation.
  - Kept `Load()` startup fallback behavior unchanged for missing, malformed, and invalid-data recovery.
- `src/D4Hub.App/ViewModels/MainViewModel.cs`
  - Uses `LoadStrict()`, saves the validated import before switching `Document`, and reports failures without escaping.
- `src/D4Hub.App/ViewModels/HudViewModel.cs`
  - Uses `LoadStrict()`, saves before switching `Document`, refreshes imported selection state without a second implicit import save, and reports failures without escaping.
- `tests/D4Hub.AcceptanceTests/Program.cs`
  - Added deterministic startup fallback, strict rejection, valid round-trip, and failed-import byte/SHA preservation checks.
- `docs/manager/evidence/v1.0/V10-D01-S1-provisional-2026-07-23.md`
  - This provisional evidence report.

No other files were intentionally changed. Existing dirty/untracked user work remains untouched.

## Verification Commands And Results

1. `dotnet build .\D4Hub.sln -c Release`
   - Exit `0`; all projects built; `0` warnings; `0` errors.
2. `dotnet run --project .\tests\D4Hub.AcceptanceTests\D4Hub.AcceptanceTests.csproj -c Release --no-build`
   - Exit `0`; all `44/44` acceptance checks passed.
   - New checks cover startup missing/damaged fallback, strict missing/malformed/empty/null/schema/model rejection, valid strict export/import, no temporary files, and failed-import active-state byte/SHA-256 preservation.
3. `powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1`
   - Exit `0`; Release rebuild `0` warnings/`0` errors; `44/44` acceptance checks; `PASS repository verification`.
4. `git diff --check`
   - Exit `0`.
5. Fenced-file trailing-whitespace scan over the four code/test files
   - PASS; no trailing whitespace found. This supplemental scan is recorded because the product tree is currently untracked and therefore invisible to a normal Git diff.
6. Fenced-file forbidden-API scan
   - No new memory access, injection, input automation, macro, upload, or telemetry API was introduced. The pre-existing `System.Net.Http` using in `HudViewModel.cs` remains outside this slice and was not changed.

## Behavior Evidence

- Startup `Load()` still returns a starter document when the state file is missing or contains malformed JSON.
- `LoadStrict()` throws an explicit exception for missing files, parser-invalid JSON, empty files, JSON `null`, unsupported schema, incomplete build/profile shape, and selected-profile model mismatch.
- A valid exported document succeeds through `LoadStrict()` with its build name and selected profile preserved.
- A malformed import attempt leaves the active document reference and contents unchanged, leaves persisted bytes byte-for-byte unchanged, preserves the SHA-256 hash, and leaves no `*.tmp` files.
- UI import entry points catch strict-load and persistence failures and set failure status text; they do not intentionally throw to the UI caller.

## State Classification

| State | Slice classification | Boundary |
| --- | --- | --- |
| implemented | yes, in current working tree | Strict API and both UI import paths are implemented; no candidate freeze. |
| tested | yes, local deterministic | `44/44` acceptance and canonical verifier only. |
| independently verified | no | Manager must inspect the complete worker diff and independently rerun all commands. |
| owner accepted | no | No exact v1.0 SHA or package exists; Owner has not accepted this slice. |
| committed | no | Product tree remains uncommitted working-tree work. |
| pushed | no | No push was authorized or performed. |
| published | no; blocked/deferred | Signing, hosting, upload, distribution, telemetry, and support policy remain separately blocked. |

Candidate SHA: none.

## Residual Risks And Limits

- Acceptance tests exercise `JsonStateStore` directly; the WPF ViewModels are covered by Release compilation and manager source inspection, not by a UI/device run.
- The state store remains a local JSON store. This slice does not prove power-loss durability, filesystem permission recovery, physical-device behavior, offline clean-machine operation, or owner recovery-matrix acceptance.
- V10-E01 remains blocked on the twelve raw target screenshots; this slice does not advance target-resolution or multi-BD recognition evidence.
- V10-D01-S1 remains provisional until the manager independently reruns the commands, checks every changed line and file fence, and records the result in the control document and authoritative PKR state.
