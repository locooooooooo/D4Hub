# V10-E02-S2 Mixed-Resolution Exploratory Replay

Date: 2026-07-23 (Asia/Shanghai)

Status: **PROVISIONAL - REPLAY FAILED AS DECLARED; EXPLORATORY SMOKE ONLY**

PKR Task: `task_3a7ebeb946cfdce1df842b83b5ef123b`, revision `1`

This worker evidence is provisional until the manager inspects the complete file set and independently reruns the commands below. It records the current detector's behavior on the 17 existing native-size PNGs. It does not change the detector, threshold, input files, or target evidence requirements.

## Allowed Write Inventory

The complete task-authorized write set is:

1. `.artifacts/v0.2-real-frames/target/exploratory-smoke-manifest.json` - local ignored manifest.
2. `.artifacts/v0.2-real-frames/target/exploratory-smoke-report.json` - local ignored replay output.
3. `docs/manager/evidence/v1.0/V10-E02-S2-provisional-2026-07-23.md` - this tracked provisional report.

No control-board, source, test, script, library, PKR, tool, package, publish, reference, threshold, algorithm, or image file was changed by this slice. The required build and canonical verification commands may refresh their ordinary ignored `bin`/`obj` outputs; those are not delivery artifacts.

## Manifest And Coverage

- Manifest schema: `1`.
- Evidence class: `smoke`.
- Cases: `17`.
- Original PNGs: `17`.
- Unique opaque IDs: `17`.
- Unique manifest paths: `17`.
- Missing paths: `0`.
- Uncovered PNGs: `0`.
- Extra paths: `0`.
- Coverage: **17/17 originals, each exactly once**.
- Every case declares its PNG-header native dimensions, `captureSpace: unknown`, `panelThreshold: 0.55`, and `expectedDecision: accepted`.
- Filenames are UUID-like and are mapped to opaque IDs only inside the ignored local manifest. This tracked report does not reproduce file paths or visible personal, account, game-value, or currency text.

## Read-Only Inventory And Actual Results

The byte counts, dimensions, and SHA-256 values below were independently recomputed from the original files before replay. After replay, all 17 were recomputed and matched both this inventory and the prior V10-E01 intake inventory. The actual dimensions also matched the manifest and replay output for every case.

| Opaque case | PNG bytes | Native dimensions | SHA-256 | Confidence | Actual decision | Assertion result |
| --- | ---: | ---: | --- | ---: | --- | --- |
| `case-001` | 4,652,272 | 2560x1400 | `E36E55D21D9A405A242FEBE9DEDC41222C1CBD5C54AC94A98AE4C41466E6B714` | 0.47561750837884553 | rejected | failed |
| `case-002` | 2,385,429 | 1602x1056 | `DDE45C29F0B0AC9D6FD8DD0268EA9668E08093ED88F827B4A7C24D921450E40F` | 0.57004691327362267 | accepted | passed |
| `case-003` | 1,610,523 | 1362x800 | `298417D4AADD91957495AD10EF2DA03FD90E1DF5043BD758CB30A3FA2EB0A1B6` | 0.50802477289215775 | rejected | failed |
| `case-004` | 1,615,762 | 1368x800 | `88630F1CCBF103A8CC71D28AD37BE0BB4DDDE0E1FC1BF319366F41741A302DC1` | 0.56445300438873058 | accepted | passed |
| `case-005` | 1,603,928 | 1282x832 | `D094C09494C44FD10CCCB150A6B598BC4C86AE9EB94582C7A9F5732F4EBF2DC5` | 0.3825883649624387 | rejected | failed |
| `case-006` | 1,582,758 | 1282x992 | `38D47A12B61D673C0954BA4B7DE252754CE46E894A5246312EC1FDFEA385A316` | 0.42671807272507017 | rejected | failed |
| `case-007` | 1,073,880 | 1026x800 | `E899ADB3986A798386B9480EB8530ABDCCD9BF87B732B9558F164EE5BD81E8BC` | 0.45470736322543737 | rejected | failed |
| `case-008` | 1,920,847 | 1442x1112 | `C6A83FBA9BD99482E5265E5BC59809FE57C05D550EBBB7BB2B8A2A6A5B53F8CA` | 0.69863595305868742 | accepted | passed |
| `case-009` | 716,177 | 802x632 | `24F472F9D2168A300547BC576167DB408ECD95315AC730DDBA6C4DBD4F64572D` | 0.74915914546683848 | accepted | passed |
| `case-010` | 1,589,617 | 1282x1056 | `5A88603956DD297E7F2B8E7B8F848F6680EBA738741D853565377CB77ED9DEF5` | 0.36528280930487866 | rejected | failed |
| `case-011` | 2,999,505 | 1922x1112 | `51B3E494346241291C1134EDAD65B8D94F8A08E667C9D879748706FFE0B3F164` | 0.45892602219878242 | rejected | failed |
| `case-012` | 1,944,499 | 1442x932 | `485C31D09EDB2671633686C2C3861317816FF91961B0B33149CCA7088291A77E` | 0.54663495497622294 | rejected | failed |
| `case-013` | 1,474,926 | 1282x752 | `D5448EDCDAE451AF902388A9CCEC4595CAEDCD9C9F552874B3191A6C1AD9BD0A` | 0.49565714236518882 | rejected | failed |
| `case-014` | 2,091,967 | 1602x932 | `12D2BB362068F6F0D56B77CD1799D88371F94939912651ECECF6382AD3933AA5` | 0.71597702898100035 | accepted | passed |
| `case-015` | 1,552,661 | 1282x800 | `6A8D203AD7BBD36A312DC1AB71B58E2105E6A639DD40D1AA6C5AD9D9DFA8D90A` | 0.34922803659416379 | rejected | failed |
| `case-016` | 3,255,403 | 1922x1232 | `B4F62E3855EDF039B505945280444DD009D7BF13BF829E046145C84CA43AA7D0` | 0.73114672053733065 | accepted | passed |
| `case-017` | 1,295,476 | 1178x696 | `FE3FE4AB8A04DE34423292B42B7612E9DA5AF911C3EAF5EEA4FDC90E9B526052` | 0.37242123379352071 | rejected | failed |

Inventory comparison result: prior rows `17`; current PNGs `17`; manifest cases `17`; replay cases `17`; mismatches `0`. Therefore the original PNG dimensions, byte counts, and SHA-256 values were unchanged.

## Replay Outcome

Command:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-vision-replay.ps1 -Manifest .\.artifacts\v0.2-real-frames\target\exploratory-smoke-manifest.json -Output .\.artifacts\v0.2-real-frames\target\exploratory-smoke-report.json
```

- Harness exit: **`5`**, the documented replay assertion-failure exit.
- Total: `17`.
- Actual accepted: `6`.
- Actual rejected: `11`.
- Assertion-passed cases: `6`.
- Assertion-failed cases: `11` (`33` assertion messages total).
- Operational failures: `0`.
- Manifest errors: `0`.
- No `-ValidationOnly` or `-SchemaOnly` flag was used.
- Manifest SHA-256: `776B5801C7074731039550C72549203D2391B55006EE874AEAA73452AA82DDA0`.
- Replay report SHA-256: `B544A20D690298B913BDF27251FD106E7418E4ACE7D695D64DEA5429F6AB5FC4`.

All rejected cases remain in the manifest with the required threshold and expected decision. No threshold was lowered, no assertion was weakened, and no case was deleted.

## Failed Cases

Each failure below has the same three harness reasons: Probe returned exit `4` instead of expected exit `0`; actual decision was `rejected` instead of expected `accepted`; and the accepted-decision assertion required a complete fingerprint, which a rejected result did not contain.

| Opaque case | Native dimensions | Confidence | Failure reasons |
| --- | ---: | ---: | --- |
| `case-001` | 2560x1400 | 0.47561750837884553 | probe exit 4 vs 0; rejected vs accepted; complete fingerprint absent |
| `case-003` | 1362x800 | 0.50802477289215775 | probe exit 4 vs 0; rejected vs accepted; complete fingerprint absent |
| `case-005` | 1282x832 | 0.3825883649624387 | probe exit 4 vs 0; rejected vs accepted; complete fingerprint absent |
| `case-006` | 1282x992 | 0.42671807272507017 | probe exit 4 vs 0; rejected vs accepted; complete fingerprint absent |
| `case-007` | 1026x800 | 0.45470736322543737 | probe exit 4 vs 0; rejected vs accepted; complete fingerprint absent |
| `case-010` | 1282x1056 | 0.36528280930487866 | probe exit 4 vs 0; rejected vs accepted; complete fingerprint absent |
| `case-011` | 1922x1112 | 0.45892602219878242 | probe exit 4 vs 0; rejected vs accepted; complete fingerprint absent |
| `case-012` | 1442x932 | 0.54663495497622294 | probe exit 4 vs 0; rejected vs accepted; complete fingerprint absent |
| `case-013` | 1282x752 | 0.49565714236518882 | probe exit 4 vs 0; rejected vs accepted; complete fingerprint absent |
| `case-015` | 1282x800 | 0.34922803659416379 | probe exit 4 vs 0; rejected vs accepted; complete fingerprint absent |
| `case-017` | 1178x696 | 0.37242123379352071 | probe exit 4 vs 0; rejected vs accepted; complete fingerprint absent |

The replay therefore exposes mixed-resolution detector sensitivity at the unchanged `0.55` threshold. It does not justify a threshold or algorithm change within this evidence-only task.

## Canonical Verification

Command:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

- Exit: `0`.
- Release build: `0` warnings, `0` errors.
- Acceptance executable: `PASS all 44 acceptance checks`.
- Replay coordinate contract: `PASS vision replay client-surface coordinate contract`.
- Final: `PASS repository verification`.

- `git diff --check`: exit `0`, output none.
- Direct trailing-whitespace scan of this untracked provisional report: `0` matches.

## Evidence Boundary And Residual Risks

- This is `smoke` evidence with `captureSpace: unknown`; it is permanently exploratory and non-gating.
- None of the 17 cases is target evidence. The native sizes include neither exact `1920x1080` nor exact `1280x960`.
- This replay does not establish capture provenance, client-only coordinates, HUD-hidden status, privacy clearance, BD identity, known/unknown role, repeated-pair binding, or panel-absent evidence.
- The `6/17` accepted result is panel-detection smoke behavior only. It does not prove BD fingerprint stability or BD discrimination, and the `11/17` rejected result leaves mixed-resolution panel detection unreliable at the current threshold for this corpus.
- The successful canonical verifier is deterministic local evidence; it does not override the replay failure or prove real-game, UI/device, soak, clean-machine, reproducible-build, or production behavior.
- V10-E01 remains blocked on its separate twelve-case exact-resolution target corpus and Owner-approved labels/provenance.
- This report is not independent Verification, Owner acceptance, a candidate, candidate acceptance, packaging, signing, commit, push, upload, hosting, publication, or release authorization. Candidate SHA remains `none`.

## Status Classification

- requirements defined: `yes`
- implemented: `local exploratory evidence generated`
- tested: `yes, with replay assertion failure preserved`
- independently verified: `no; manager review and rerun pending`
- Owner accepted: `no`
- target evidence: `no`
- candidate: `no`
- candidate SHA: `none`
- committed: `no`
- pushed: `no`
- packaged/signed/uploaded/hosted/published: `no`
