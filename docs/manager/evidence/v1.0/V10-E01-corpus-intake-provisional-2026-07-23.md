# V10-E01 Corpus Intake Audit

Date: 2026-07-23 (Asia/Shanghai)

Status: **PROVISIONAL - EXPLORATORY CORPUS ONLY; TARGET GATE BLOCKED**

This is a worker intake report, not independent Verification, target replay acceptance, Owner acceptance, candidate freeze, packaging, or release authorization. It records visible pixels and file metadata only. A filename, screenshot appearance, or repeated visual layout does not prove capture provenance or BD identity.

## Scope And Safety Fence

The worker read the latest V10-E01 Corpus Intake Audit card, replay contract/docs, manager evidence, and the 17 existing PNGs under `.artifacts/v0.2-real-frames/target/`. Each PNG was opened read-only with the image viewer for visual inspection. No image was modified, cropped, resized, re-encoded, renamed, deleted, or used to create a derivative. No `manifest.json` was created or changed, and no target replay or target acceptance was run.

The only write in this slice is this report:
`docs/manager/evidence/v1.0/V10-E01-corpus-intake-provisional-2026-07-23.md`.

## Exact Gate Counts

- PNG files inventoried: **17/17**.
- Exact `1920x1080` PNGs: **0**.
- Exact `1280x960` PNGs: **0**.
- Exact target-resolution corpus: **0/12** required cases.
- Manifest present under `.artifacts/v0.2-real-frames/target/`: **no** (`manifest.json` absent).
- Target replay/acceptance: **not run**. There is no manifest and no exact target-resolution input.
- Exploratory/smoke usefulness: **all 17 are visual exploratory material only**. They may help a later Owner prepare labels or inspect detector behavior after a separately authorized manifest, but they cannot close the target gate.

Near-size files such as `1922x1112`, `1922x1232`, and `1282x800` remain non-target. Approximate dimensions are not promoted to `1920x1080` or `1280x960`.

## Visual Classification Rules

- `full surface: yes` means the image visibly contains the complete game/client window surface, including its title bar and any black letterbox bars visible in the file. It does not prove an unmodified capture or provenance.
- `D4Hub HUD: uncertain` is used throughout because colored/purple-framed symbols and item-slot markers are visible, but pixels alone do not establish whether those marks are D4Hub HUD or game-native UI. No image is called HUD-absent or HUD-present without a trusted visual reference.
- `character panel: open/visible` and `skill bar: visible` are direct observations from the pixels.
- `repeat-pair candidate` means the visible equipment/stat layout appears compatible with a repeated capture of the same on-screen setup. It is not a BD label.
- `BD identity: unproven` applies to every file. There is no Owner mapping, manifest label, or pixel-only basis for BD-A/BD-B.
- `unknown-BD candidate` and `panel-absent candidate` are absent: every inspected image shows the character panel open.
- Privacy is described only by category. No account names, chat text, or player identifiers are transcribed here.

## Per-Image Inventory And Visual Review

| File | Bytes | Exact dimensions | SHA-256 | Full client surface | D4Hub HUD | Character panel | Skill bar | Privacy exposure | Role candidates visible only from pixels |
| --- | ---: | ---: | --- | --- | --- | --- | --- | --- | --- |
| `0827512d-1556-45aa-83b1-39b949272d64.png` | 4,652,272 | 2560x1400 | `E36E55D21D9A405A242FEBE9DEDC41222C1CBD5C54AC94A98AE4C41466E6B714` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `0f1f5850-d8c7-4230-ae77-91b310b003f3.png` | 2,385,429 | 1602x1056 | `DDE45C29F0B0AC9D6FD8DD0268EA9668E08093ED88F827B4A7C24D921450E40F` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `15aec7ef-1fab-4947-8f25-814a85b34919.png` | 1,610,523 | 1362x800 | `298417D4AADD91957495AD10EF2DA03FD90E1DF5043BD758CB30A3FA2EB0A1B6` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `1c2c0eb3-ea15-4650-846c-425883342a03.png` | 1,615,762 | 1368x800 | `88630F1CCBF103A8CC71D28AD37BE0BB4DDDE0E1FC1BF319366F41741A302DC1` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `22037c92-58b3-4f29-a52b-70720371b6b8.png` | 1,603,928 | 1282x832 | `D094C09494C44FD10CCCB150A6B598BC4C86AE9EB94582C7A9F5732F4EBF2DC5` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `2eb9747a-4cb6-4264-930c-af2e2806d417.png` | 1,582,758 | 1282x992 | `38D47A12B61D673C0954BA4B7DE252754CE46E894A5246312EC1FDFEA385A316` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `6c2ebddf-3b15-4aeb-9e4b-f0f6b0996997.png` | 1,073,880 | 1026x800 | `E899ADB3986A798386B9480EB8530ABDCCD9BF87B732B9558F164EE5BD81E8BC` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `744c4305-f614-415c-9c6a-9c3f4c45a173.png` | 1,920,847 | 1442x1112 | `C6A83FBA9BD99482E5265E5BC59809FE57C05D550EBBB7BB2B8A2A6A5B53F8CA` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `88503b50-8e0c-4e25-9ce6-3a6de154b616.png` | 716,177 | 802x632 | `24F472F9D2168A300547BC576167DB408ECD95315AC730DDBA6C4DBD4F64572D` | yes | uncertain | open/visible | visible | multiple player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `9019b1b6-4614-4de2-999d-21fac1d68d61.png` | 1,589,617 | 1282x1056 | `5A88603956DD297E7F2B8E7B8F848F6680EBA738741D853565377CB77ED9DEF5` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `9fe271ae-8c3e-4679-a359-5a06c79ce5b0.png` | 2,999,505 | 1922x1112 | `51B3E494346241291C1134EDAD65B8D94F8A08E667C9D879748706FFE0B3F164` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency, item tooltip | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `b58bcc7f-99ce-4d1c-a4d1-60f8808bd6a0.png` | 1,944,499 | 1442x932 | `485C31D09EDB2671633686C2C3861317816FF91961B0B33149CCA7088291A77E` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `c38170e0-5bf9-4c5f-8095-5ab05a49d041.png` | 1,474,926 | 1282x752 | `D5448EDCDAE451AF902388A9CCEC4595CAEDCD9C9F552874B3191A6C1AD9BD0A` | yes | uncertain | open/visible | visible | multiple player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `cc30682b-53f4-49e3-976a-b62144500ae0.png` | 2,091,967 | 1602x932 | `12D2BB362068F6F0D56B77CD1799D88371F94939912651ECECF6382AD3933AA5` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `dc257205-86e2-4112-9191-559005cff4bf.png` | 1,552,661 | 1282x800 | `6A8D203AD7BBD36A312DC1AB71B58E2105E6A639DD40D1AA6C5AD9D9DFA8D90A` | yes | uncertain | open/visible | visible | multiple player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `f650a84a-80e6-42c1-a6c7-1acb42d9f2bf.png` | 3,255,403 | 1922x1232 | `B4F62E3855EDF039B505945280444DD009D7BF13BF829E046145C84CA43AA7D0` | yes | uncertain | open/visible | visible | player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |
| `fe3e12d8-b920-4594-8181-710997e5c994.png` | 1,295,476 | 1178x696 | `FE3FE4AB8A04DE34423292B42B7612E9DA5AF911C3EAF5EEA4FDC90E9B526052` | yes | uncertain | open/visible | visible | multiple player labels, character label, stats/currency | repeat-pair candidate; BD identity unproven; no unknown/panel-absent candidate |

The repeated-pair designation is deliberately weak: it is based only on the visually similar panel/equipment/stat arrangement. It is not a claim that the files are from the same BD, session, client resolution, or capture event.

## Read-Only Commands And Results

All commands were run from `E:\D4Hub`.

1. File enumeration:

```powershell
Get-ChildItem -LiteralPath .artifacts\v0.2-real-frames\target -File -Filter *.png |
  Sort-Object Name |
  Select-Object Name,Length
```

Result: 17 PNG files, all UUID-like names, no `manifest.json` in the target directory.

2. Exact dimensions and hashes:

```powershell
Get-ChildItem -LiteralPath .artifacts\v0.2-real-frames\target -File -Filter *.png |
  Sort-Object Name |
  ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    Add-Type -AssemblyName System.Drawing
    $image = [System.Drawing.Image]::FromFile($_.FullName)
    try {
      [pscustomobject]@{
        Name = $_.Name
        Bytes = $_.Length
        Width = $image.Width
        Height = $image.Height
        SHA256 = $hash
      }
    } finally {
      $image.Dispose()
    }
  }
```

Result: 17/17 metadata records are reproduced in the table above. Exact target counts are zero at both required resolutions.

3. Visual inspection:

Each of the 17 original files was opened individually with the read-only image viewer in five batches. The direct observations are recorded per row. No image was exported, transformed, or otherwise written.

4. Replay contract read-only check:

`tests/vision-replay/README.md` requires schema version `1`, `evidenceClass` `smoke` or `target`, and both target resolutions in a target manifest. The target directory contains no manifest, and this worker did not create one or invoke `verify-vision-replay.ps1`.

## Target Gate Gaps

The current 17-image set cannot satisfy the target matrix:

- no exact 1920x1080 pair for known BD-A;
- no exact 1920x1080 pair for known BD-B;
- no exact 1920x1080 unknown/unregistered-BD panel-open negative;
- no exact 1920x1080 panel-absent negative;
- no exact 1280x960 pair for known BD-A;
- no exact 1280x960 pair for known BD-B;
- no exact 1280x960 unknown/unregistered-BD panel-open negative;
- no exact 1280x960 panel-absent negative;
- no Owner-approved labels or provenance bound to image hashes;
- no manifest with reviewed expected decisions/panel bounds;
- no target replay report.

The visible panel is open in all 17 images, so there is no panel-absent candidate. No image can be designated BD-A, BD-B, or unknown from pixels alone. The apparent HUD markers remain `uncertain`, and the required HUD-hidden capture condition is not demonstrated.

## Exact Owner Input Request

Owner must provide or explicitly approve provenance for twelve new raw client-surface PNGs, with their exact SHA-256 values bound to the labels below:

| Resolution | Required cases |
| --- | --- |
| `1920x1080` | known BD-A panel open twice; known BD-B panel open twice; one unknown/unregistered BD panel open; one panel-absent frame |
| `1280x960` | known BD-A panel open twice; known BD-B panel open twice; one unknown/unregistered BD panel open; one panel-absent frame |

For every file, Owner must confirm: exact client dimensions; HUD hidden; no crop/resize/composite/re-encode; stable UI scale within each repeated pair; visible skill bar; positive BD mapping outside the image; and account/chat information hidden before capture. A later manager-controlled manifest must use exact paths, hashes, dimensions, and expected accepted/rejected decisions. The current 17 images may not be relabeled or substituted for these cases.

## Evidence Classification And Boundary

| State | Corpus-intake classification |
| --- | --- |
| implemented | intake audit/report only; no product behavior changed |
| tested | metadata inventory and visual inspection complete; target replay not run |
| packaged | no |
| independently verified | no; this report is provisional worker evidence |
| owner accepted | no |
| committed | no candidate SHA |
| pushed | no evidence and no authorization |
| published | no; blocked/deferred |

Remaining boundary: no target manifest, replay, acceptance, packaging, signing, hosting, upload, installer/updater, production distribution, telemetry, push, commit, or publication is authorized by this intake. V10-E01 remains blocked until the exact Owner input above is available and independently reviewed.
