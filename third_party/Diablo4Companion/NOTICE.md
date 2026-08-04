# Diablo4Companion calibration metadata

This directory contains a narrow subset of numeric calibration configuration
from [josdemmers/Diablo4Companion](https://github.com/josdemmers/Diablo4Companion).
The upstream project is licensed under MIT; see `LICENSE`.

Source snapshot:

- Repository: `https://github.com/josdemmers/Diablo4Companion`
- Git tree: `b0bfad89a7474676ad9b291c488603fd0a44e52c`
- Audit archive SHA-256: `70BE6436C02D95DBC87CE5BEAF1F34C7F4B45DF2A75ED07A9E3772F6164821EE`
- Retrieved for audit: 2026-07-27

Included calibration files contain only numeric thresholds, offsets, and
dimensions. They are reference inputs and are not proof that a D4Hub detector
is calibrated. Each profile must be validated against user-owned capture
fixtures before it can be promoted to a runtime default.

At the user's request, the audit snapshot's 407 visual files are now present
under `visual-assets/` for source-level research. Their relative paths,
sizes, and SHA-256 values are recorded in
`visual-assets/VISUAL_ASSETS_MANIFEST.json`. They are deliberately excluded
from the D4Hub build and publish outputs. The upstream preset guide says many
of these images are captured from Diablo IV UI, so the upstream MIT license
alone is not sufficient evidence that D4Hub may redistribute the underlying
game imagery.

The localized affix, aspect, item, paragon, rune, sigil, and unique catalogs
under upstream `D4Companion/Data` are also excluded. They are outside the
calibration scope and their underlying game-data provenance is not established
by the MIT license notice.

`SOURCE_MANIFEST.json` records source and destination hashes plus audit counts.
