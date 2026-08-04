# Offline combat text models

D4Hub does not download combat-recognition models at runtime. The calibrated
text-spotting pipeline remains unavailable until this directory contains a
reviewed `manifest.json` plus exactly two verified ONNX assets:

- `detector`: a lightweight PP-OCR/DBNet-class text instance detector.
- `recognizer`: a recognizer restricted to
  `0123456789.,，．万亿兆京`.

The manifest uses schema version 1 and records, for each asset, its relative
file path, SHA-256, source URL, upstream version, and SPDX license identifier.
It must also list every supported D4Hub calibration profile. D4Hub validates
all metadata, paths, hashes, language, and profile compatibility before an
engine may report itself as available.

No model is currently bundled because a source/version/license/hash set has
not yet been approved. Until that review is complete, the application reports
the local Windows OCR path as a baseline screen estimate rather than claiming
calibrated text spotting or exact DPS.

## Model decision

The production target is a lightweight, D4-specific text-instance detector
plus the restricted recognizer above, exported to ONNX and evaluated with the
same multi-frame tracker. YOLO may be compared as a detector, but it is not the
numeric recognizer and is not the default architecture.

A local PP-OCRv5/PaddleSharp experiment remains available through
`D4Hub.CombatProbe --pipeline paddle`. A same-clip replay on 2026-07-28 raised
the tracker's internal confirmed-observation coverage from 17.0% to 26.2%, but
averaged 2899 ms per frame, had a 4635 ms P95, and merged overlapping text into
one false `7,334.80亿` event. Half-scale input was slower and still produced a
suspicious small event. Those results reject the generic model as the live HUD
default; they are not accuracy or recall measurements because the clip is not
fully annotated.
