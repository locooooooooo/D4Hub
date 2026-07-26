using System.Security.Cryptography;
using D4Hub.Core;

var tests = new (string Name, Action Run)[]
{
    ("starter document", StarterDocumentHasExpectedSections),
    ("state round trip", StateRoundTripsWithoutDataLoss),
    ("atomic replacement", ExistingStateIsReplacedWithoutTemporaryFiles),
    ("damaged state recovery", DamagedStateFallsBackToStarterDocument),
    ("strict import rejects missing state", StrictImportRejectsMissingState),
    ("strict import rejects malformed state", StrictImportRejectsMalformedState),
    ("strict import rejects empty or null state", StrictImportRejectsEmptyOrNullState),
    ("strict import rejects unsupported schema", StrictImportRejectsUnsupportedSchema),
    ("strict import rejects invalid model", StrictImportRejectsInvalidModel),
    ("strict import accepts valid export", StrictImportAcceptsValidExport),
    ("failed strict import preserves active state", FailedStrictImportPreservesActiveState),
    ("overlay setting bounds", OverlayOpacityIsClamped),
    ("HUD mode defaults compact", HudDisplayModeDefaultsToCompact),
    ("HUD profile defaults", HudProfileContainsAllEquipmentSlots),
    ("HUD layout template round trip", HudLayoutTemplateRoundTrips),
    ("HUD layout templates isolate resolutions", HudLayoutTemplatesIsolateResolutions),
    ("HUD layout templates isolate profiles", HudLayoutTemplatesIsolateProfiles),
    ("HUD layout reset restores defaults", HudLayoutResetRestoresDefaults),
    ("transmutation reminder requires the selected recipe", TransmutationReminderRequiresSelectedRecipe),
    ("character panel detection", CharacterPanelIsDetectedInSyntheticFrame),
    ("character panel requires character title", CharacterPanelRequiresCharacterTitle),
    ("letterboxed panel detection", LetterboxedCharacterPanelIsDetected),
    ("windowed screenshot panel detection", WindowedScreenshotPanelIsDetected),
    ("build fingerprint matching", BuildFingerprintMatchesIdenticalFrame),
    ("build fingerprint rejects no registered fingerprints", BuildFingerprintRejectsNoRegisteredFingerprints),
    ("build fingerprint rejects below threshold", BuildFingerprintRejectsBelowThreshold),
    ("build fingerprint rejects near-tie ambiguity", BuildFingerprintRejectsNearTieAmbiguity),
    ("build fingerprint selects clearly distinct winner", BuildFingerprintSelectsClearlyDistinctWinner),
    ("local update manifest accepts valid artifact", LocalUpdateManifestAcceptsValidArtifact),
    ("local update manifest rejects invalid JSON shapes", LocalUpdateManifestRejectsInvalidJsonShapes),
    ("local update manifest rejects schema and identity mismatch", LocalUpdateManifestRejectsSchemaAndIdentityMismatch),
    ("local update manifest requires forward version", LocalUpdateManifestRequiresForwardVersion),
    ("local update manifest rejects unsafe artifact names", LocalUpdateManifestRejectsUnsafeArtifactNames),
    ("local update manifest rejects missing artifact", LocalUpdateManifestRejectsMissingArtifact),
    ("local update manifest rejects invalid and mismatched size", LocalUpdateManifestRejectsInvalidAndMismatchedSize),
    ("local update manifest rejects invalid and mismatched hash", LocalUpdateManifestRejectsInvalidAndMismatchedHash),
    ("D2Core URL normalization", D2CoreUrlSelectsRequestedVariant),
    ("D2Core public sample uses one-based var", D2CorePublicSampleUsesHumanVariantNumber),
    ("D2Core HUD text is compact", D2CoreHudTextIsCompact),
    ("HUD source markers can coexist", HudSourceMarkersCanCoexist),
    ("HUD transfigured poison affixes stay distinct and last", HudTransfiguredPoisonAffixesStayDistinctAndLast),
    ("D2Core legacy HUD affixes migrate", D2CoreLegacyHudAffixesMigrate),
    ("D2Core parser preserves affixes", D2CoreParserPreservesStructuredAffixes),
    ("D2Core profile maps all equipment", D2CoreProfileMapsSelectedVariant),
    ("Barbarian profile maps four weapons", BarbarianProfileMapsFourWeapons),
    ("legacy Barbarian profile gains fourth weapon", LegacyBarbarianProfileMigratesFourWeapons),
    ("public library avoids network", PublicLibraryHitAvoidsNetwork),
    ("cache miss fetches once", CacheMissFetchesAndPersistsOnce)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception.Message}");
    }
}

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    return 1;
}

Console.WriteLine($"PASS all {tests.Length} acceptance checks");
return 0;

static void StarterDocumentHasExpectedSections()
{
    var document = BuildDocument.CreateStarter();
    Assert(document.SchemaVersion == 1, "schema version should be 1");
    Assert(document.Build.Sections.Count == 4, "starter should contain four sections");
    Assert(document.Build.Sections.All(section => section.Items.Count >= 3), "every section should contain starter items");
    Assert(document.Build.TotalCount == 12, "starter should contain twelve items");
}

static void StateRoundTripsWithoutDataLoss()
{
    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "state.json");
    var store = new JsonStateStore(path);
    var document = BuildDocument.CreateStarter();
    document.Build.Name = "压制流测试";
    document.Build.Sections[0].Items[0].IsCompleted = true;
    document.Overlay.Opacity = 0.78;
    document.Overlay.HudDisplayMode = HudDisplayMode.Values;

    store.Save(document);
    var reloaded = store.Load();

    Assert(reloaded.Build.Name == "压制流测试", "build name should survive a round trip");
    Assert(reloaded.Build.Sections[0].Items[0].IsCompleted, "completion state should survive a round trip");
    Assert(Math.Abs(reloaded.Overlay.Opacity - 0.78) < 0.001, "overlay opacity should survive a round trip");
    Assert(reloaded.Overlay.HudDisplayMode == HudDisplayMode.Values, "HUD display mode should survive a round trip");
}

static void ExistingStateIsReplacedWithoutTemporaryFiles()
{
    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "state.json");
    var store = new JsonStateStore(path);
    var document = BuildDocument.CreateStarter();
    store.Save(document);
    document.Build.Name = "第二次保存";
    store.Save(document);

    Assert(store.Load().Build.Name == "第二次保存", "second save should replace the state file");
    Assert(Directory.GetFiles(workspace.Path, "*.tmp").Length == 0, "temporary files should be cleaned up");
}

static void DamagedStateFallsBackToStarterDocument()
{
    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "state.json");

    var missing = new JsonStateStore(path).Load();
    Assert(missing.Build.TotalCount == 12, "missing startup state should recover to starter items");

    File.WriteAllText(path, "{ not valid json");

    var recovered = new JsonStateStore(path).Load();

    Assert(recovered.Build.Sections.Count == 4, "damaged state should recover to a usable starter document");
    Assert(recovered.Build.TotalCount == 12, "recovered state should contain starter items");
}

static void StrictImportRejectsMissingState()
{
    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "missing.json");

    AssertThrows<FileNotFoundException>(
        () => new JsonStateStore(path).LoadStrict(),
        "strict import should reject a missing file");
}

static void StrictImportRejectsMalformedState()
{
    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "malformed.json");
    File.WriteAllText(path, "{ not valid json");

    AssertThrows<System.Text.Json.JsonException>(
        () => new JsonStateStore(path).LoadStrict(),
        "strict import should reject malformed JSON");
}

static void StrictImportRejectsEmptyOrNullState()
{
    using var workspace = new TemporaryDirectory();
    var emptyPath = Path.Combine(workspace.Path, "empty.json");
    var nullPath = Path.Combine(workspace.Path, "null.json");
    File.WriteAllText(emptyPath, string.Empty);
    File.WriteAllText(nullPath, "null");

    AssertThrows<System.Text.Json.JsonException>(
        () => new JsonStateStore(emptyPath).LoadStrict(),
        "strict import should reject an empty file");
    AssertThrows<InvalidDataException>(
        () => new JsonStateStore(nullPath).LoadStrict(),
        "strict import should reject a JSON null document");
}

static void StrictImportRejectsUnsupportedSchema()
{
    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "schema.json");
    new JsonStateStore(path).Save(BuildDocument.CreateStarter());
    var json = File.ReadAllText(path).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
    File.WriteAllText(path, json);

    AssertThrows<InvalidDataException>(
        () => new JsonStateStore(path).LoadStrict(),
        "strict import should reject an unsupported schema");
}

static void StrictImportRejectsInvalidModel()
{
    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "model.json");
    var document = BuildDocument.CreateStarter();
    new JsonStateStore(path).Save(document);
    var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    root["selectedProfileId"] = "missing-profile";
    File.WriteAllText(path, root.ToJsonString());

    AssertThrows<InvalidDataException>(
        () => new JsonStateStore(path).LoadStrict(),
        "strict import should reject a selected profile that does not exist");
}

static void StrictImportAcceptsValidExport()
{
    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "export.json");
    var document = BuildDocument.CreateStarter();
    document.Build.Name = "strict round trip";
    var store = new JsonStateStore(path);
    store.Export(document, path);

    var imported = store.LoadStrict();

    Assert(imported.Build.Name == "strict round trip", "strict import should preserve a valid exported document");
    Assert(imported.SelectedProfileId == document.SelectedProfileId, "strict import should preserve the selected profile");
    Assert(Directory.GetFiles(workspace.Path, "*.tmp").Length == 0, "valid export/import should leave no temporary files");
}

static void FailedStrictImportPreservesActiveState()
{
    using var workspace = new TemporaryDirectory();
    var activePath = Path.Combine(workspace.Path, "active.json");
    var importPath = Path.Combine(workspace.Path, "import.json");
    var activeStore = new JsonStateStore(activePath);
    var activeDocument = BuildDocument.CreateStarter();
    activeDocument.Build.Name = "preserve me";
    activeStore.Save(activeDocument);
    var beforeBytes = File.ReadAllBytes(activePath);
    var beforeHash = Convert.ToHexString(SHA256.HashData(beforeBytes));
    var originalReference = activeDocument;
    File.WriteAllText(importPath, "{ malformed import");

    try
    {
        var imported = new JsonStateStore(importPath).LoadStrict();
        activeStore.Save(imported);
        activeDocument = imported;
    }
    catch (System.Text.Json.JsonException)
    {
    }

    var afterBytes = File.ReadAllBytes(activePath);
    var afterHash = Convert.ToHexString(SHA256.HashData(afterBytes));
    Assert(ReferenceEquals(activeDocument, originalReference), "failed import should not replace the active in-memory document");
    Assert(activeDocument.Build.Name == "preserve me", "failed import should not mutate active in-memory data");
    Assert(beforeBytes.SequenceEqual(afterBytes), "failed import should preserve persisted state bytes");
    Assert(beforeHash == afterHash, "failed import should preserve the persisted SHA-256");
    Assert(Directory.GetFiles(workspace.Path, "*.tmp").Length == 0, "failed import should leave no temporary files");
}

static void OverlayOpacityIsClamped()
{
    var settings = new OverlaySettings { Opacity = 0.1 };
    Assert(Math.Abs(settings.Opacity - 0.55) < 0.001, "opacity should not become unreadably low");
    settings.Opacity = 2;
    Assert(Math.Abs(settings.Opacity - 1) < 0.001, "opacity should not exceed one");
}

static void HudDisplayModeDefaultsToCompact()
{
    Assert(new OverlaySettings().HudDisplayMode == HudDisplayMode.Compact, "new settings should default to the compact HUD");
    Assert(BuildDocument.CreateStarter().Overlay.HudDisplayMode == HudDisplayMode.Compact, "starter documents should use the compact HUD");
}

static void HudLayoutTemplateRoundTrips()
{
    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "state.json");
    var document = BuildDocument.CreateStarter();
    var profile = document.Profiles[0];
    var helm = profile.EquipmentRules.Single(rule => rule.Slot == EquipmentSlotKind.Helm);
    helm.AnchorX = 321;
    helm.AnchorY = 123;
    HudLayoutTemplateService.Capture(profile, 1920, 1080, DateTimeOffset.Parse("2026-07-22T00:00:00Z"));

    new JsonStateStore(path).Save(document);
    var reloaded = new JsonStateStore(path).Load();
    var template = reloaded.Profiles[0].LayoutTemplates.Single();

    Assert(template.ClientWidth == 1920 && template.ClientHeight == 1080, "template resolution should survive JSON round trip");
    Assert(template.Slots.Single(slot => slot.Slot == EquipmentSlotKind.Helm).AnchorX == 321, "template position should survive JSON round trip");
}

static void HudLayoutTemplatesIsolateResolutions()
{
    var profile = HudProfileFactory.CreateStarterProfile();
    var helm = profile.EquipmentRules.Single(rule => rule.Slot == EquipmentSlotKind.Helm);
    helm.AnchorX = 260;
    HudLayoutTemplateService.Capture(profile, 1920, 1080);
    helm.AnchorX = 340;
    HudLayoutTemplateService.Capture(profile, 2560, 1440);

    HudProfileFactory.ResetLayout(profile);
    Assert(HudLayoutTemplateService.Apply(profile, 1920, 1080), "saved 1080p template should be found");
    Assert(helm.AnchorX == 260, "1080p template should keep its own coordinate");
    Assert(HudLayoutTemplateService.Apply(profile, 2560, 1440), "saved 1440p template should be found");
    Assert(helm.AnchorX == 340, "1440p template should keep its own coordinate");
    Assert(!HudLayoutTemplateService.Apply(profile, 1600, 900), "an unsaved resolution must not reuse another resolution template");
}

static void HudLayoutTemplatesIsolateProfiles()
{
    var rogue = HudProfileFactory.CreateStarterProfile();
    var sorcerer = HudProfileFactory.CreateStarterProfile();
    var rogueHelm = rogue.EquipmentRules.Single(rule => rule.Slot == EquipmentSlotKind.Helm);
    var sorcererHelm = sorcerer.EquipmentRules.Single(rule => rule.Slot == EquipmentSlotKind.Helm);
    rogueHelm.AnchorX = 245;
    sorcererHelm.AnchorX = 365;
    HudLayoutTemplateService.Capture(rogue, 1920, 1080);
    HudLayoutTemplateService.Capture(sorcerer, 1920, 1080);

    HudProfileFactory.ResetLayout(rogue);
    HudProfileFactory.ResetLayout(sorcerer);
    HudLayoutTemplateService.Apply(rogue, 1920, 1080);
    HudLayoutTemplateService.Apply(sorcerer, 1920, 1080);

    Assert(rogueHelm.AnchorX == 245, "first profile should load only its layout");
    Assert(sorcererHelm.AnchorX == 365, "second profile should load only its layout");
}

static void HudLayoutResetRestoresDefaults()
{
    var profile = HudProfileFactory.CreateStarterProfile();
    var helm = profile.EquipmentRules.Single(rule => rule.Slot == EquipmentSlotKind.Helm);
    helm.AnchorX = 410;
    helm.AnchorY = 310;
    HudProfileFactory.ResetLayout(profile);

    Assert(helm.AnchorX == 193 && helm.AnchorY == 83, "reset should restore the starter layout");
}

static void TransmutationReminderRequiresSelectedRecipe()
{
    var detector = new TransmutationSceneDetector();
    var blank = CreateSyntheticGameFrame();
    var blankDetection = detector.Detect(blank);
    Assert(!blankDetection.IsTransmutationVisible, "ordinary game frames must not show the transmutation reminder");

    var frame = CreateSyntheticTransmutationFrame();
    var detection = detector.Detect(frame);
    Assert(detection.IsTransmutationVisible, $"selected transmutation recipe should be detected, actual confidence {detection.ContextConfidence:F3}");
    Assert(detection.SelectedRecipeBounds.Width > 0, "detected recipe should provide placement bounds for the reminder");
}

static void HudProfileContainsAllEquipmentSlots()
{
    var document = BuildDocument.CreateStarter();
    Assert(document.Profiles.Count == 1, "starter should contain one editable HUD profile");
    Assert(document.Profiles[0].EquipmentRules.Count == 11, "HUD profile should cover eleven equipment slots");
    Assert(document.Profiles[0].EquipmentRules.All(rule => !string.IsNullOrWhiteSpace(rule.MandatoryText)), "every slot should have visible target affixes");
    var rules = document.Profiles[0].EquipmentRules.ToDictionary(rule => rule.Slot);
    var expectedLayout = new Dictionary<EquipmentSlotKind, (double X, double Y, double Width)>
    {
        [EquipmentSlotKind.Helm] = (193, 83, 200),
        [EquipmentSlotKind.Chest] = (196, 177, 200),
        [EquipmentSlotKind.Gloves] = (196, 272, 200),
        [EquipmentSlotKind.Pants] = (195, 378, 200),
        [EquipmentSlotKind.Boots] = (195, 480, 200),
        [EquipmentSlotKind.Ranged] = (195, 582, 200),
        [EquipmentSlotKind.Amulet] = (540, 185, 100),
        [EquipmentSlotKind.RingLeft] = (539, 275, 100),
        [EquipmentSlotKind.RingRight] = (537, 373, 100),
        [EquipmentSlotKind.MainHand] = (365, 578, 95),
        [EquipmentSlotKind.OffHand] = (441, 578, 100)
    };
    Assert(expectedLayout.All(entry =>
    {
        var rule = rules[entry.Key];
        return rule.AnchorX == entry.Value.X
            && rule.AnchorY == entry.Value.Y
            && rule.DisplayWidth == entry.Value.Width;
    }), "starter HUD positions should match the approved calibrated layout");
}

static void CharacterPanelIsDetectedInSyntheticFrame()
{
    var frame = CreateSyntheticGameFrame();
    var detection = new CharacterPanelDetector().Detect(frame);
    Assert(Math.Abs(detection.Bounds.X - 0.62) < 0.025, $"panel boundary should be near 0.62, actual {detection.Bounds.X:F3}");
    Assert(detection.Confidence >= 0.55, $"panel confidence should be actionable, actual {detection.Confidence:F3}");
}

static void CharacterPanelRequiresCharacterTitle()
{
    var frame = CreateSyntheticGameFrame(includeCharacterTitle: false);
    var detection = new CharacterPanelDetector().Detect(frame);

    Assert(detection.Confidence == 0, "panel-like structure without the character title must be rejected");
}

static void LetterboxedCharacterPanelIsDetected()
{
    var frame = CreateSyntheticGameFrame(
        width: 1280,
        height: 960,
        panelLeft: 768,
        activeTop: 80,
        activeBottom: 920);
    var detection = new CharacterPanelDetector().Detect(frame);

    Assert(Math.Abs(detection.Bounds.X - 0.60) < 0.025, $"letterboxed panel boundary should be near 0.60, actual {detection.Bounds.X:F3}");
    Assert(Math.Abs(detection.Bounds.Y - 80d / 960) < 0.01, $"HUD should start below the top letterbox, actual {detection.Bounds.Y:F3}");
    Assert(Math.Abs(detection.Bounds.Height - 840d / 960) < 0.015, $"HUD should use only the active viewport height, actual {detection.Bounds.Height:F3}");
    Assert(detection.Confidence >= 0.55, $"letterboxed panel confidence should be actionable, actual {detection.Confidence:F3}");
}

static void WindowedScreenshotPanelIsDetected()
{
    var frame = CreateSyntheticGameFrame(
        width: 1000,
        height: 600,
        panelLeft: 620,
        activeTop: 30,
        activeBottom: 600,
        titleHeight: 30);
    var detection = new CharacterPanelDetector().Detect(frame);

    Assert(Math.Abs(detection.Bounds.X - 0.62) < 0.025, $"windowed panel boundary should be near 0.62, actual {detection.Bounds.X:F3}");
    Assert(Math.Abs(detection.Bounds.Y - 0.05) < 0.01, $"screenshot preview should exclude the window title bar, actual {detection.Bounds.Y:F3}");
}

static void BuildFingerprintMatchesIdenticalFrame()
{
    var frame = CreateSyntheticGameFrame();
    var detector = new CharacterPanelDetector();
    var detection = detector.Detect(frame);
    var fingerprints = new BuildFingerprintService();
    var expected = fingerprints.Capture(frame, detection);
    var profile = HudProfileFactory.CreateStarterProfile();
    profile.Fingerprint = expected;

    var match = fingerprints.Recognize(frame, detection, new[] { profile }, 0.72);
    Assert(ReferenceEquals(match.Profile, profile), "identical visual fingerprint should resolve to the registered profile");
    Assert(match.Confidence > 0.99, "identical visual fingerprint should have full confidence");
}

static void BuildFingerprintRejectsNoRegisteredFingerprints()
{
    var (frame, detection, fingerprints, observed) = CreateFingerprintTestContext();
    var missing = HudProfileFactory.CreateStarterProfile();
    var incomplete = HudProfileFactory.CreateStarterProfile();
    incomplete.Fingerprint = new BuildVisualFingerprint
    {
        LeftHash = observed.LeftHash,
        CenterHash = observed.CenterHash
    };

    var match = fingerprints.Recognize(frame, detection, new[] { missing, incomplete }, 0.72);

    Assert(match.Profile is null, "profiles without a complete registered fingerprint must be rejected");
    Assert(match.Confidence == 0, "no registered fingerprints should report zero confidence");
}

static void BuildFingerprintRejectsBelowThreshold()
{
    var (frame, detection, fingerprints, observed) = CreateFingerprintTestContext();
    var profile = HudProfileFactory.CreateStarterProfile();
    profile.Fingerprint = CreateFingerprintAtDistance(observed, 64);

    var match = fingerprints.Recognize(frame, detection, new[] { profile }, 0.72);

    Assert(match.Profile is null, "a fingerprint below the configured threshold must be rejected");
    Assert(Math.Abs(match.Confidence - (1 - 64 / 192d)) < 0.000001, "below-threshold confidence should remain observable");
}

static void BuildFingerprintRejectsNearTieAmbiguity()
{
    var (frame, detection, fingerprints, observed) = CreateFingerprintTestContext();
    var first = HudProfileFactory.CreateStarterProfile();
    first.Fingerprint = CreateFingerprintAtDistance(observed, 4);
    var second = HudProfileFactory.CreateStarterProfile();
    second.Fingerprint = CreateFingerprintAtDistance(observed, 10);

    var match = fingerprints.Recognize(frame, detection, new[] { first, second }, 0.72);

    Assert(match.Profile is null, "fingerprints separated by less than the ambiguity margin must be rejected");
    Assert(Math.Abs(match.Confidence - (1 - 4 / 192d)) < 0.000001, "ambiguous rejection should report the leading confidence");
}

static void BuildFingerprintSelectsClearlyDistinctWinner()
{
    var (frame, detection, fingerprints, observed) = CreateFingerprintTestContext();
    var winner = HudProfileFactory.CreateStarterProfile();
    winner.Fingerprint = CreateFingerprintAtDistance(observed, 4);
    var runnerUp = HudProfileFactory.CreateStarterProfile();
    runnerUp.Fingerprint = CreateFingerprintAtDistance(observed, 16);

    var match = fingerprints.Recognize(frame, detection, new[] { runnerUp, winner }, 0.72);

    Assert(ReferenceEquals(match.Profile, winner), "a candidate outside the ambiguity margin should be selected");
    Assert(Math.Abs(match.Confidence - (1 - 4 / 192d)) < 0.000001, "distinct winner should report its confidence");
}

static void LocalUpdateManifestAcceptsValidArtifact()
{
    using var workspace = new TemporaryDirectory();
    var payload = System.Text.Encoding.UTF8.GetBytes("local-update-contract-fixture");
    const string fileName = "D4Hub-0.2.0-win-x64.zip";
    File.WriteAllBytes(Path.Combine(workspace.Path, fileName), payload);
    var manifestJson = CreateLocalUpdateManifestJson(payload, fileName);

    var result = new LocalUpdateManifestValidator().Validate(
        manifestJson,
        CreateLocalUpdateContext(workspace.Path));

    Assert(result.IsAccepted, $"valid local artifact should be accepted, actual {result.RejectionCode}: {result.Message}");
    Assert(result.RejectionCode == LocalUpdateRejectionCode.None, "accepted result should not contain a rejection code");
    Assert(result.Manifest?.Version == "0.2.0", "accepted result should expose the parsed candidate version");
    Assert(result.Manifest?.Artifact.FileName == fileName, "accepted result should expose the single artifact file name");
}

static void LocalUpdateManifestRejectsInvalidJsonShapes()
{
    using var workspace = new TemporaryDirectory();
    var validator = new LocalUpdateManifestValidator();
    var context = CreateLocalUpdateContext(workspace.Path);
    var payload = System.Text.Encoding.UTF8.GetBytes("strict-shape-fixture");
    var complete = CreateLocalUpdateManifestJson(payload);

    var malformed = validator.Validate("{ not-json", context);
    var missing = validator.Validate("{}", context);
    var unknown = validator.Validate(
        complete.Replace("\"artifact\":", "\"unexpected\":true,\"artifact\":", StringComparison.Ordinal),
        context);
    var duplicate = validator.Validate(
        complete.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal),
        context);
    var artifactUnknown = validator.Validate(
        complete.Replace("\"fileName\":", "\"unexpected\":true,\"fileName\":", StringComparison.Ordinal),
        context);
    var artifactDuplicate = validator.Validate(
        complete.Replace("\"size\":", "\"size\":1,\"size\":", StringComparison.Ordinal),
        context);

    Assert(malformed.RejectionCode == LocalUpdateRejectionCode.MalformedJson, "malformed JSON should have an explicit rejection");
    Assert(missing.RejectionCode == LocalUpdateRejectionCode.MissingRequiredField, "missing fields should have an explicit rejection");
    Assert(unknown.RejectionCode == LocalUpdateRejectionCode.InvalidManifestShape, "unknown root fields must be rejected by the strict contract");
    Assert(duplicate.RejectionCode == LocalUpdateRejectionCode.InvalidManifestShape, "duplicate root fields must be rejected by the strict contract");
    Assert(artifactUnknown.RejectionCode == LocalUpdateRejectionCode.InvalidManifestShape, "unknown artifact fields must be rejected by the strict contract");
    Assert(artifactDuplicate.RejectionCode == LocalUpdateRejectionCode.InvalidManifestShape, "duplicate artifact fields must be rejected by the strict contract");
}

static void LocalUpdateManifestRejectsSchemaAndIdentityMismatch()
{
    using var workspace = new TemporaryDirectory();
    var payload = System.Text.Encoding.UTF8.GetBytes("identity-fixture");
    var validator = new LocalUpdateManifestValidator();
    var context = CreateLocalUpdateContext(workspace.Path);

    var schema = validator.Validate(CreateLocalUpdateManifestJson(payload, schemaVersion: 2), context);
    var product = validator.Validate(CreateLocalUpdateManifestJson(payload, product: "OtherProduct"), context);
    var channel = validator.Validate(CreateLocalUpdateManifestJson(payload, channel: "beta"), context);
    var architecture = validator.Validate(CreateLocalUpdateManifestJson(payload, architecture: "linux-x64"), context);

    Assert(schema.RejectionCode == LocalUpdateRejectionCode.UnsupportedSchemaVersion, "unsupported schema should be rejected");
    Assert(product.RejectionCode == LocalUpdateRejectionCode.ProductMismatch, "product mismatch should be rejected");
    Assert(channel.RejectionCode == LocalUpdateRejectionCode.ChannelMismatch, "channel mismatch should be rejected");
    Assert(architecture.RejectionCode == LocalUpdateRejectionCode.ArchitectureMismatch, "architecture mismatch should be rejected");
}

static void LocalUpdateManifestRequiresForwardVersion()
{
    using var workspace = new TemporaryDirectory();
    var payload = System.Text.Encoding.UTF8.GetBytes("version-fixture");
    var validator = new LocalUpdateManifestValidator();
    var context = CreateLocalUpdateContext(workspace.Path, currentVersion: "1.2.3");

    var same = validator.Validate(CreateLocalUpdateManifestJson(payload, version: "1.2.3"), context);
    var stale = validator.Validate(CreateLocalUpdateManifestJson(payload, version: "1.2.2"), context);
    var malformed = validator.Validate(CreateLocalUpdateManifestJson(payload, version: "1.3"), context);
    const string fileName = "D4Hub-0.2.0-win-x64.zip";
    File.WriteAllBytes(Path.Combine(workspace.Path, fileName), payload);
    var numericForward = validator.Validate(
        CreateLocalUpdateManifestJson(payload, version: "1.10.0"),
        CreateLocalUpdateContext(workspace.Path, currentVersion: "1.9.9"));

    Assert(same.RejectionCode == LocalUpdateRejectionCode.VersionNotNewer, "same version must not be offered as an update");
    Assert(stale.RejectionCode == LocalUpdateRejectionCode.VersionNotNewer, "stale version must not be offered as an update");
    Assert(malformed.RejectionCode == LocalUpdateRejectionCode.InvalidVersion, "candidate version must use strict major.minor.patch form");
    Assert(numericForward.IsAccepted, $"1.10.0 should be newer than 1.9.9, actual {numericForward.RejectionCode}");
}

static void LocalUpdateManifestRejectsUnsafeArtifactNames()
{
    using var workspace = new TemporaryDirectory();
    var payload = System.Text.Encoding.UTF8.GetBytes("path-fixture");
    var validator = new LocalUpdateManifestValidator();
    var context = CreateLocalUpdateContext(workspace.Path);
    var unsafeNames = new[]
    {
        Path.Combine(workspace.Path, "absolute.zip"),
        "../escape.zip",
        "nested/artifact.zip",
        "nested\\artifact.zip",
        "artifact..zip",
        "CON.zip",
        "CON.foo.bar"
    };

    foreach (var fileName in unsafeNames)
    {
        var result = validator.Validate(CreateLocalUpdateManifestJson(payload, fileName), context);
        Assert(
            result.RejectionCode == LocalUpdateRejectionCode.UnsafeArtifactFileName,
            $"unsafe artifact name '{fileName}' should be rejected, actual {result.RejectionCode}");
    }
}

static void LocalUpdateManifestRejectsMissingArtifact()
{
    using var workspace = new TemporaryDirectory();
    var payload = System.Text.Encoding.UTF8.GetBytes("missing-fixture");
    var result = new LocalUpdateManifestValidator().Validate(
        CreateLocalUpdateManifestJson(payload),
        CreateLocalUpdateContext(workspace.Path));

    Assert(result.RejectionCode == LocalUpdateRejectionCode.ArtifactNotFound, "missing artifact should have an explicit rejection");
}

static void LocalUpdateManifestRejectsInvalidAndMismatchedSize()
{
    using var workspace = new TemporaryDirectory();
    var payload = System.Text.Encoding.UTF8.GetBytes("size-fixture");
    const string fileName = "D4Hub-0.2.0-win-x64.zip";
    File.WriteAllBytes(Path.Combine(workspace.Path, fileName), payload);
    var validator = new LocalUpdateManifestValidator();
    var context = CreateLocalUpdateContext(workspace.Path);

    var invalid = validator.Validate(CreateLocalUpdateManifestJson(payload, size: 0), context);
    var mismatch = validator.Validate(CreateLocalUpdateManifestJson(payload, size: payload.Length + 1), context);

    Assert(invalid.RejectionCode == LocalUpdateRejectionCode.InvalidArtifactSize, "non-positive artifact size should be rejected");
    Assert(mismatch.RejectionCode == LocalUpdateRejectionCode.ArtifactSizeMismatch, "artifact byte-size mismatch should be rejected");
}

static void LocalUpdateManifestRejectsInvalidAndMismatchedHash()
{
    using var workspace = new TemporaryDirectory();
    var payload = System.Text.Encoding.UTF8.GetBytes("hash-fixture");
    const string fileName = "D4Hub-0.2.0-win-x64.zip";
    File.WriteAllBytes(Path.Combine(workspace.Path, fileName), payload);
    var validator = new LocalUpdateManifestValidator();
    var context = CreateLocalUpdateContext(workspace.Path);

    var invalid = validator.Validate(CreateLocalUpdateManifestJson(payload, sha256: "not-a-sha256"), context);
    var mismatch = validator.Validate(CreateLocalUpdateManifestJson(payload, sha256: new string('0', 64)), context);

    Assert(invalid.RejectionCode == LocalUpdateRejectionCode.InvalidArtifactSha256, "invalid SHA-256 format should be rejected");
    Assert(mismatch.RejectionCode == LocalUpdateRejectionCode.ArtifactSha256Mismatch, "artifact SHA-256 mismatch should be rejected");
}

static void D2CoreUrlSelectsRequestedVariant()
{
    var reference = D2CoreBuildUrl.Parse("https://www.d2core.com/d4/planner?var=6&bd=1Zep&utm_source=test");
    Assert(reference.BuildId == "1Zep", "bd parameter should become the build id");
    Assert(reference.VariantNumber == 6, "canonical variant number should remain user-visible and one-based");
    Assert(reference.VariantIndex == 5, "var=6 should select internal variant index 5");
    Assert(reference.CanonicalUrl == "https://www.d2core.com/d4/planner?bd=1Zep&var=6", "URL should be canonical and stable");
}

static void D2CorePublicSampleUsesHumanVariantNumber()
{
    var reference = D2CoreBuildUrl.Parse("https://www.d2core.com/d4/planner?bd=1Zep&var=6");
    var library = new FileBuildLibraryStore(Path.Combine(Environment.CurrentDirectory, "library"), isReadOnly: true);
    Assert(library.TryLoad("d2core", reference.BuildId, out var record), "public 1Zep sample should be loadable");
    var variant = record!.Variants.Single(candidate => candidate.Index == reference.VariantIndex);
    Assert(variant.Index == 5, "the sixth visible variant should use internal index 5");
    Assert(variant.Name.Contains("双核心", StringComparison.Ordinal), $"var=6 should select 双核心, actual {variant.Name}");

    var profile = D2CoreProfileMapper.CreateProfile(record, reference.VariantIndex);
    Assert(profile.Variant.EndsWith("#6", StringComparison.Ordinal), "profile label should show #6");
    Assert(profile.SourceUrl.EndsWith("var=6", StringComparison.Ordinal), "profile source URL should remain var=6");
}

static void D2CoreHudTextIsCompact()
{
    var reference = D2CoreBuildUrl.Parse("https://www.d2core.com/d4/planner?bd=1Zep&var=6");
    var library = new FileBuildLibraryStore(Path.Combine(Environment.CurrentDirectory, "library"), isReadOnly: true);
    Assert(library.TryLoad("d2core", reference.BuildId, out var record), "public 1Zep sample should be loadable");

    var profile = D2CoreProfileMapper.CreateProfile(record!, reference.VariantIndex);
    var helm = profile.EquipmentRules.Single(rule => rule.Slot == EquipmentSlotKind.Helm);
    var expectedCompact = string.Join(Environment.NewLine, "敏捷", "毒灌注", "冷却", "生命", "生命", "毒素伤害");
    var lines = helm.DisplayLines;

    Assert(helm.CompactText == expectedCompact, $"helmet compact text should split duplicate aliases by source, actual {helm.CompactText.Replace(Environment.NewLine, " / ")}");
    Assert(helm.ValueText.Contains("敏捷 121", StringComparison.Ordinal), "value mode should preserve the exact dexterity value");
    Assert(helm.ValueText.Contains("毒灌注 +3", StringComparison.Ordinal), "value mode should preserve the exact skill rank");
    Assert(helm.ValueText.Contains("生命 +1,450", StringComparison.Ordinal) && helm.ValueText.Contains("生命 +1,500", StringComparison.Ordinal), "normal and tempered life should retain separate exact values");
    Assert(!helm.ValueText.Contains("头盔", StringComparison.Ordinal) && !helm.ValueText.Contains("无名者", StringComparison.Ordinal), "HUD text should omit slot and item names");
    Assert(lines.Single(line => line.Name == "毒灌注").IsMasterworked, "critical upgrade level should produce the masterwork marker");
    Assert(lines[^1].Name == "毒素伤害" && lines[^1].IsTransfigured, "transfiguration affixes should retain their name and stay at the bottom");
    Assert(lines.Single(line => line.Name == "生命" && line.IsTempered).ValueText == "生命 +1,500", "tempered life should have its own marker and value");
    Assert(lines.Select(line => line.ColorKind).Distinct().Count() == 5, "core, skill, utility, defensive, and offensive affixes should use distinct color categories");
}

static void HudSourceMarkersCanCoexist()
{
    var line = HudAffixTextFormatter.CreateDisplayLines(new[]
    {
        new ItemAffixRecord
        {
            SourceKey = "X2_Transfiguration_DamageTypePercent_Poison",
            Name = "毒素伤害",
            DisplayText = "10.0%[x]毒素伤害",
            IsTempered = true,
            CriticalUpgradeLevel = 2
        }
    }).Single();

    Assert(line.IsTempered, "tempered marker should be retained");
    Assert(line.IsMasterworked, "masterwork marker should be retained beside tempering");
    Assert(line.IsTransfigured, "transfiguration marker should be retained beside other markers");
    Assert(line.ColorKind == HudAffixColorKind.Offensive, "source markers should not replace the affix color category");
}

static void HudTransfiguredPoisonAffixesStayDistinctAndLast()
{
    var lines = HudAffixTextFormatter.CreateDisplayLines(new[]
    {
        new ItemAffixRecord
        {
            SourceKey = "X2_Transfiguration_DamageTypePercent_Poison",
            Name = "毒素 伤害",
            DisplayText = "12.5%[x]毒素 伤害"
        },
        new ItemAffixRecord
        {
            SourceKey = "X2_DamageType_Poison",
            Name = "毒素 伤害增倍",
            DisplayText = "x13% 毒素 伤害增倍"
        },
        new ItemAffixRecord
        {
            SourceKey = "S04_Armor",
            Name = "护甲",
            DisplayText = "+900 护甲"
        }
    });

    Assert(lines.Select(line => line.Name).SequenceEqual(new[] { "毒素伤害增倍", "护甲", "毒素伤害" }),
        $"non-transfigured affixes should keep their order and transfigured affixes should be last, actual {string.Join(" / ", lines.Select(line => line.Name))}");
    Assert(!lines[0].IsTransfigured && lines[^1].IsTransfigured, "only the transfiguration poison affix should carry the transfiguration marker");
}

static void D2CoreLegacyHudAffixesMigrate()
{
    var reference = D2CoreBuildUrl.Parse("https://www.d2core.com/d4/planner?bd=1Zep&var=6");
    var library = new FileBuildLibraryStore(Path.Combine(Environment.CurrentDirectory, "library"), isReadOnly: true);
    Assert(library.TryLoad("d2core", reference.BuildId, out var record), "public 1Zep sample should be loadable");

    var profile = D2CoreProfileMapper.CreateProfile(record!, reference.VariantIndex);
    foreach (var rule in profile.EquipmentRules)
    {
        rule.Affixes = new List<ItemAffixRecord>();
    }

    var document = BuildDocument.CreateStarter();
    document.Profiles.Clear();
    document.Profiles.Add(profile);
    document.SelectedProfileId = profile.Id;
    document.EnsureValid();

    var helm = profile.EquipmentRules.Single(rule => rule.Slot == EquipmentSlotKind.Helm);
    Assert(helm.Affixes.Count == 6, "legacy imported equipment should repopulate structured HUD affixes");
    Assert(helm.DisplayLines.Count == 6, "migrated HUD text should preserve distinct source markers");
}

static void D2CoreParserPreservesStructuredAffixes()
{
    var catalog = D2CoreAffixCatalog.FromJson("""
        {
          "affix": [
            {
              "key": "S04_CoreStat_Dexterity",
              "desc": "+[100 - 121] 点敏捷",
              "descTpl": "+[{VALUE}|~|] 点敏捷",
              "tempered": false,
              "effectList": [{ "ipower": 900, "min": 100, "max": 121 }]
            },
            {
              "key": "Tempered_Test",
              "desc": "[7.0 - 10.0]% 灌注效果强度",
              "descTpl": "[{VALUE}*100|1%|] 灌注效果强度",
              "tempered": true,
              "effectList": [{ "ipower": 1, "min": 0.07, "max": 0.1 }]
            }
          ]
        }
        """);
    var response = CreateD2CoreFixtureResponse();
    var record = D2CoreBuildParser.Parse(
        response,
        new D2CoreBuildReference("1Zep", 5),
        catalog,
        DateTimeOffset.Parse("2026-07-22T00:00:00Z"));

    Assert(record.BuildId == "1Zep", "parser should retain the source build id");
    Assert(record.Variants.Count == 7, "parser should preserve all variants");
    var item = record.Variants[6].Equipment.Single();
    Assert(item.DisplayName == "无名者兜帽", "parser should retain the item name");
    Assert(item.Affixes.Count == 2, "parser should retain every modifier");
    Assert(item.Affixes[0].DisplayText == "太古 · +121 点敏捷", "greater affix should retain its roll and marker");
    Assert(item.Affixes[0].Minimum == 100 && item.Affixes[0].Maximum == 121, "affix range should be preserved");
    Assert(item.Affixes[1].DisplayText == "回火 · 10.0% 灌注效果强度", "tempered percentage should be formatted");
}

static void D2CoreProfileMapsSelectedVariant()
{
    var equipment = new[] { 0, 1, 2, 3, 4, 5, 8, 9, 10, 12, 13 }
        .Select(slot => new EquipmentItemRecord
        {
            SourceSlot = slot,
            DisplayName = $"装备 {slot}",
            Affixes = new List<ItemAffixRecord>
            {
                new() { SourceKey = $"affix-{slot}", Name = "测试词缀", DisplayText = $"+{slot + 1} 测试词缀" }
            }
        })
        .ToList();
    var record = CreateLibraryRecord("1Zep", sourceUpdatedAt: DateTimeOffset.Parse("2026-07-20T00:00:00Z"));
    record.Variants = Enumerable.Range(0, 7)
        .Select(index => new BuildVariantRecord { Index = index, Name = $"变体 {index}", Equipment = index == 5 ? equipment : new() })
        .ToList();

    var profile = D2CoreProfileMapper.CreateProfile(record, 5);

    Assert(profile.SourceBuildId == "1Zep" && profile.SourceVariantIndex == 5, "profile should retain the internal selected variant index");
    Assert(profile.ImportedEquipment.Count == 11, "all source equipment should remain structured on the profile");
    Assert(profile.EquipmentRules.Count == 11, "all known D4 equipment slots should be visible in the HUD");
    Assert(profile.EquipmentRules.Any(rule => rule.Slot == EquipmentSlotKind.Ranged), "ranged weapon should not be dropped");
}

static void BarbarianProfileMapsFourWeapons()
{
    var equipment = new[] { 0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 12, 13 }
        .Select(slot => new EquipmentItemRecord
        {
            SourceSlot = slot,
            DisplayName = $"slot-{slot}",
            Affixes = new List<ItemAffixRecord>
            {
                new() { SourceKey = $"affix-{slot}", Name = "affix", DisplayText = $"+{slot + 1} affix" }
            }
        })
        .ToList();
    var record = CreateLibraryRecord("barbarian", sourceUpdatedAt: DateTimeOffset.Parse("2026-07-24T00:00:00Z"));
    record.ClassName = "Barbarian";
    record.Variants = new List<BuildVariantRecord>
    {
        new() { Index = 0, Name = "test", Equipment = equipment }
    };

    var profile = D2CoreProfileMapper.CreateProfile(record, 0);
    var rulesByItem = profile.EquipmentRules.ToDictionary(rule => rule.ItemName);
    var expectedWeapons = new Dictionary<int, EquipmentSlotKind>
    {
        [5] = EquipmentSlotKind.BarbarianBludgeoning,
        [6] = EquipmentSlotKind.BarbarianDualWieldMainHand,
        [12] = EquipmentSlotKind.BarbarianSlashing,
        [13] = EquipmentSlotKind.BarbarianDualWieldOffHand
    };

    Assert(profile.EquipmentRules.Count == 12, "Barbarian should render five armor, three jewelry, and four weapon rules");
    Assert(expectedWeapons.All(entry => rulesByItem[$"slot-{entry.Key}"].Slot == entry.Value), "each Barbarian weapon source slot should remain distinct");

    var weapons = expectedWeapons.Values
        .Select(slot => profile.EquipmentRules.Single(rule => rule.Slot == slot))
        .OrderBy(rule => rule.AnchorX)
        .ToList();
    Assert(weapons.Zip(weapons.Skip(1), (left, right) => left.AnchorX + left.DisplayWidth <= right.AnchorX).All(value => value),
        "four Barbarian weapon HUD regions should not overlap");
}

static void LegacyBarbarianProfileMigratesFourWeapons()
{
    var imported = new[] { 5, 6, 12, 13 }
        .Select(slot => new EquipmentItemRecord
        {
            SourceSlot = slot,
            DisplayName = $"slot-{slot}",
            Affixes = new List<ItemAffixRecord>
            {
                new() { SourceKey = $"affix-{slot}", Name = "affix", DisplayText = $"+{slot + 1} affix" }
            }
        })
        .ToList();
    var profile = new BuildProfile
    {
        Source = "d2core",
        ClassName = "野蛮人",
        ImportedEquipment = imported,
        EquipmentRules = new List<EquipmentAffixRule>
        {
            new() { Slot = EquipmentSlotKind.Ranged, SlotLabel = "legacy ranged", AnchorX = 195, AnchorY = 582, DisplayWidth = 200 },
            new() { Slot = EquipmentSlotKind.MainHand, SlotLabel = "legacy main hand", AnchorX = 365, AnchorY = 578, DisplayWidth = 95 },
            new() { Slot = EquipmentSlotKind.OffHand, SlotLabel = "legacy off hand", AnchorX = 441, AnchorY = 578, DisplayWidth = 100 }
        },
        LayoutTemplates = new List<HudLayoutTemplate>
        {
            new()
            {
                ClientWidth = 1920,
                ClientHeight = 1080,
                Slots = new List<HudSlotLayout>
                {
                    new() { Slot = EquipmentSlotKind.Ranged },
                    new() { Slot = EquipmentSlotKind.MainHand },
                    new() { Slot = EquipmentSlotKind.OffHand }
                }
            }
        }
    };

    HudProfileFactory.EnsureRules(profile);

    Assert(profile.EquipmentRules.Count == 4, "legacy Barbarian import should gain the missing dual-wield main-hand rule");
    Assert(profile.EquipmentRules.Any(rule => rule.Slot == EquipmentSlotKind.BarbarianDualWieldMainHand && rule.ItemName == "slot-6"),
        "legacy Barbarian import should preserve the source-six weapon");
    Assert(profile.LayoutTemplates.Single().Slots.All(slot => slot.Slot is not EquipmentSlotKind.Ranged
        and not EquipmentSlotKind.MainHand
        and not EquipmentSlotKind.OffHand), "legacy layout templates should migrate weapon slot identities");
}

static void PublicLibraryHitAvoidsNetwork()
{
    using var workspace = new TemporaryDirectory();
    var publicRoot = Path.Combine(workspace.Path, "public");
    var localRoot = Path.Combine(workspace.Path, "local");
    new FileBuildLibraryStore(publicRoot).Save(CreateLibraryRecord("1Zep", DateTimeOffset.Parse("2026-07-20T00:00:00Z")));
    var client = new CountingD2CoreClient(CreateLibraryRecord("1Zep", DateTimeOffset.Parse("2026-07-21T00:00:00Z")));
    var resolver = new D2CoreBuildResolver(
        new FileBuildLibraryStore(publicRoot, isReadOnly: true),
        new FileBuildLibraryStore(localRoot),
        client);

    var result = resolver.ResolveAsync(new D2CoreBuildReference("1Zep", 5)).GetAwaiter().GetResult();

    Assert(result.Origin == BuildResolutionOrigin.PublicLibrary, "bundled record should resolve from the public library");
    Assert(client.FetchCount == 0, "public library hit must perform zero provider requests");
}

static void CacheMissFetchesAndPersistsOnce()
{
    using var workspace = new TemporaryDirectory();
    var publicRoot = Path.Combine(workspace.Path, "public");
    var localRoot = Path.Combine(workspace.Path, "local");
    var client = new CountingD2CoreClient(CreateLibraryRecord("1Zep", DateTimeOffset.Parse("2026-07-21T00:00:00Z")));
    var resolver = new D2CoreBuildResolver(
        new FileBuildLibraryStore(publicRoot, isReadOnly: true),
        new FileBuildLibraryStore(localRoot),
        client);
    var reference = new D2CoreBuildReference("1Zep", 5);

    var first = resolver.ResolveAsync(reference).GetAwaiter().GetResult();
    var second = resolver.ResolveAsync(reference).GetAwaiter().GetResult();

    Assert(first.Origin == BuildResolutionOrigin.D2CoreNetwork, "first cache miss should use the provider");
    Assert(second.Origin == BuildResolutionOrigin.LocalCache, "second import should use the persisted local record");
    Assert(client.FetchCount == 1, "cache miss should fetch exactly once across repeated imports");
    Assert(File.Exists(Path.Combine(localRoot, "d2core", "1Zep.json")), "network result should be written to the local library");
    Assert(File.Exists(Path.Combine(localRoot, "index.json")), "local library index should be maintained");
}

static string CreateD2CoreFixtureResponse()
{
    var variants = Enumerable.Range(0, 7)
        .Select(index => index == 6
            ? new
            {
                name = "刃舞刀扇-单体王-打老墨",
                gear = new Dictionary<string, object>
                {
                    ["0"] = new
                    {
                        itemPower = 900,
                        itemType = "Helm",
                        key = "Helm_Unique_Rogue_001",
                        name = "无名者兜帽",
                        type = "uniqueItem",
                        mythic = true,
                        mods = new object[]
                        {
                            new { greater = true, name = "S04_CoreStat_Dexterity", value = 121 },
                            new { name = "Tempered_Test", value = 0.1 }
                        }
                    }
                }
            }
            : new { name = $"变体 {index}", gear = new Dictionary<string, object>() })
        .ToArray<object>();
    var source = new
    {
        data = new
        {
            _id = "1Zep",
            title = "测试 BD",
            @char = "Rogue",
            season = 14,
            _updateTime = 1784561585999,
            variants
        }
    };
    return System.Text.Json.JsonSerializer.Serialize(new
    {
        data = new { response_data = System.Text.Json.JsonSerializer.Serialize(source) }
    });
}

static PublicBuildRecord CreateLibraryRecord(string buildId, DateTimeOffset sourceUpdatedAt) => new()
{
    BuildId = buildId,
    CanonicalUrl = $"https://www.d2core.com/d4/planner?bd={buildId}",
    Title = "测试公共 BD",
    ClassName = "Rogue",
    Season = 14,
    ContentHash = "test-hash",
    SourceUpdatedAt = sourceUpdatedAt,
    Variants = Enumerable.Range(0, 7)
        .Select(index => new BuildVariantRecord { Index = index, Name = $"变体 {index}" })
        .ToList()
};

static LocalUpdateValidationContext CreateLocalUpdateContext(
    string artifactDirectory,
    string currentVersion = "0.1.0") =>
    new("D4Hub", "stable", "win-x64", currentVersion, artifactDirectory);

static string CreateLocalUpdateManifestJson(
    byte[] payload,
    string fileName = "D4Hub-0.2.0-win-x64.zip",
    int schemaVersion = 1,
    string product = "D4Hub",
    string version = "0.2.0",
    string channel = "stable",
    string architecture = "win-x64",
    long? size = null,
    string? sha256 = null) =>
    System.Text.Json.JsonSerializer.Serialize(new
    {
        schemaVersion,
        product,
        version,
        channel,
        architecture,
        artifact = new
        {
            fileName,
            size = size ?? payload.LongLength,
            sha256 = sha256 ?? Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()
        }
    });

static (PixelFrame Frame, PanelDetection Detection, BuildFingerprintService Service, BuildVisualFingerprint Observed) CreateFingerprintTestContext()
{
    var frame = CreateSyntheticGameFrame();
    var detection = new CharacterPanelDetector().Detect(frame);
    var service = new BuildFingerprintService();
    return (frame, detection, service, service.Capture(frame, detection));
}

static BuildVisualFingerprint CreateFingerprintAtDistance(BuildVisualFingerprint baseline, int distance)
{
    if (distance is < 0 or > 192)
    {
        throw new ArgumentOutOfRangeException(nameof(distance));
    }

    var leftDistance = Math.Min(distance, 64);
    var centerDistance = Math.Min(distance - leftDistance, 64);
    var rightDistance = distance - leftDistance - centerDistance;
    return new BuildVisualFingerprint
    {
        LeftHash = ToggleLowBits(baseline.LeftHash, leftDistance),
        CenterHash = ToggleLowBits(baseline.CenterHash, centerDistance),
        RightHash = ToggleLowBits(baseline.RightHash, rightDistance)
    };
}

static string ToggleLowBits(string hash, int bitCount)
{
    var value = Convert.ToUInt64(hash, 16);
    var mask = bitCount switch
    {
        0 => 0UL,
        64 => ulong.MaxValue,
        _ => (1UL << bitCount) - 1
    };
    return (value ^ mask).ToString("X16");
}

static PixelFrame CreateSyntheticGameFrame(
    int width = 1000,
    int height = 600,
    int panelLeft = 620,
    int activeTop = 0,
    int? activeBottom = null,
    int titleHeight = 0,
    bool includeCharacterTitle = true)
{
    var contentBottom = activeBottom ?? height;
    var activeHeight = contentBottom - activeTop;
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            var isTitle = y < titleHeight;
            var isActive = y >= activeTop && y < contentBottom;
            var luminance = isTitle
                ? (byte)250
                : isActive
                ? x < panelLeft
                    ? (byte)(45 + ((x + y) % 9))
                    : (byte)(24 + ((x * 3 + y * 5) % 18))
                : (byte)0;
            if (isActive && Math.Abs(x - panelLeft) <= 2)
            {
                luminance = 190;
            }

            if (isActive
                && x >= panelLeft
                && y >= activeTop + activeHeight * 0.66
                && (x % 52 <= 2 || y % 42 <= 2))
            {
                luminance = 118;
            }

            if (isActive
                && x >= width * 0.18
                && x <= width * 0.39
                && y >= activeTop + activeHeight * 0.84
                && y <= activeTop + activeHeight * 0.96)
            {
                luminance = (byte)(35 + ((x / 13 + y / 11) % 8) * 25);
            }

            var offset = ((y * width) + x) * 4;
            pixels[offset] = luminance;
            pixels[offset + 1] = luminance;
            pixels[offset + 2] = luminance;
            pixels[offset + 3] = 255;
        }
    }

    if (includeCharacterTitle)
    {
        DrawSyntheticCharacterTitle(pixels, width, panelLeft, activeTop, activeHeight);
    }

    return new PixelFrame(width, height, pixels);
}

static void DrawSyntheticCharacterTitle(byte[] pixels, int frameWidth, int panelLeft, int activeTop, int activeHeight)
{
    const ulong characterTitleHash = 0x00140C1C1E1E0200UL;
    var left = (int)Math.Round(panelLeft + activeHeight * 0.031);
    var top = (int)Math.Round(activeTop + activeHeight * 0.057);
    var width = Math.Max(12, (int)Math.Round(activeHeight * 0.064));
    var height = Math.Max(8, (int)Math.Round(activeHeight * 0.026));

    for (var index = 0; index < 64; index++)
    {
        if ((characterTitleHash & (1UL << index)) == 0)
        {
            continue;
        }

        var column = index % 8;
        var row = index / 8;
        var x = left + (int)(width * ((column + 0.5) / 8));
        var y = top + (int)(height * ((row + 0.5) / 8));
        for (var offsetY = -1; offsetY <= 1; offsetY++)
        {
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                var target = (((y + offsetY) * frameWidth) + x + offsetX) * 4;
                pixels[target] = 190;
                pixels[target + 1] = 220;
                pixels[target + 2] = 235;
            }
        }
    }
}

static PixelFrame CreateSyntheticTransmutationFrame()
{
    const int width = 1000;
    const int height = 700;
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            var offset = ((y * width) + x) * 4;
            pixels[offset] = 28;
            pixels[offset + 1] = 28;
            pixels[offset + 2] = 28;
            pixels[offset + 3] = 255;
        }
    }

    for (var y = 250; y < 310; y++)
    {
        for (var x = 660; x < 900; x++)
        {
            var offset = ((y * width) + x) * 4;
            pixels[offset] = 18;
            pixels[offset + 1] = 36;
            pixels[offset + 2] = 182;
        }
    }

    return new PixelFrame(width, height, pixels);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

file sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"d4hub-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

file sealed class CountingD2CoreClient : ID2CoreBuildClient
{
    private readonly PublicBuildRecord _record;

    public CountingD2CoreClient(PublicBuildRecord record)
    {
        _record = record;
    }

    public int FetchCount { get; private set; }

    public Task<PublicBuildRecord> FetchAsync(D2CoreBuildReference reference, CancellationToken cancellationToken = default)
    {
        FetchCount++;
        return Task.FromResult(_record);
    }
}
