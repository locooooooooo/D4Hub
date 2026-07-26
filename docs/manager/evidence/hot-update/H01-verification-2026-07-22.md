# H01 Local Update Contract Verification

Verification date: 2026-07-22 (Asia/Shanghai)

Verifier: D4Hub manager, independent of the implementation worker

## Scope

H01 adds only a local JSON manifest and artifact-integrity contract. It does not add network access, download, process replacement, restart, installation, signing, hosting, telemetry, third-party dependencies, upload, release, or player distribution.

## Reviewed Changes

- `src/D4Hub.Core/UpdateManifest.cs`: new strict parser and validator. SHA-256: `e9870454986f8f5d09acfee12262c232657ce8f93d0c16a66f461f17b39c263b`.
- `tests/D4Hub.AcceptanceTests/Program.cs`: eight H01 acceptance checks added. SHA-256 after the change: `bf9d3a94870dcc92533796b59b6dd5781afee99cf31d9c25b07ba22210e82ed4`.
- The pre-dispatch SHA-256 values for `D2CoreBuilds.cs`, `HudModels.cs`, `JsonStateStore.cs`, and `Models.cs` still match exactly. No existing Core implementation file was changed.
- Project files contain no new `PackageReference` or dependency change.

The manager inspected the complete new Core file and all H01 additions in the acceptance runner. An initial review returned two implementation defects and two coverage gaps: definite assignment/string-overload issues, multi-extension Windows device names such as `CON.foo.bar`, nested artifact unknown/duplicate fields, and numeric `1.10.0 > 1.9.9` comparison. The worker revised all four before final verification.

## Independent Commands

1. `dotnet build .\D4Hub.sln -c Release --nologo`
   - Passed with 0 warnings and 0 errors.
2. `dotnet run --project .\tests\D4Hub.AcceptanceTests\D4Hub.AcceptanceTests.csproj -c Release --no-build`
   - Passed all 37 checks, including all eight H01 checks.
3. `powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1`
   - Passed the Release build, all 37 acceptance checks, repository forbidden-game-API scan, whitespace check, and `PASS repository verification`.
4. H01-specific forbidden-capability scan across `UpdateManifest.cs` and both affected project files.
   - No networking, download, process execution/restart, installer, MSIX/Velopack, registry, P/Invoke, telemetry, or package dependency match.
5. `git diff --check`
   - Passed.

## Rejection Evidence

The checked acceptance cases explicitly reject malformed JSON, missing fields, unknown or duplicate root fields, unknown or duplicate artifact fields, unsupported schema, product/channel/architecture mismatch, same or older versions, invalid versions, absolute/traversal/nested/reserved artifact names, missing artifacts, non-positive or mismatched size, and invalid or mismatched SHA-256. A valid local artifact is accepted, and `1.10.0` is proven newer than `1.9.9` without string ordering.

## Truth Classification

- implemented: yes, in the current uncommitted working tree.
- tested: yes, deterministic local contract tests only.
- independently verified: yes, for H01's local-only boundary.
- packaged: no.
- owner accepted: no.
- committed: no product candidate; Git HEAD remains `8e944d23bead51ecc1e2a386b8e088f0ceb0e722`.
- pushed: no evidence and no remote authorization.
- published: no; explicitly deferred.
- PKR: Task `task_800a01630b0e2cdbbf051d269ec1049e` remains Runtime `backlog`. Repo-board verification is not a PKR claim, Assignment, submission, Verification, or Acceptance.

## Residual Risk

H01 accepts only three-part numeric `major.minor.patch`; prerelease and build metadata are outside this slice. The manifest is intentionally smaller than the governed production manifest. SHA-256 proves that a local file matches a named local manifest, not publisher authenticity, manifest authenticity, secure transport, signing, atomic installation, rollback, recovery, or production trust. No package or real updater was exercised.

## Result

H01 is **implemented, tested, and independently verified** within its stated local-only boundary. It is not packaged, Owner accepted, committed, pushed, or published.
