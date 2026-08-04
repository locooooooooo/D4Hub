using System.IO;
using System.Security.Cryptography;
using D4Hub.App.Services;
using D4Hub.App.ViewModels;
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
    ("statistics HUD settings default and persist", StatisticsHudSettingsDefaultAndPersist),
    ("statistics HUD placement avoids the minimap", StatisticsHudPlacementAvoidsMinimap),
    ("statistics HUD OCR exclusion covers mode transitions", StatisticsHudOcrExclusionCoversModeTransitions),
    ("HUD profile defaults", HudProfileContainsAllEquipmentSlots),
    ("HUD layout template round trip", HudLayoutTemplateRoundTrips),
    ("HUD layout templates isolate resolutions", HudLayoutTemplatesIsolateResolutions),
    ("HUD layout templates isolate profiles", HudLayoutTemplatesIsolateProfiles),
    ("HUD layout reset restores defaults", HudLayoutResetRestoresDefaults),
    ("transmutation reminder requires the selected recipe", TransmutationReminderRequiresSelectedRecipe),
    ("transmutation detector rejects combat-like crimson noise", TransmutationDetectorRejectsCombatLikeCrimsonNoise),
    ("transmutation detector is resolution independent", TransmutationDetectorIsResolutionIndependent),
    ("transmutation reminder uses enter and exit hysteresis", TransmutationReminderUsesEnterAndExitHysteresis),
    ("transmutation reminder reset hides immediately", TransmutationReminderResetHidesImmediately),
    ("combat damage text parsing", CombatDamageTextParsingSupportsChineseUnits),
    ("combat damage observations deduplicate", CombatDamageObservationsDeduplicateAcrossFrames),
    ("combat damage single-frame candidates fail closed", CombatDamageSingleFrameCandidatesFailClosed),
    ("combat damage default timing survives 0.6 second sampling", CombatDamageDefaultTimingSurvivesLiveSampling),
    ("combat damage simultaneous equal hits stay distinct", CombatDamageSimultaneousEqualHitsStayDistinct),
    ("combat damage repeated hits remain distinct", CombatDamageRepeatedHitsRemainDistinct),
    ("combat damage sessions split on inactivity", CombatDamageSessionsSplitOnInactivity),
    ("combat damage current one-second window and rejection receipt", CombatDamageCurrentWindowAndRejectionReceipt),
    ("combat damage average DPS advances then freezes", CombatDamageAverageDpsAdvancesThenFreezes),
    ("Diablo damage numbers use Chinese large units", DiabloDamageNumbersUseChineseLargeUnits),
    ("realtime statistics session maps trusted damage and reset", RealtimeStatisticsMapsTrustedDamageAndReset),
    ("realtime statistics distinguishes no data and insufficient evidence", RealtimeStatisticsDistinguishesNoDataAndInsufficientEvidence),
    ("realtime panel viewmodel exposes controls and metric bindings", RealtimePanelViewModelExposesControlsAndMetrics),
    ("realtime panel reports feature availability honestly", RealtimePanelReportsFeatureAvailabilityHonestly),
    ("realtime capture lifecycle is idempotent", RealtimeCaptureLifecycleIsIdempotent),
    ("realtime OCR scheduler is single flight", RealtimeOcrSchedulerIsSingleFlight),
    ("realtime OCR scheduler throttles frames", RealtimeOcrSchedulerThrottlesFrames),
    ("realtime 0.6 second pipeline confirms one moving popup once", RealtimeLiveCadenceConfirmsOnePopupOnce),
    ("realtime OCR discards stale foreground results", RealtimeOcrDiscardsStaleForegroundResults),
    ("realtime OCR failures remain visible", RealtimeOcrFailuresRemainVisible),
    ("combat OCR ROI maps calibrated pixel coordinates", CombatOcrRoiMapsCalibratedCoordinates),
    ("realtime OCR masks its statistics HUD", RealtimeOcrMasksItsStatisticsHud),
    ("combat OCR candidate mapping preserves evidence receipts", CombatOcrCandidateMappingPreservesEvidenceReceipts),
    ("combat OCR rejects missing decimal catastrophes", CombatOcrRejectsMissingDecimalCatastrophes),
    ("combat text model bundle fails closed when absent", CombatTextModelBundleFailsClosedWhenAbsent),
    ("vision calibration selects resolution language and display mode", VisionCalibrationSelectsCompatibleProfile),
    ("realtime unsupported HDR calibration fails closed", RealtimeUnsupportedHdrCalibrationFailsClosed),
    ("Paddle adapter success remains experimental", PaddleAdapterSuccessRemainsExperimental),
    ("Paddle adapter inference failure permanently falls back", PaddleAdapterInferenceFailurePermanentlyFallsBack),
    ("visible resource counters separate gain outflow and reset", VisibleResourceCountersSeparateChanges),
    ("visible progress parsing and reset tracking", VisibleProgressParsingAndResetTracking),
    ("visible buff uptime uses only contiguous observations", VisibleBuffUptimeUsesObservedIntervals),
    ("visible map markers expire without fabrication", VisibleMapMarkersExpireWithoutFabrication),
    ("local automation emits only edge-triggered local actions", LocalAutomationIsEdgeTriggeredAndLocal),
    ("character panel detection", CharacterPanelIsDetectedInSyntheticFrame),
    ("1080p character panel title placement", CharacterPanelDetects1080pTitlePlacement),
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
    ("bundled external resource catalog", BundledExternalResourceCatalogIsStrictAndUsable),
    ("external resource catalog rejects unsafe links", ExternalResourceCatalogRejectsUnsafeLinks),
    ("Helltides privacy request policy", HelltidesPrivacyRequestPolicyIsNarrow),
    ("Helltides privacy DOM sanitizer", HelltidesPrivacyDomSanitizerDoesNotFakeConsent),
    ("D2Core URL normalization", D2CoreUrlSelectsRequestedVariant),
    ("D2Core public sample uses one-based var", D2CorePublicSampleUsesHumanVariantNumber),
    ("D2Core metadata classification", D2CoreMetadataClassifiesModesAndPurposes),
    ("bundled library seeds classified variants", BundledLibrarySeedsClassifiedVariants),
    ("bundled defaults merge idempotently", BundledDefaultsMergeIdempotently),
    ("D2Core HUD text is compact", D2CoreHudTextIsCompact),
    ("HUD source markers can coexist", HudSourceMarkersCanCoexist),
    ("HUD transfigured poison affixes stay distinct and last", HudTransfiguredPoisonAffixesStayDistinctAndLast),
    ("D2Core legacy HUD affixes migrate", D2CoreLegacyHudAffixesMigrate),
    ("D2Core parser preserves affixes", D2CoreParserPreservesStructuredAffixes),
    ("D2Core profile maps all equipment", D2CoreProfileMapsSelectedVariant),
    ("Barbarian profile maps four weapons", BarbarianProfileMapsFourWeapons),
    ("legacy Barbarian profile gains fourth weapon", LegacyBarbarianProfileMigratesFourWeapons),
    ("loot filter code normalizes and classifies stages", LootFilterCodeNormalizesAndClassifiesStages),
    ("loot filter library round trips locally", LootFilterLibraryRoundTripsLocally),
    ("bundled loot filter seed is usable", BundledLootFilterSeedIsUsable),
    ("loot filter collection imports and filters", LootFilterCollectionImportsAndFilters),
    ("loot filter collection supports decision filters", LootFilterCollectionSupportsDecisionFilters),
    ("public library avoids network", PublicLibraryHitAvoidsNetwork),
    ("cache miss fetches once", CacheMissFetchesAndPersistsOnce),
    ("map HUD settings default and persist", MapHudSettingsDefaultAndPersist),
    ("map HUD placement anchors to the game window", MapHudPlacementAnchorsToGameWindow),
    ("world event clock evaluates schedule and manual offset", WorldEventClockEvaluatesScheduleAndManualOffset),
    ("POI catalog rejects invalid records", PoiCatalogRejectsInvalidRecords),
    ("world event edge tracker reports rising events once", WorldEventEdgeTrackerReportsRisingEventsOnce),
    ("map HUD hotkey and audio settings persist", MapHudHotkeyAndAudioSettingsPersist)
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

static void CombatDamageTextParsingSupportsChineseUnits()
{
    var values = CombatDamageTextParser.ParseAll("943.0万 1,060万 1.05亿 1.25兆 2京");
    Assert(
        values.SequenceEqual(new[]
        {
            9_430_000L,
            10_600_000L,
            105_000_000L,
            1_250_000_000_000L,
            20_000_000_000_000_000L
        }),
        "the restricted Chinese large-unit domain should parse into absolute damage");
    Assert(
        CombatDamageTextParser.ParseAll("获得声望: 10,080 / 21,250").Count == 0,
        "plain UI counters without a damage unit should be rejected");
    Assert(
        !CombatDamageTextParser.TryParse("1.5万 2.5万", out _),
        "single-value parsing should reject merged OCR lines with multiple damage values");
}

static void CombatDamageObservationsDeduplicateAcrossFrames()
{
    var tracker = new CombatDamageTracker();
    tracker.AddFrame(0, [DamageObservation(10_000_000, 0, 500, 300)]);
    tracker.AddFrame(0.1, [DamageObservation(10_000_000, 0.1, 504, 290)]);
    tracker.AddFrame(0.2, [DamageObservation(10_000_000, 0.2, 509, 280)]);

    var report = tracker.BuildReport();
    Assert(report.ParsedObservationCount == 3, "all OCR observations should be counted");
    Assert(report.UniqueEventCount == 1, "one moving popup should become one damage event");
    Assert(report.DuplicateObservationCount == 2, "later frames should be collapsed as duplicate observations");
    Assert(report.TotalDamage == 10_000_000, "deduplication should not inflate total damage");
}

static void CombatDamageSingleFrameCandidatesFailClosed()
{
    var tracker = new CombatDamageTracker(trackLifetimeSeconds: 0.4);
    tracker.AddFrame(0, [DamageObservation(358_000_000, 0, 500, 300)]);

    var pending = tracker.BuildReport();
    Assert(pending.UniqueEventCount == 0 && pending.TotalDamage == 0,
        "a single parsed OCR candidate must not become a damage event");
    Assert(pending.Evidence.State == CombatEvidenceState.InsufficientEvidence
        && pending.Evidence.PendingObservationCount == 1,
        "the pending candidate should be visible as insufficient evidence");

    tracker.AddFrame(0.5, []);
    var expired = tracker.BuildReport();
    Assert(expired.RejectionReasons["insufficient-multiframe-evidence"] == 1,
        "a short-lived popup that cannot be confirmed should fail closed with a receipt");
}

static void CombatDamageDefaultTimingSurvivesLiveSampling()
{
    var session = new RealtimeStatisticsSession(damageSamplingIntervalSeconds: 0.6);
    session.Start();
    session.AddFrame(
        0,
        DamageReadout(DamageObservation(35_800_000, 0, 500, 310)));
    var confirmed = session.AddFrame(
        0.6,
        DamageReadout(DamageObservation(35_800_000, 0.6, 506, 286)));

    Assert(confirmed.AcceptedDamageEvents == 1 && confirmed.TotalDamage == 35_800_000,
        "the live 0.6 second cadence must retain a popup long enough to confirm it once");
    Assert(confirmed.RecentOneSecondDamage == 35_800_000,
        "the confirmed event should enter the rolling one-second window once");
}

static void CombatDamageSimultaneousEqualHitsStayDistinct()
{
    var tracker = new CombatDamageTracker();
    tracker.AddFrame(0,
    [
        DamageObservation(25_000_000, 0, 400, 340),
        DamageObservation(25_000_000, 0, 800, 340)
    ]);
    tracker.AddFrame(0.1,
    [
        DamageObservation(25_000_000, 0.1, 405, 325),
        DamageObservation(25_000_000, 0.1, 805, 325)
    ]);

    var report = tracker.BuildReport();
    Assert(report.UniqueEventCount == 2 && report.TotalDamage == 50_000_000,
        "equal values with independent spatial tracks must remain separate hits");
}

static void CombatDamageRepeatedHitsRemainDistinct()
{
    var tracker = new CombatDamageTracker(trackLifetimeSeconds: 0.4);
    tracker.AddFrame(0, [DamageObservation(25_000_000, 0, 700, 350)]);
    tracker.AddFrame(0.1, [DamageObservation(25_000_000, 0.1, 705, 340)]);
    tracker.AddFrame(0.6, [DamageObservation(25_000_000, 0.6, 701, 352)]);
    tracker.AddFrame(0.7, [DamageObservation(25_000_000, 0.7, 706, 342)]);

    var report = tracker.BuildReport();
    Assert(report.UniqueEventCount == 2, "a repeated value after the popup lifetime should remain a distinct hit");
    Assert(report.TotalDamage == 50_000_000, "separate repeated hits should both contribute damage");
}

static void CombatDamageSessionsSplitOnInactivity()
{
    var tracker = new CombatDamageTracker(trackLifetimeSeconds: 0.2, sessionGapSeconds: 4);
    tracker.AddFrame(0, [DamageObservation(10_000_000, 0, 400, 300)]);
    tracker.AddFrame(0.1, [DamageObservation(10_000_000, 0.1, 405, 290)]);
    tracker.AddFrame(0.5, [DamageObservation(20_000_000, 0.5, 800, 300)]);
    tracker.AddFrame(0.6, [DamageObservation(20_000_000, 0.6, 805, 290)]);
    tracker.AddFrame(5, [DamageObservation(30_000_000, 5, 600, 300)]);
    tracker.AddFrame(5.1, [DamageObservation(30_000_000, 5.1, 605, 290)]);

    var report = tracker.BuildReport();
    Assert(report.Sessions.Count == 2, "more than four seconds without damage should start a new session");
    Assert(report.Sessions[0].TotalDamage == 30_000_000, "the first session should include its two hits");
    Assert(report.Sessions[0].PeakOneSecondDamage == 30_000_000, "one-second peak should sum nearby hits");
    Assert(report.Sessions[1].MaximumHit == 30_000_000, "the second session should retain its maximum hit");
}

static void CombatDamageCurrentWindowAndRejectionReceipt()
{
    var tracker = new CombatDamageTracker(trackLifetimeSeconds: 0.2);
    tracker.AddFrame(0, [DamageObservation(10_000_000, 0, 400, 300)]);
    tracker.AddFrame(0.1, [DamageObservation(10_000_000, 0.1, 405, 290)]);
    tracker.AddFrame(0.5, [DamageObservation(20_000_000, 0.5, 800, 300)]);
    tracker.AddFrame(0.6, [DamageObservation(20_000_000, 0.6, 805, 290)]);
    tracker.AddFrame(2, [DamageObservation(5_000_000, 2, 600, 300)]);
    tracker.AddFrame(2.1,
        [DamageObservation(5_000_000, 2.1, 605, 290)]);
    tracker.AddFrame(2.2,
    [
        new CombatTextObservation(999_000_000, 2.2, 700, 300, 80, 32, "999亿", 0.2),
        new CombatTextObservation(888_000_000, 2.2, 720, 300, 80, 32, "888亿", 0.9, "missing-decimal-risk")
    ]);

    var report = tracker.BuildReport();
    Assert(report.CurrentOneSecondDamage == 5_000_000, "current one-second damage should use report time instead of the historical peak");
    Assert(report.Sessions[0].PeakOneSecondDamage == 30_000_000, "historical one-second peak should remain available separately");
    Assert(report.RejectedObservationCount == 2, "low-confidence and explicitly suspicious OCR candidates should be rejected");
    Assert(report.RejectionReasons["low-evidence"] == 1, "low-evidence rejection should have a receipt");
    Assert(report.RejectionReasons["missing-decimal-risk"] == 1, "format-risk rejection should have a receipt");
}

static void CombatDamageAverageDpsAdvancesThenFreezes()
{
    var tracker = new CombatDamageTracker(sessionGapSeconds: 4);
    tracker.AddFrame(0, [DamageObservation(40_000_000, 0, 500, 300)]);
    tracker.AddFrame(0.1, [DamageObservation(40_000_000, 0.1, 505, 290)]);
    tracker.AddFrame(2, []);
    var duringGap = tracker.BuildReport().Sessions.Single();
    Assert(duringGap.IsActive && duringGap.AverageDps == 20_000_000,
        "average DPS denominator should advance while the combat-end gap is open");

    tracker.AddFrame(6, []);
    var frozen = tracker.BuildReport().Sessions.Single();
    Assert(!frozen.IsActive && frozen.EndSeconds == 4 && frozen.AverageDps == 10_000_000,
        "average DPS should freeze at the explicit four-second combat timeout");
}

static void RealtimeStatisticsMapsTrustedDamageAndReset()
{
    var session = new RealtimeStatisticsSession();
    session.Start();
    var pending = session.AddFrame(
        0,
        DamageReadout(DamageObservation(12_000_000, 0, 600, 300)));
    Assert(pending.Status == RealtimeCaptureStatus.InsufficientEvidence && pending.TotalDamage == 0,
        "a single trusted-looking readout must remain pending");
    var snapshot = session.AddFrame(
        0.6,
        DamageReadout(DamageObservation(12_000_000, 0.6, 605, 285)));

    Assert(snapshot.Status == RealtimeCaptureStatus.Capturing, "a trusted visible hit should put the panel in capturing state");
    Assert(snapshot.HasData, "a trusted visible hit should make data available");
    Assert(snapshot.CurrentDps == 12_000_000, "the current session DPS should be mapped into the realtime snapshot");
    Assert(snapshot.RecentOneSecondDamage == 12_000_000, "the current one-second damage should be mapped into the realtime snapshot");
    Assert(snapshot.TotalDamage == 12_000_000, "the realtime total must equal accepted visible events only");

    session.Reset();
    snapshot = session.Snapshot;
    Assert(snapshot.Status == RealtimeCaptureStatus.NoData, "reset should keep an enabled session ready for the next frame");
    Assert(snapshot.TotalDamage == 0 && !snapshot.HasData, "reset should clear the current capture session");
}

static void DiabloDamageNumbersUseChineseLargeUnits()
{
    Assert(DiabloNumberFormatter.Format(9_999) == "9,999", "values below ten thousand should remain unabridged");
    Assert(DiabloNumberFormatter.Format(12_700) == "1.27万", "ten-thousand values should use three significant digits");
    Assert(DiabloNumberFormatter.Format(170_000) == "17.0万", "mid-range ten-thousand values should retain one decimal");
    Assert(DiabloNumberFormatter.Format(100_000_000) == "1.00亿", "hundred-million values should use 亿");
    Assert(DiabloNumberFormatter.Format(9_680_000_000) == "96.8亿", "large 亿 values should retain three significant digits");
    Assert(DiabloNumberFormatter.Format(1_000_000_000_000) == "1.00兆", "trillion values should use 兆");
    Assert(DiabloNumberFormatter.Format(10_000_000_000_000_000) == "1.00京", "ten-quadrillion values should use 京");
    AssertThrows<ArgumentOutOfRangeException>(
        () => DiabloNumberFormatter.Format(-1),
        "negative damage values should be rejected");
}

static void RealtimeStatisticsDistinguishesNoDataAndInsufficientEvidence()
{
    var session = new RealtimeStatisticsSession();
    session.Start();

    var noData = session.AddFrame(0, RealtimeVisionReadout.Empty);
    Assert(noData.Status == RealtimeCaptureStatus.NoData, "an empty OCR readout must stay explicitly unavailable");
    Assert(noData.TotalDamage == 0, "an empty OCR readout must never fabricate damage");

    var insufficient = session.AddFrame(
        0.1,
        new RealtimeVisionReadout(
            [new CombatTextObservation(50_000_000, 0.1, 600, 300, 80, 32, "5000万", 0.2)],
            [],
            [],
            [],
            [],
            0.2));
    Assert(insufficient.Status == RealtimeCaptureStatus.InsufficientEvidence,
        "a rejected OCR readout should be visible as insufficient evidence rather than an accuracy percentage");
    Assert(insufficient.TotalDamage == 0, "insufficient OCR evidence must not contribute to damage totals");
    Assert(insufficient.RejectedDamageObservations == 1, "rejected OCR evidence should retain a rejection receipt");

    session.Pause();
    var paused = session.MarkWaitingForGame(0.2);
    Assert(paused.Status == RealtimeCaptureStatus.Paused, "host window polling must not override a user pause");
}

static void RealtimePanelViewModelExposesControlsAndMetrics()
{
    var viewModel = new RealtimePanelViewModel();
    Assert(viewModel.IsPanelVisible, "the realtime panel switch should default to visible so users can discover it");
    Assert(viewModel.IsCollectionEnabled, "realtime visible-screen collection should start ready for captured frames");
    Assert(viewModel.DpsText == "--", "the panel must show an unavailable marker before trusted OCR exists");

    viewModel.ToggleCollectionCommand.Execute(null);
    Assert(!viewModel.IsCollectionEnabled && viewModel.Status == RealtimeCaptureStatus.Paused,
        "the panel command should pause collection without clearing the current session");
    viewModel.ToggleCollectionCommand.Execute(null);
    viewModel.ApplyReadout(
        0,
        DamageReadout(DamageObservation(8_000_000, 0, 500, 280)));
    Assert(viewModel.DpsText == "--" && viewModel.DataQualityText == "低覆盖",
        "one frame should stay unavailable and quality must not be shown as an accuracy percentage");
    viewModel.ApplyReadout(
        0.6,
        DamageReadout(DamageObservation(8_000_000, 0.6, 505, 265)));
    Assert(viewModel.DpsText == "800万" && viewModel.TotalDamageText == "800万",
        "trusted Core metrics should use the Diablo Chinese-unit formatter");
    Assert(viewModel.DataQualityText == "基线估算",
        "legacy OCR evidence should be labelled as a baseline estimate, not a percentage");

    viewModel.ResetStatisticsCommand.Execute(null);
    Assert(viewModel.TotalDamage == 0 && viewModel.TotalDamageText == "--",
        "the clear command should reset numeric bindings instead of retaining stale values");
    viewModel.IsPanelVisible = false;
    Assert(viewModel.PanelVisibilityText == "显示实时面板", "the panel switch should expose its hidden state to the UI");
}

static void RealtimePanelReportsFeatureAvailabilityHonestly()
{
    var viewModel = new RealtimePanelViewModel();
    Assert(viewModel.ExperienceText == "不可用"
        && viewModel.GoldText == "不可用"
        && viewModel.MaterialsText == "不可用"
        && viewModel.BuffStatusText == "不可用"
        && viewModel.ProgressStatusText == "不可用"
        && viewModel.MapStatusText == "不可用",
        "the damage-only live adapter must not present unimplemented OCR features as empty measurements");
    Assert(!viewModel.IsAutomationEnabled && viewModel.AutomationStatusText == "已禁用",
        "local automation must remain explicitly disabled until it is wired to a user-approved workflow");

    viewModel.ApplyReadout(
        0,
        new RealtimeVisionReadout(
            [],
            [
                new VisibleCounterObservation("xp", "经验", VisibleCounterKind.Experience, 100, 0, 0.9),
                new VisibleCounterObservation("gold", "金币", VisibleCounterKind.Gold, 200, 0, 0.9),
                new VisibleCounterObservation("iron", "铁块", VisibleCounterKind.Material, 3, 0, 0.9)
            ],
            [new VisibleProgressObservation("renown", "声望", new VisibleProgressValue(10, 100), 0, 0.9)],
            [new VisibleBuffObservation("barrier", "屏障", 1, 4, 0.9)],
            [new VisibleMapMarkerObservation("event", "event", "军团集结", 0.4, 0.3, 0.9)],
            0.9));
    viewModel.ApplyReadout(
        1,
        new RealtimeVisionReadout(
            [],
            [
                new VisibleCounterObservation("xp", "经验", VisibleCounterKind.Experience, 150, 1, 0.9),
                new VisibleCounterObservation("gold", "金币", VisibleCounterKind.Gold, 250, 1, 0.9),
                new VisibleCounterObservation("iron", "铁块", VisibleCounterKind.Material, 5, 1, 0.9)
            ],
            [new VisibleProgressObservation("renown", "声望", new VisibleProgressValue(25, 100), 1, 0.9)],
            [new VisibleBuffObservation("barrier", "屏障", 2, 3, 0.9)],
            [new VisibleMapMarkerObservation("event", "event", "军团集结", 0.4, 0.3, 0.9)],
            0.9));

    Assert(viewModel.ExperienceText == "50"
        && viewModel.GoldText == "50"
        && viewModel.MaterialsText == "2",
        "fixture readouts should flow through the same counter aggregation and panel bindings");
    Assert(viewModel.BuffStatusText == "屏障 x2"
        && viewModel.ProgressStatusText == "声望 25%"
        && viewModel.MapStatusText == "1 个可见标记",
        "fixture buff, progress, and map observations should remain distinguishable from unavailable live features");
}

static void RealtimeCaptureLifecycleIsIdempotent()
{
    var starts = 0;
    var stops = 0;
    var lifecycle = new RealtimeCaptureLifecycle(() => starts++, () => stops++);

    Assert(lifecycle.Start() && lifecycle.IsRunning, "the first start should enter the running state");
    Assert(!lifecycle.Start() && starts == 1,
        "repeated Loaded/start signals must not start or subscribe the capture loop twice");
    Assert(lifecycle.Stop() && !lifecycle.IsRunning, "the first stop should leave the running state");
    Assert(!lifecycle.Stop() && stops == 1,
        "repeated Closing/stop signals must not stop the underlying loop twice");
}

static void RealtimeOcrSchedulerIsSingleFlight()
{
    var adapter = new ControlledRealtimeVisionAdapter();
    var viewModel = new RealtimePanelViewModel(adapter, minimumOcrIntervalSeconds: 0);
    var frame = new PixelFrame(1920, 1080, new byte[1920 * 1080 * 4]);

    Assert(viewModel.CaptureFrame(frame, 0), "the first frame should schedule OCR immediately");
    Assert(adapter.WaitUntilEntered(), "the asynchronous OCR worker should start");
    Assert(!viewModel.CaptureFrame(frame, 0.3), "a frame arriving during OCR must be dropped");
    Assert(adapter.CallCount == 1 && adapter.MaximumConcurrentCalls == 1,
        "single-flight scheduling must never enter a second adapter call");
    Assert(viewModel.DroppedBusyFrameCount == 1, "busy frame drops should be observable");

    adapter.Complete(RealtimeVisionReadout.Empty);
    Assert(SpinWait.SpinUntil(() => !viewModel.IsOcrInFlight, TimeSpan.FromSeconds(3)),
        "the OCR slot should be released after completion");
}

static void RealtimeOcrSchedulerThrottlesFrames()
{
    var adapter = new ImmediateRealtimeVisionAdapter();
    var viewModel = new RealtimePanelViewModel(adapter, minimumOcrIntervalSeconds: 0.6);
    var frame = new PixelFrame(1920, 1080, new byte[1920 * 1080 * 4]);

    Assert(viewModel.CaptureFrame(frame, 0), "the initial frame should be accepted");
    Assert(SpinWait.SpinUntil(() => !viewModel.IsOcrInFlight, TimeSpan.FromSeconds(3)),
        "the immediate adapter should release the OCR slot");
    Assert(!viewModel.CaptureFrame(frame, 0.3), "a frame inside the throttle interval should be dropped");
    Assert(viewModel.DroppedThrottledFrameCount == 1 && adapter.CallCount == 1,
        "throttled frames must not invoke the OCR adapter");
    Assert(viewModel.CaptureFrame(frame, 0.6), "a frame at the throttle boundary should be accepted");
    Assert(SpinWait.SpinUntil(() => adapter.CallCount == 2 && !viewModel.IsOcrInFlight, TimeSpan.FromSeconds(3)),
        "the next eligible frame should run after the throttle interval");
}

static void RealtimeLiveCadenceConfirmsOnePopupOnce()
{
    var adapter = new TimedDamageRealtimeVisionAdapter();
    var viewModel = new RealtimePanelViewModel(adapter, minimumOcrIntervalSeconds: 0.6);
    var frame = new PixelFrame(1920, 1080, new byte[1920 * 1080 * 4]);

    Assert(viewModel.CaptureFrame(frame, 0), "the first live sample should be scheduled");
    Assert(SpinWait.SpinUntil(() => !viewModel.IsOcrInFlight, TimeSpan.FromSeconds(3)),
        "the first live sample should finish");
    Assert(viewModel.TotalDamage == 0 && viewModel.Status == RealtimeCaptureStatus.InsufficientEvidence,
        "the first live sample must remain pending");

    Assert(viewModel.CaptureFrame(frame, 0.6), "the second live sample should be scheduled at the boundary");
    Assert(SpinWait.SpinUntil(() => !viewModel.IsOcrInFlight, TimeSpan.FromSeconds(3)),
        "the second live sample should finish");
    Assert(viewModel.AcceptedDamageEvents == 1 && viewModel.TotalDamage == 35_800_000,
        "the 0.6 second live path must confirm and count the moving popup exactly once");
}

static void RealtimeOcrDiscardsStaleForegroundResults()
{
    var adapter = new ControlledRealtimeVisionAdapter();
    var viewModel = new RealtimePanelViewModel(adapter, minimumOcrIntervalSeconds: 0);
    var frame = new PixelFrame(1920, 1080, new byte[1920 * 1080 * 4]);

    Assert(viewModel.CaptureFrame(frame, 0), "the foreground frame should schedule OCR");
    Assert(adapter.WaitUntilEntered(), "the asynchronous OCR worker should start");
    viewModel.MarkWaitingForGame(0.1);
    adapter.Complete(new RealtimeVisionReadout(
        [DamageObservation(42_000_000, 0, 500, 280)],
        [],
        [],
        [],
        [],
        0.95));

    Assert(SpinWait.SpinUntil(() => !viewModel.IsOcrInFlight, TimeSpan.FromSeconds(3)),
        "the stale OCR worker should still release its slot");
    Assert(viewModel.Status == RealtimeCaptureStatus.WaitingForGame
        && !viewModel.HasData
        && viewModel.TotalDamage == 0,
        "a result completed after foreground loss must not enter realtime totals");
}

static void RealtimeOcrFailuresRemainVisible()
{
    var viewModel = new RealtimePanelViewModel(new ThrowingRealtimeVisionAdapter(), minimumOcrIntervalSeconds: 0);
    var frame = new PixelFrame(1920, 1080, new byte[1920 * 1080 * 4]);

    Assert(viewModel.CaptureFrame(frame, 0), "an OCR failure should still begin asynchronously");
    Assert(SpinWait.SpinUntil(
            () => !viewModel.IsOcrInFlight && viewModel.Status == RealtimeCaptureStatus.Error,
            TimeSpan.FromSeconds(3)),
        "an OCR adapter exception should become a visible panel error");
    Assert(viewModel.DataAvailabilityText.Contains("test OCR unavailable", StringComparison.Ordinal),
        "the panel should expose the OCR failure instead of showing fabricated values");
    Assert(!viewModel.HasData && viewModel.TotalDamage == 0,
        "an OCR failure must leave the statistics unavailable");
}

static void CombatOcrRoiMapsCalibratedCoordinates()
{
    var pixels = new byte[1920 * 1080 * 4];
    var firstSourceOffset = ((0 * 1920) + 100) * 4;
    pixels[firstSourceOffset] = 11;
    pixels[firstSourceOffset + 1] = 22;
    pixels[firstSourceOffset + 2] = 33;
    pixels[firstSourceOffset + 3] = 255;
    var frame = new PixelFrame(1920, 1080, pixels);
    var combat = D4VisionCalibrationProfiles.All
        .Single(profile => profile.Id == "1080p-zhCN-sdr")
        .Regions["combat"];

    var bounds = VisionRegionPixels.GetPixelBounds(frame, combat.Bounds);
    Assert(bounds == new PixelRect(100, 0, 1400, 800),
        "the calibrated normalized ROI should map to the verified 1080p combat crop");
    var extracted = VisionRegionPixels.ExtractBgra(frame, combat.Bounds);
    Assert(extracted.Width == 1400 && extracted.Height == 800,
        "OCR input should contain only the combat-text ROI");
    Assert(extracted.BgraPixels[0] == 11
        && extracted.BgraPixels[1] == 22
        && extracted.BgraPixels[2] == 33,
        "ROI extraction should begin at the calibrated source pixel");

    var limited = VisionRegionPixels.ExtractBgra(frame, combat.Bounds, maximumDimension: 700);
    Assert(limited.Width == 700 && limited.Height == 400,
        "oversized OCR regions should be reduced within the Windows OCR dimension limit");
    Assert(limited.SourceBounds == bounds
        && limited.SourcePixelsPerOutputPixelX == 2
        && limited.SourcePixelsPerOutputPixelY == 2,
        "downscaled OCR coordinates must retain an exact mapping to the source frame");
}

static void RealtimeOcrMasksItsStatisticsHud()
{
    var pixels = Enumerable.Repeat((byte)73, 1920 * 1080 * 4).ToArray();
    var frame = new PixelFrame(1920, 1080, pixels);
    var adapter = new InspectingRealtimeVisionAdapter();
    var viewModel = new RealtimePanelViewModel(adapter, minimumOcrIntervalSeconds: 0);
    var exclusion = new PixelRect(1320, 44, 296, 178);

    Assert(viewModel.CaptureFrame(frame, 0, exclusion),
        "the first foreground frame should be scheduled for OCR");
    Assert(adapter.WaitUntilRead(), "the OCR adapter should receive the scheduled frame");
    var observed = adapter.Frame ?? throw new InvalidOperationException("OCR frame was not recorded.");
    var maskedOffset = ((exclusion.Y * observed.Width) + exclusion.X) * 4;
    Assert(observed.Pixels[maskedOffset] == 0
        && observed.Pixels[maskedOffset + 1] == 0
        && observed.Pixels[maskedOffset + 2] == 0
        && observed.Pixels[maskedOffset + 3] == 255,
        "the visible statistics HUD must be blacked out before OCR");
    Assert(frame.Pixels[maskedOffset] == 73,
        "masking the OCR copy must not alter the frame used by other detectors");
    Assert(observed.Pixels[0] == 73,
        "pixels outside the statistics HUD must remain available to OCR");
}

static void CombatOcrCandidateMappingPreservesEvidenceReceipts()
{
    var trusted = CombatOcrObservationMapper.ReadDamageObservations(
        [new CombatOcrLine(
        [
            new CombatOcrWord("943.0", 10, 20, 50, 20),
            new CombatOcrWord("万", 60, 20, 20, 20)
        ])],
        1,
        sourceOffsetX: 100,
        sourceOffsetY: 50,
        sourceScaleX: 2,
        sourceScaleY: 2).Single();
    Assert(trusted.Damage == 9_430_000 && trusted.Confidence == 0.90 && trusted.RejectionReason is null,
        "decimal Chinese damage should retain the CombatProbe trusted confidence rule");
    Assert(trusted.CenterX == 190 && trusted.CenterY == 110,
        "OCR word coordinates should map from the cropped bitmap back to the full frame");

    var risky = CombatOcrObservationMapper.ReadDamageObservations(
        [new CombatOcrLine(
        [
            new CombatOcrWord("12345", 10, 20, 50, 20),
            new CombatOcrWord("万", 60, 20, 20, 20)
        ])],
        1).Single();
    Assert(risky.Confidence == 0.35 && risky.RejectionReason == "missing-decimal-risk",
        "five-digit integer OCR must retain the CombatProbe missing-decimal rejection receipt");

    var tracker = new CombatDamageTracker();
    tracker.AddFrame(1, [trusted, risky]);
    tracker.AddFrame(1.1, [trusted with { TimeSeconds = 1.1, CenterY = trusted.CenterY - 10 }]);
    var report = tracker.BuildReport();
    Assert(report.TotalDamage == trusted.Damage && report.RejectedObservationCount == 1,
        "only the trusted candidate should enter realtime damage totals");
}

static void CombatOcrRejectsMissingDecimalCatastrophes()
{
    var observations = CombatOcrObservationMapper.ReadDamageObservations(
    [
        new CombatOcrLine([new CombatOcrWord("358亿", 0, 0, 80, 24)]),
        new CombatOcrLine([new CombatOcrWord("170万", 0, 40, 80, 24)]),
        new CombatOcrLine([new CombatOcrWord("3.58亿", 0, 80, 80, 24)]),
        new CombatOcrLine([new CombatOcrWord("17.0万", 0, 120, 80, 24)])
    ], 0);

    Assert(observations[0].RejectionReason == "missing-decimal-risk"
        && observations[1].RejectionReason == "missing-decimal-risk",
        "ungrouped integer OCR aliases that can hide a decimal point must fail closed");
    Assert(observations[2].RejectionReason is null && observations[3].RejectionReason is null,
        "explicit decimal values should remain eligible for multi-frame confirmation");
}

static void CombatTextModelBundleFailsClosedWhenAbsent()
{
    using var workspace = new TemporaryDirectory();
    var availability = CombatTextModelBundleValidator.Validate(workspace.Path);
    Assert(!availability.IsAvailable && availability.Code == "manifest-missing",
        "a missing offline detector/recognizer bundle must report an explicit unavailable state");
    Assert(availability.VerifiedAssets.Count == 0,
        "missing assets must never produce a synthetic verification receipt");
}

static void VisionCalibrationSelectsCompatibleProfile()
{
    var selected = VisionCalibrationCatalog.SelectClosest(
        D4VisionCalibrationProfiles.All,
        3440,
        1440,
        "zh-cn",
        VisionDisplayMode.StandardDynamicRange);
    Assert(selected?.Id == "1440p-zhCN-sdr", "widescreen should select a calibration by content height before width");
    Assert(selected!.Regions["combat"].Bounds == new NormalizedRect(100d / 1920, 0, 1400d / 1920, 800d / 1080),
        "the default combat region should preserve the verified 1080p video crop in normalized coordinates");
    Assert(
        VisionCalibrationCatalog.SelectClosest(
            D4VisionCalibrationProfiles.All,
            1920,
            1080,
            "zh-CN",
            VisionDisplayMode.HighDynamicRange) is null,
        "SDR calibration must not be silently reused for HDR");
    AssertThrows<ArgumentOutOfRangeException>(
        () => new VisionCalibrationProfile(
            "invalid",
            1920,
            1080,
            "zh-CN",
            VisionDisplayMode.StandardDynamicRange,
            70,
            0.8,
            0.05,
            [new VisionRegionDefinition("outside", VisionRegionKind.Progress, new NormalizedRect(0.9, 0.9, 0.2, 0.2))]),
        "calibration must reject regions outside the visible frame");
}

static void RealtimeUnsupportedHdrCalibrationFailsClosed()
{
    var viewModel = new RealtimePanelViewModel(
        minimumOcrIntervalSeconds: 0,
        languageTag: "zh-CN",
        displayMode: VisionDisplayMode.HighDynamicRange);
    var frame = new PixelFrame(1920, 1080, new byte[1920 * 1080 * 4]);

    Assert(!viewModel.CaptureFrame(frame, 0),
        "capture must not silently reuse an SDR profile for HDR");
    Assert(viewModel.DataQuality.Level == RealtimeVisionQualityLevel.Unavailable
        && viewModel.DataQuality.Detail.Contains("HighDynamicRange", StringComparison.Ordinal),
        "the unsupported resolution/language/HDR combination should remain visible and unavailable");
}

static void PaddleAdapterSuccessRemainsExperimental()
{
    using var engine = new FixtureCombatTextSpottingEngine(throwOnRead: false);
    using var adapter = new ExperimentalCombatVisionAdapter(
        new FixtureBaselineVisionAdapter(),
        () => engine);
    var frame = new PixelFrame(1920, 1080, new byte[1920 * 1080 * 4]);
    var calibration = D4VisionCalibrationProfiles.All.Single(profile =>
        profile.Id == "1080p-zhCN-sdr");

    var readout = adapter.ReadAsync(frame, calibration, 0).GetAwaiter().GetResult();
    Assert(readout.Quality?.Level == RealtimeVisionQualityLevel.ExperimentalVisualEstimate,
        "successful Paddle inference must remain experimental until replay calibration passes");
    Assert(readout.Quality?.Level != RealtimeVisionQualityLevel.CalibratedVisualEstimate,
        "runtime loading must never be presented as calibrated accuracy");
    Assert(adapter.ActivePipeline == "paddleocr-v5-experimental" && adapter.FallbackReason is null,
        "a successful fixture inference should expose the experimental pipeline without fallback");
}

static void PaddleAdapterInferenceFailurePermanentlyFallsBack()
{
    var factoryCalls = 0;
    using var engine = new FixtureCombatTextSpottingEngine(throwOnRead: true);
    var baseline = new FixtureBaselineVisionAdapter();
    using var adapter = new ExperimentalCombatVisionAdapter(
        baseline,
        () =>
        {
            factoryCalls++;
            return engine;
        });
    var frame = new PixelFrame(1920, 1080, new byte[1920 * 1080 * 4]);
    var calibration = D4VisionCalibrationProfiles.All.Single(profile =>
        profile.Id == "1080p-zhCN-sdr");

    var first = adapter.ReadAsync(frame, calibration, 0).GetAwaiter().GetResult();
    var second = adapter.ReadAsync(frame, calibration, 0.6).GetAwaiter().GetResult();

    Assert(factoryCalls == 1 && engine.ReadCount == 1 && baseline.ReadCount == 2,
        "an inference failure must trip a permanent process-lifetime fallback without rebuilding Paddle");
    Assert(adapter.ActivePipeline == "windows-ocr-baseline"
        && adapter.FallbackReason?.Contains("inference", StringComparison.Ordinal) == true,
        "the active pipeline and inference-stage reason should remain observable");
    Assert(first.Quality?.Level == RealtimeVisionQualityLevel.BaselineScreenEstimate
        && second.Quality?.Level == RealtimeVisionQualityLevel.BaselineScreenEstimate,
        "the failed frame and every later frame should use the Windows baseline receipt");
}

static void VisibleResourceCountersSeparateChanges()
{
    var tracker = new VisibleCounterTracker();
    tracker.Add(new VisibleCounterObservation("gold", "金币", VisibleCounterKind.Gold, 100, 0, 0.9));
    tracker.Add(new VisibleCounterObservation("gold", "金币", VisibleCounterKind.Gold, 150, 1, 0.9));
    tracker.Add(new VisibleCounterObservation("gold", "金币", VisibleCounterKind.Gold, 120, 2, 0.9));
    tracker.Add(new VisibleCounterObservation("xp", "经验", VisibleCounterKind.Experience, 80, 0, 0.9));
    tracker.Add(new VisibleCounterObservation("xp", "经验", VisibleCounterKind.Experience, 10, 1, 0.9));
    tracker.Add(new VisibleCounterObservation("iron", "铁块", VisibleCounterKind.Material, 999, 1, 0.2));

    var report = tracker.BuildReport();
    var gold = report.Counters.Single(counter => counter.Key == "gold");
    var experience = report.Counters.Single(counter => counter.Key == "xp");
    Assert(gold.TotalGain == 50 && gold.TotalOutflow == 30, "gold gains and spending should remain separate");
    Assert(experience.ResetCount == 1 && experience.TotalOutflow == 0, "experience rollover should be a reset, not spending");
    Assert(report.RejectedObservationCount == 1, "low-confidence material observations should not create a counter");
    Assert(report.Counters.All(counter => counter.Key != "iron"), "rejected material values must not be fabricated in the report");
}

static void VisibleProgressParsingAndResetTracking()
{
    Assert(
        VisibleProgressTextParser.TryParse("声望 10,080 / 21,250", out var fraction)
        && Math.Abs(fraction.Fraction - 10080d / 21250d) < 0.0001,
        "fraction progress should parse with thousands separators");
    Assert(
        VisibleProgressTextParser.TryParse("65.5%", out var percent) && percent == new VisibleProgressValue(65.5, 100),
        "percentage progress should parse");
    Assert(!VisibleProgressTextParser.TryParse("120 / 100", out _), "progress above its target should be rejected");

    var tracker = new VisibleProgressTracker();
    tracker.Add(new VisibleProgressObservation("season", "赛季旅程", new VisibleProgressValue(50, 100), 0, 0.9));
    tracker.Add(new VisibleProgressObservation("season", "赛季旅程", new VisibleProgressValue(75, 100), 1, 0.9));
    tracker.Add(new VisibleProgressObservation("season", "赛季旅程", new VisibleProgressValue(10, 100), 2, 0.9));
    tracker.Add(new VisibleProgressObservation("unknown", "未知", new VisibleProgressValue(10, 100), 2, 0.1));

    var report = tracker.BuildReport();
    var progress = report.Progress.Single();
    Assert(Math.Abs(progress.TotalPositiveProgress - 0.25) < 0.0001, "positive progress should accumulate independently of resets");
    Assert(progress.ResetCount == 1, "progress rollover should be explicit");
    Assert(report.RejectedObservationCount == 1, "low-confidence progress should be rejected");
}

static void VisibleBuffUptimeUsesObservedIntervals()
{
    var tracker = new VisibleBuffTracker(maximumSampleGapSeconds: 1);
    tracker.AddFrame(0, [new VisibleBuffObservation("barrier", "屏障", 1, 4, 0.9)]);
    tracker.AddFrame(0.5, [new VisibleBuffObservation("barrier", "屏障", 2, 3.5, 0.9)]);
    tracker.AddFrame(1, []);
    tracker.AddFrame(3, [new VisibleBuffObservation("untrusted", "未知", 1, null, 0.1)]);

    var report = tracker.BuildReport();
    var barrier = report.Buffs.Single();
    Assert(Math.Abs(report.ObservedSeconds - 1) < 0.0001, "large capture gaps should not be counted as observed time");
    Assert(Math.Abs(barrier.ActiveObservedSeconds - 1) < 0.0001 && barrier.UptimeFraction == 1,
        "buff uptime should be based only on contiguous sampled intervals");
    Assert(barrier.MaximumStackCount == 2 && !barrier.IsPresentInLatestFrame, "stack peak and latest presence should remain distinct");
    Assert(report.RejectedObservationCount == 1, "low-confidence buff icons should be rejected");
}

static void VisibleMapMarkersExpireWithoutFabrication()
{
    var tracker = new VisibleMapTracker(markerLifetimeSeconds: 2);
    tracker.AddFrame(0, [new VisibleMapMarkerObservation("event-1", "event", "军团集结", 0.4, 0.3, 0.9)]);
    Assert(tracker.BuildReport().FreshMarkers.Count == 1, "a fresh visible marker should be reported");
    tracker.AddFrame(3,
    [
        new VisibleMapMarkerObservation("event-2", "event", "未知", 0.5, 0.5, 0.1)
    ]);
    var report = tracker.BuildReport();
    Assert(report.FreshMarkers.Count == 0, "markers should expire when they are no longer visibly observed");
    Assert(report.RejectedObservationCount == 1, "low-confidence map markers should be rejected");
}

static void LocalAutomationIsEdgeTriggeredAndLocal()
{
    var engine = new LocalAutomationRuleEngine(
    [
        new LocalAutomationRule(
            "season-80",
            "progress:season",
            AutomationComparison.AtLeast,
            0.8,
            LocalAutomationAction.ShowNotification,
            "赛季进度达到 80%")
    ]);

    Assert(engine.Evaluate(0, new Dictionary<string, double> { ["progress:season"] = 0.7 }).Count == 0,
        "automation should remain idle before the threshold");
    Assert(engine.Evaluate(1, new Dictionary<string, double> { ["progress:season"] = 0.8 }).Count == 1,
        "automation should emit when the threshold is crossed");
    Assert(engine.Evaluate(2, new Dictionary<string, double> { ["progress:season"] = 0.9 }).Count == 0,
        "automation should not repeat while the condition remains true");
    engine.Evaluate(3, new Dictionary<string, double> { ["progress:season"] = 0.6 });
    Assert(engine.Evaluate(4, new Dictionary<string, double> { ["progress:season"] = 0.85 }).Count == 1,
        "automation should re-arm after the condition becomes false");
    AssertThrows<ArgumentException>(
        () => new LocalAutomationRuleEngine(
        [
            new LocalAutomationRule(
                "unsafe",
                "progress:season",
                AutomationComparison.AtLeast,
                1,
                (LocalAutomationAction)999,
                "unsupported")
        ]),
        "unknown actions must not enter the local automation engine");
}

static CombatTextObservation DamageObservation(long damage, double time, double x, double y) =>
    new(damage, time, x, y, 80, 32, $"{damage / 10_000d:F1}万");

static RealtimeVisionReadout DamageReadout(CombatTextObservation observation) => new(
    [observation],
    [],
    [],
    [],
    [],
    observation.Confidence,
    RealtimeVisionQuality.Baseline("Acceptance fixture Windows OCR baseline."));

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

static void StatisticsHudSettingsDefaultAndPersist()
{
    var defaults = new OverlaySettings();
    Assert(defaults.DamageStatisticsHudEnabled, "the damage HUD should be discoverable on a new install");
    Assert(!defaults.StatisticsHudCompact, "the damage HUD should default to the expanded layout");

    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "statistics-hud.json");
    var document = BuildDocument.CreateStarter();
    document.Overlay.DamageStatisticsHudEnabled = false;
    document.Overlay.StatisticsHudCompact = true;
    var store = new JsonStateStore(path);
    store.Save(document);
    var reloaded = store.Load();

    Assert(!reloaded.Overlay.DamageStatisticsHudEnabled && reloaded.Overlay.StatisticsHudCompact,
        "statistics HUD visibility and compact mode should survive restart");
}

static void StatisticsHudPlacementAvoidsMinimap()
{
    var window = new GameClientWindow(IntPtr.Zero, "Diablo IV", 0, 0, 1920, 1080, true, false);
    var expanded = HudViewModel.CalculateStatisticsHudPlacement(window, compact: false);
    var compact = HudViewModel.CalculateStatisticsHudPlacement(window, compact: true);

    Assert(expanded == new StatisticsHudPlacement(1296, 44, 320, 310),
        "the 1080p statistics HUD should sit immediately left of the minimap reserve");
    Assert(compact.Left == expanded.Left && compact.Top == expanded.Top && compact.Height == 64,
        "compact mode should preserve the anchor while reducing only the panel height");
}

static void StatisticsHudOcrExclusionCoversModeTransitions()
{
    var window1080 = new GameClientWindow(IntPtr.Zero, "Diablo IV", 40, 60, 1920, 1080, true, false);
    var exclusion1080 = HudViewModel.CalculateStatisticsHudOcrExclusion(window1080);
    Assert(exclusion1080 == new PixelRect(1296, 44, 320, 310),
        "OCR should always exclude the maximum expanded footprint in client coordinates");

    var window1440 = new GameClientWindow(IntPtr.Zero, "Diablo IV", 0, 0, 2560, 1440, true, false);
    var exclusion1440 = HudViewModel.CalculateStatisticsHudOcrExclusion(window1440);
    var expanded1440 = HudViewModel.CalculateStatisticsHudPlacement(window1440, compact: false);
    Assert(exclusion1440.Width == expanded1440.Width
        && exclusion1440.Height == expanded1440.Height
        && exclusion1440.Height > HudViewModel.CalculateStatisticsHudPlacement(window1440, compact: true).Height,
        "compact-to-expanded and expanded-to-compact transitions must retain the larger exclusion until the next render");
}

static void MapHudSettingsDefaultAndPersist()
{
    var defaults = new OverlaySettings();
    Assert(defaults.MapHud?.Enabled == true, "the map HUD should be discoverable on a new install");
    Assert(defaults.MapHud is not null && Math.Abs(defaults.MapHud.Opacity - 1.0) < 0.001,
        "the map HUD should default to fully opaque");

    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "map-hud.json");
    var document = BuildDocument.CreateStarter();
    document.Overlay.MapHud = new MapHudSettings
    {
        Enabled = false,
        CurrentRegion = "fractured_peaks",
        Opacity = 0.6,
        OverlayScale = 1.3,
        ShowChests = false,
        ScheduleOffsetSeconds = 120
    };
    var store = new JsonStateStore(path);
    store.Save(document);
    var reloaded = store.Load();

    Assert(reloaded.Overlay.MapHud is not null
        && !reloaded.Overlay.MapHud.Enabled
        && reloaded.Overlay.MapHud.CurrentRegion == "fractured_peaks"
        && Math.Abs(reloaded.Overlay.MapHud.Opacity - 0.6) < 0.001
        && Math.Abs(reloaded.Overlay.MapHud.OverlayScale - 1.3) < 0.001
        && !reloaded.Overlay.MapHud.ShowChests
        && Math.Abs(reloaded.Overlay.MapHud.ScheduleOffsetSeconds - 120) < 0.001,
        "map HUD settings should survive restart");
}

static void MapHudPlacementAnchorsToGameWindow()
{
    var settings = new MapHudSettings();
    var window = new GameClientWindow(IntPtr.Zero, "Diablo IV", 40, 60, 1920, 1080, true, false);
    var placement = HudViewModel.CalculateMapHudPlacement(window, settings);

    Assert(placement.Left == window.Left + 12 && placement.Top == window.Top + 56,
        "the 1080p map HUD should anchor inside the game window top-left with margins");
    Assert(placement.Width == 484 && placement.Height == 572,
        "440x520 base size should scale with the 1080p display scale and the overlay scale");

    settings.OverlayScale = 0.5;
    var small = HudViewModel.CalculateMapHudPlacement(window, settings);
    Assert(small.Width == 220 && small.Height == 260,
        "the overlay scale should scale the map HUD size");
}

static void WorldEventClockEvaluatesScheduleAndManualOffset()
{
    var clock = new WorldEventClock(WorldEventSchedule.Defaults);
    var start = DateTimeOffset.UnixEpoch;

    // 军团：30 分钟一轮，进行 10 分钟，相位偏移 5 分钟 → 第 6 分钟处于进行中
    var active = clock.Evaluate(WorldEventKind.Legion, start + TimeSpan.FromMinutes(6));
    Assert(active.Active, "a legion event inside its active window should be marked active");
    Assert(active.Remaining <= TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(1),
        "legion remaining should count down from the active window end");

    // 第 16 分钟：相位 11 分钟，超过进行时长 → 等待下一轮
    var waiting = clock.Evaluate(WorldEventKind.Legion, start + TimeSpan.FromMinutes(16));
    Assert(!waiting.Active, "a legion event outside the active window should be waiting");
    Assert(waiting.Remaining >= TimeSpan.FromMinutes(19) - TimeSpan.FromSeconds(1),
        "legion waiting time should reach the next cycle start");

    // 手动偏移 +5 分钟：原本等待的事件提前进入进行中
    clock.ManualOffsetSeconds = 5 * 60;
    var shifted = clock.Evaluate(WorldEventKind.Legion, start + TimeSpan.FromMinutes(1));
    Assert(shifted.Active, "a positive manual offset should bring the next event earlier");
    clock.ManualOffsetSeconds = 0;
}

static void PoiCatalogRejectsInvalidRecords()
{
    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "poi.json");
    File.WriteAllText(path, """
        {
          "formatVersion": 1,
          "regions": [
            {
              "key": "dry_steppes",
              "name": "干旱草原",
              "markers": [
                { "category": "chest", "x": 0.5, "y": 0.5 },
                { "category": "chest", "x": 1.5, "y": 0.5 },
                { "category": "unknown_category", "x": 0.2, "y": 0.2 },
                { "category": "chest" }
              ]
            }
          ]
        }
        """);
    var catalog = PoiCatalogStore.Load(path);
    Assert(catalog is not null, "a catalog with some invalid records should still load");
    var markers = PoiCatalogStore.GetMarkers(catalog, "dry_steppes");
    Assert(markers.Count == 1, "invalid POI records should be rejected while valid records survive");
    Assert(markers[0].Category == PoiMarkerCategory.Chest
        && markers[0].X == 0.5
        && markers[0].Y == 0.5,
        "the surviving marker should keep its category and coordinates");

    File.WriteAllText(path, """{ "formatVersion": 2, "regions": [] }""");
    Assert(PoiCatalogStore.Load(path) is null, "an unsupported format version should fail closed");

    Assert(PoiCatalogStore.Load(Path.Combine(workspace.Path, "missing.json")) is null,
        "a missing POI file should fail closed");
}

static void WorldEventEdgeTrackerReportsRisingEventsOnce()
{
    var clock = new WorldEventClock(WorldEventSchedule.Defaults);
    var start = DateTimeOffset.UnixEpoch;

    // 基线 t=0：军团相位 25 分钟 → 等待中；地狱狂潮/世界Boss 进行中（不影响军团断言）
    var tracker = new WorldEventEdgeTracker(clock, start);

    Assert(tracker.Rising(clock, start + TimeSpan.FromMinutes(4)).Count == 0,
        "no rising edge should be reported before the active window");
    var risen = tracker.Rising(clock, start + TimeSpan.FromMinutes(6));
    Assert(risen.Count == 1 && risen[0] == WorldEventKind.Legion,
        "legion entering its active window should rise exactly once");
    Assert(tracker.Rising(clock, start + TimeSpan.FromMinutes(6)).Count == 0,
        "the same rising edge must not be reported twice");
    Assert(tracker.Rising(clock, start + TimeSpan.FromMinutes(16)).Count == 0,
        "leaving the active window is a falling edge, not a rising one");
    var reentered = tracker.Rising(clock, start + TimeSpan.FromMinutes(36));
    Assert(reentered.Count == 1 && reentered[0] == WorldEventKind.Legion,
        "legion re-entering after a full cycle should rise exactly once");
    Assert(tracker.Rising(clock, start + TimeSpan.FromMinutes(36)).Count == 0,
        "the re-entering edge must not be reported twice");
}

static void MapHudHotkeyAndAudioSettingsPersist()
{
    var defaults = new MapHudSettings();
    Assert(!defaults.AudioEnabled, "audio reminders should default to off");
    Assert(Math.Abs(defaults.AudioVolume - 0.70) < 0.001, "audio volume should default to 0.70");
    Assert(defaults.HotkeyToggle == "Oem3"
        && defaults.HotkeyRedraw == "F5"
        && defaults.HotkeyResetPlacement == "F6",
        "map hotkeys should have safe non-game-defaults");

    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "map-hud-p2.json");
    var document = BuildDocument.CreateStarter();
    document.Overlay.MapHud = new MapHudSettings
    {
        AudioEnabled = true,
        AudioVolume = 0.35,
        AudioBossPath = "C:\\sounds\\boss.wav",
        HotkeyToggle = "F7",
        HotkeyRedraw = "None",
        HotkeyResetPlacement = "F8"
    };
    var store = new JsonStateStore(path);
    store.Save(document);
    var reloaded = store.Load();

    Assert(reloaded.Overlay.MapHud is not null
        && reloaded.Overlay.MapHud.AudioEnabled
        && Math.Abs(reloaded.Overlay.MapHud.AudioVolume - 0.35) < 0.001
        && reloaded.Overlay.MapHud.AudioBossPath == "C:\\sounds\\boss.wav"
        && reloaded.Overlay.MapHud.HotkeyToggle == "F7"
        && reloaded.Overlay.MapHud.HotkeyRedraw == "None"
        && reloaded.Overlay.MapHud.HotkeyResetPlacement == "F8",
        "map audio and hotkey settings should survive restart");
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

static void TransmutationDetectorRejectsCombatLikeCrimsonNoise()
{
    var detector = new TransmutationSceneDetector();
    var detection = detector.Detect(CreateSyntheticCombatFlameFrame());

    Assert(!detection.IsTransmutationVisible,
        $"combat-like warm effects outside a recipe list must be rejected, actual confidence {detection.ContextConfidence:F3}");
}

static void TransmutationDetectorIsResolutionIndependent()
{
    var detector = new TransmutationSceneDetector();
    foreach (var (width, height) in new[] { (1280, 720), (1920, 1080), (2560, 1440) })
    {
        var detection = detector.Detect(CreateSyntheticTransmutationFrame(width, height));
        Assert(detection.IsTransmutationVisible,
            $"normalized recipe evidence should survive {width}x{height}, actual confidence {detection.ContextConfidence:F3}");
    }
}

static void TransmutationReminderUsesEnterAndExitHysteresis()
{
    var detector = new TransmutationSceneDetector();
    var positive = detector.Detect(CreateSyntheticTransmutationFrame());
    var missing = new TransmutationSceneDetection(false, 0, default);
    var state = new TransmutationReminderStateMachine();

    Assert(!state.Advance(positive).IsVisible, "one positive frame must not show the reminder");
    Assert(!state.Advance(positive).IsVisible, "two positive frames must not show the reminder");
    var entered = state.Advance(positive);
    Assert(entered.IsVisible && entered.VisibilityChanged, "three consistent positive frames should show once");

    var firstMiss = state.Advance(missing);
    Assert(firstMiss.IsVisible && !firstMiss.VisibilityChanged, "one noisy miss must not flicker the reminder");
    var recovered = state.Advance(positive);
    Assert(recovered.IsVisible && !recovered.VisibilityChanged, "a recovered observation should clear the pending exit");
    state.Advance(missing);
    var exited = state.Advance(missing);
    Assert(!exited.IsVisible && exited.VisibilityChanged, "two consecutive misses should hide the reminder");
}

static void TransmutationReminderResetHidesImmediately()
{
    var detector = new TransmutationSceneDetector();
    var positive = detector.Detect(CreateSyntheticTransmutationFrame());
    var state = new TransmutationReminderStateMachine();
    state.Advance(positive);
    state.Advance(positive);
    Assert(state.Advance(positive).IsVisible, "precondition should enter the visible state");

    var reset = state.Reset();
    Assert(!reset.IsVisible && reset.VisibilityChanged,
        "focus loss or tracking pause should bypass exit hysteresis and hide immediately");
    Assert(!state.Advance(positive).IsVisible, "reset should require a fresh enter sequence");
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

static void CharacterPanelDetects1080pTitlePlacement()
{
    var frame = CreateSyntheticGameFrame(
        width: 1920,
        height: 1080,
        panelLeft: 1176,
        characterTitleOffsetX: 0.060,
        characterTitleOffsetY: 0.013);
    var detection = new CharacterPanelDetector().Detect(frame);

    Assert(Math.Abs(detection.Bounds.X - 0.6125) < 0.025, $"1080p panel boundary should be near 0.613, actual {detection.Bounds.X:F3}");
    Assert(detection.Confidence >= 0.55, $"1080p title placement should be actionable, actual {detection.Confidence:F3}");
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

static void BundledExternalResourceCatalogIsStrictAndUsable()
{
    var catalog = ExternalResourceCatalog.LoadStrict(
        Path.Combine(Environment.CurrentDirectory, "library", "external-resources.json"));

    Assert(catalog.SchemaVersion == 1, "external resource catalog should use schema version 1");
    Assert(catalog.Entries.Count == 1, "the initial catalog should contain one Owner-requested resource");
    var helltides = catalog.Entries.Single();
    var uri = helltides.GetLaunchUri();
    Assert(helltides.ResourceId == "diablo-iv.helltides-map", "Helltides should have a stable resource id");
    Assert(uri.AbsoluteUri == "https://helltides.com/", "Helltides should use the reviewed canonical URL");
    Assert(uri.IdnHost == "helltides.com", "Helltides should resolve to the exact reviewed host");
    Assert(helltides.AllowedHosts.SequenceEqual(new[] { "helltides.com" }),
        "Helltides should not allow unrelated or wildcard hosts");
}

static void ExternalResourceCatalogRejectsUnsafeLinks()
{
    using var workspace = new TemporaryDirectory();
    var unsafeSchemePath = Path.Combine(workspace.Path, "unsafe-scheme.json");
    var deceptiveHostPath = Path.Combine(workspace.Path, "deceptive-host.json");
    var unknownFieldPath = Path.Combine(workspace.Path, "unknown-field.json");
    const string validEntry = """
        {
          "resourceId": "diablo-iv.helltides-map",
          "gameId": "diablo-iv",
          "providerId": "helltides",
          "category": "map-tools",
          "displayName": "Helltides map",
          "description": "Reviewed map entry.",
          "canonicalUrl": "https://helltides.com/",
          "allowedHosts": ["helltides.com"],
          "locales": ["en"],
          "regions": ["global"],
          "officialStatus": "community",
          "reviewedAt": "2026-07-26T00:00:00+08:00",
          "reviewedBy": "owner",
          "reviewMethod": "browser-review",
          "status": "active",
          "riskNotes": "Third-party content and cookies.",
          "disclaimerKey": "third-party-browser",
          "attribution": "Helltides.com"
        }
        """;

    static string CatalogWith(string entry) => $$"""
        {
          "schemaVersion": 1,
          "catalogVersion": "test.1",
          "generatedAt": "2026-07-26T00:00:00+08:00",
          "entries": [{{entry}}]
        }
        """;

    File.WriteAllText(unsafeSchemePath, CatalogWith(validEntry.Replace(
        "https://helltides.com/",
        "http://helltides.com/",
        StringComparison.Ordinal)));
    File.WriteAllText(deceptiveHostPath, CatalogWith(validEntry.Replace(
        "https://helltides.com/",
        "https://helltides.com.evil.example/",
        StringComparison.Ordinal)));
    File.WriteAllText(unknownFieldPath, CatalogWith(validEntry.Replace(
        "\"attribution\": \"Helltides.com\"",
        "\"attribution\": \"Helltides.com\", \"trackingUrl\": \"https://example.com\"",
        StringComparison.Ordinal)));

    AssertThrows<InvalidDataException>(
        () => ExternalResourceCatalog.LoadStrict(unsafeSchemePath),
        "external resource catalogs should reject plaintext URLs");
    AssertThrows<InvalidDataException>(
        () => ExternalResourceCatalog.LoadStrict(deceptiveHostPath),
        "external resource catalogs should reject deceptive subdomains");
    AssertThrows<System.Text.Json.JsonException>(
        () => ExternalResourceCatalog.LoadStrict(unknownFieldPath),
        "external resource catalogs should reject unknown fields");
}

static void HelltidesPrivacyRequestPolicyIsNarrow()
{
    Assert(HelltidesPrivacyPolicy.ShouldBlockRequest("https://cmp.inmobi.com/tcfv2/cmp2.js"),
        "the Helltides CMP host should be blocked");
    Assert(HelltidesPrivacyPolicy.ShouldBlockRequest("https://www.googletagmanager.com/gtag/js?id=test"),
        "Google Tag Manager should be blocked");
    Assert(HelltidesPrivacyPolicy.ShouldBlockRequest("https://unpkg.com/feedbackfin@1.2.3/dist/index.js"),
        "the reviewed feedback widget path should be blocked without blocking all of unpkg");
    Assert(HelltidesPrivacyPolicy.ShouldBlockRequest("https://example.cloudfront.net/assets/prebid-load.js"),
        "the reviewed prebid loader path should be blocked without blocking all of CloudFront");
    Assert(!HelltidesPrivacyPolicy.ShouldBlockRequest("https://helltides.com/_nuxt/app.js"),
        "first-party Helltides assets must remain available");
    Assert(!HelltidesPrivacyPolicy.ShouldBlockRequest("https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"),
        "unrelated CDN assets must remain available");
    Assert(!HelltidesPrivacyPolicy.ShouldBlockRequest("https://notdoubleclick.net/map.js"),
        "lookalike hosts must not be blocked by substring matching");
}

static void HelltidesPrivacyDomSanitizerDoesNotFakeConsent()
{
    var script = HelltidesPrivacyPolicy.DomSanitizerScript;
    Assert(script.Contains("#qc-cmp2-ui", StringComparison.Ordinal),
        "the known Quantcast consent UI should be removed");
    Assert(script.Contains("#qc-cmp2-persistent-link", StringComparison.Ordinal),
        "the known Quantcast persistent privacy link should be removed");
    Assert(!script.Contains("document.cookie", StringComparison.OrdinalIgnoreCase),
        "privacy cleanup must not write a consent cookie");
    Assert(!script.Contains(".click(", StringComparison.OrdinalIgnoreCase),
        "privacy cleanup must not simulate consent clicks");
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

static void D2CoreMetadataClassifiesModesAndPurposes()
{
    var catalog = D2CoreAffixCatalog.FromJson("""
        {
          "affix": []
        }
        """);
    var record = D2CoreBuildParser.Parse(
        CreateD2CoreFixtureResponse(),
        new D2CoreBuildReference("1Zep", 5),
        catalog,
        DateTimeOffset.Parse("2026-07-26T00:00:00Z"));

    Assert(record.SeasonMode == BuildSeasonMode.Seasonal, "positive source season should classify as seasonal mode");
    Assert(record.DifficultyMode == BuildDifficultyMode.Hardcore, "explicit source hardcore flag should classify as hardcore");
    Assert(record.Variants[6].Purposes.SequenceEqual(new[] { BuildPurpose.Bossing }), "single-target boss variant should classify as bossing");

    var mixed = BuildMetadata.ClassifyPurposes("开荒到速刷，随后冲层");
    Assert(mixed.SequenceEqual(new[] { BuildPurpose.Leveling, BuildPurpose.PitPush, BuildPurpose.SpeedFarm }),
        "variant names should retain every matching purpose in stable order");
}

static void BundledLibrarySeedsClassifiedVariants()
{
    var library = new FileBuildLibraryStore(Path.Combine(Environment.CurrentDirectory, "library"), isReadOnly: true);
    var defaults = library.LoadDefaults();
    var defaultIds = defaults.Select(entry => entry.BuildId).ToHashSet(StringComparer.Ordinal);
    var records = library.LoadAll().Where(record => defaultIds.Contains(record.BuildId)).ToList();
    var profiles = BuildLibrarySeeder.CreateProfiles(library);
    var expectedProfileCount = records.Sum(record => record.Variants.Count(variant => variant.Equipment.Count > 0));
    Assert(defaults.Count == 6, "default catalog should contain six currently verified class representatives");
    Assert(profiles.Count == expectedProfileCount, "first-run seed should create one profile for every usable default variant");
    Assert(profiles.All(profile => profile.Season == 14 && profile.SeasonMode == BuildSeasonMode.Seasonal),
        "seeded profiles should preserve the current season mode");
    Assert(profiles.Select(profile => profile.ClassName).Distinct().OrderBy(value => value).SequenceEqual(
            new[] { "巫师", "德鲁伊", "死灵法师", "游侠", "灵巫", "野蛮人" }.OrderBy(value => value)),
        "seeded profiles should cover every class verified from the current recommendation page");
    Assert(profiles.Any(profile => profile.Purposes.Contains(BuildPurpose.Leveling)
        && profile.Purposes.Contains(BuildPurpose.SpeedFarm)),
        "catalog scene tags should retain combined leveling and speed-farm use");
    Assert(profiles.Any(profile => profile.Purposes.Contains(BuildPurpose.PitPush)), "seed should include a pit-push profile");
    Assert(profiles.Any(profile => profile.Purposes.Contains(BuildPurpose.Bossing)), "seed should include a boss profile");
    Assert(profiles.All(profile => profile.DifficultyMode == BuildDifficultyMode.Unknown),
        "missing provider difficulty metadata must remain unknown instead of being guessed");
}

static void BundledDefaultsMergeIdempotently()
{
    var document = BuildDocument.CreateStarter();
    var selectedProfileId = document.SelectedProfileId;
    var defaults = BuildLibrarySeeder.CreateProfiles(
        new FileBuildLibraryStore(Path.Combine(Environment.CurrentDirectory, "library"), isReadOnly: true));

    var firstAdded = BuildLibrarySeeder.MergeMissingProfiles(document, defaults);
    var secondAdded = BuildLibrarySeeder.MergeMissingProfiles(document, defaults);

    Assert(firstAdded == defaults.Count, "existing state should receive every missing bundled default");
    Assert(secondAdded == 0, "repeated startup should not duplicate bundled defaults");
    Assert(document.SelectedProfileId == selectedProfileId, "merging defaults must preserve the user's current selection");
    Assert(document.Profiles.Count == defaults.Count + 1, "merging defaults must preserve the user's existing profiles");
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

static void LootFilterCodeNormalizesAndClassifiesStages()
{
    Assert(LootFilterMetadata.NormalizeCode(" ABCDefgh12345678\n") == "ABCDefgh12345678", "filter codes should drop copied whitespace");
    Assert(LootFilterMetadata.InferStage("风暴狼德前期过滤器") == LootFilterStage.Early, "前期 should map to the early stage");
    Assert(LootFilterMetadata.InferStage("狼德 - 1-70级") == LootFilterStage.Leveling, "1-70 should map to leveling");
    Assert(LootFilterMetadata.InferStage("冲层版本") == LootFilterStage.Push, "冲层 should map to push");
    AssertThrows<InvalidDataException>(
        () => LootFilterMetadata.NormalizeCode("not valid!"),
        "non-filter text should be rejected");
}

static void LootFilterLibraryRoundTripsLocally()
{
    using var workspace = new TemporaryDirectory();
    var path = Path.Combine(workspace.Path, "loot-filters.json");
    var filter = new LootFilterPreset
    {
        Id = "local-1",
        Source = "manual",
        Name = "本地前期过滤器",
        ClassName = "Druid",
        Stage = LootFilterStage.Early,
        CopyCode = "ABCDEFGHIJKLMNOP"
    };

    var store = new FileLootFilterStore(path);
    store.Save([filter]);
    var loaded = store.LoadAll();

    Assert(loaded.Count == 1, "one local filter should round trip");
    Assert(loaded[0].CopyCode == filter.CopyCode, "copy code should survive the local store");
    Assert(loaded[0].Stage == LootFilterStage.Early, "stage should survive the local store");
}

static void BundledLootFilterSeedIsUsable()
{
    var path = Path.Combine(Environment.CurrentDirectory, "library", "d2core-filters.json");
    var filters = new FileLootFilterStore(path, isReadOnly: true).LoadAll();

    Assert(filters.Count >= 18, "bundled filter catalog should contain the verified page seeds and curated additions");
    Assert(filters.Any(filter => filter.BuildId == "1ZsP"
        && filter.ClassName == "Druid"
        && filter.Stage == LootFilterStage.Early),
        "bundled catalog should expose the verified Druid early filter");
    Assert(filters.Count(filter => filter.ClassName == "Barbarian") >= 3
        && filters.Any(filter => filter.BuildId == "1ZCi" && filter.Stage == LootFilterStage.Mid)
        && filters.Any(filter => filter.BuildId == "1ZCi" && filter.Stage == LootFilterStage.Late),
        "bundled catalog should preserve the verified Barbarian stages");
    Assert(filters.Any(filter => filter.ClassName == "Spiritborn"),
        "bundled catalog should expose the verified Spiritborn filter");
    Assert(filters.Any(filter => filter.ClassName == "Rogue" && filter.UseCases.Contains("冲层")),
        "bundled catalog should expose a Rogue push filter with use-case metadata");
    Assert(filters.Any(filter => filter.ClassName == "Sorcerer" && filter.BuildName == "暴风雪 / 火墙"),
        "bundled catalog should expose the verified Sorcerer firewall filter");
    Assert(filters.Any(filter => filter.BuildId == "20gB"
        && filter.ClassName == "Necromancer"
        && filter.CopyCode.Length == 2712),
        "bundled catalog should expose the verified Necromancer summon filter");
    Assert(filters.Any(filter => filter.BuildId == "219Z" && filter.ClassName == "Paladin")
        && filters.Any(filter => filter.BuildId == "1ZMU" && filter.ClassName == "Paladin"),
        "bundled catalog should expose both verified Paladin filters");
    Assert(filters.Count(filter => filter.ClassName == "Warlock") >= 2
        && filters.Any(filter => filter.BuildId == "1ZLg" && filter.Stage == LootFilterStage.Leveling)
        && filters.Any(filter => filter.BuildId == "20EK" && filter.UseCases.Contains("速刷")),
        "bundled catalog should expose the verified Warlock leveling and speed-farming filters");
    Assert(filters.Select(filter => filter.ClassName).Distinct(StringComparer.Ordinal).Count() >= 8,
        "bundled catalog should cover all eight supported Diablo 4 classes");
}

static void LootFilterCollectionImportsAndFilters()
{
    using var workspace = new TemporaryDirectory();
    var localPath = Path.Combine(workspace.Path, "loot-filters.json");
    var bundled = new LootFilterPreset
    {
        Id = "bundled-1",
        Source = "d2core",
        BuildId = "1ZsP",
        Name = "德鲁伊前期",
        ClassName = "Druid",
        Stage = LootFilterStage.Early,
        CopyCode = "ABCDEFGHIJKLMNOP"
    };
    var collection = new LootFilterCollectionViewModel(
        [bundled],
        new FileLootFilterStore(localPath));

    collection.SelectedClass = collection.ClassFilters.Single(option => option.ClassName == "Druid");
    collection.SelectedStage = collection.StageFilters.Single(option => option.Stage == LootFilterStage.Early);
    Assert(collection.FilteredFilters.Count() == 1, "class and stage filters should narrow the collection");

    collection.ClearImportCommand.Execute(null);
    collection.ImportName = "法师终局";
    collection.ImportClassName = "Sorcerer";
    collection.ImportStage = collection.ImportStages.Single(option => option.Stage == LootFilterStage.Late);
    collection.ImportCode = "QRSTUVWXYZabcdef";
    collection.ImportCommand.Execute(null);

    Assert(collection.Filters.Count == 2, "a pasted code should create a local collection entry");
    Assert(new FileLootFilterStore(localPath).LoadAll().Count == 1, "only imported filters should be written to the local store");
    Assert(collection.Filters.Any(filter => filter.ClassName == "Sorcerer" && filter.Stage == LootFilterStage.Late),
        "imported class and stage metadata should be retained");
}

static void LootFilterCollectionSupportsDecisionFilters()
{
    using var workspace = new TemporaryDirectory();
    var filters = new[]
    {
        new LootFilterPreset
        {
            Id = "recommended-leveling",
            Source = "d2core",
            BuildId = "1ZsP",
            Name = "风暴狼德前期",
            BuildName = "风暴狼德",
            ClassName = "Druid",
            Stage = LootFilterStage.Leveling,
            LevelRange = "1-70",
            UseCases = ["开荒", "刷装"],
            IsRecommended = true,
            CopyCode = "ABCDEFGHIJKLMNOP"
        },
        new LootFilterPreset
        {
            Id = "push-rogue",
            Source = "d2core",
            BuildId = "20kq",
            Name = "毒灌箭雨严格",
            BuildName = "毒灌箭雨",
            ClassName = "Rogue",
            Stage = LootFilterStage.Late,
            LevelRange = "巅峰 100+",
            UseCases = ["冲层"],
            SourceUpdatedAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            CopyCode = "QRSTUVWXYZabcdef"
        }
    };
    var collection = new LootFilterCollectionViewModel(
        filters,
        new FileLootFilterStore(Path.Combine(workspace.Path, "loot-filters.json")));

    Assert(collection.UseCaseFilters.Any(option => option.UseCase == "开荒"),
        "use-case options should be derived from catalog metadata");
    collection.SearchText = "毒灌";
    Assert(collection.FilteredFilters.Single().BuildId == "20kq",
        "search should match the BD name and narrow the list");
    collection.SearchText = string.Empty;
    collection.SelectedUseCase = collection.UseCaseFilters.Single(option => option.UseCase == "冲层");
    Assert(collection.FilteredFilters.Single().ClassName == "Rogue",
        "use-case selection should narrow the list");
    collection.SelectedUseCase = collection.UseCaseFilters[0];
    collection.SelectedSort = collection.SortFilters.Single(option => option.Value == "name");
    Assert(collection.FilteredFilters.First().Name == "毒灌箭雨严格",
        "name sort should be deterministic");
    Assert(filters[0].ClassLabel == "德鲁伊" && filters[0].StageUseCaseLabel.Contains("开荒"),
        "catalog display labels should expose localized class and use-case metadata");
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
            hardcore = true,
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
    bool includeCharacterTitle = true,
    double characterTitleOffsetX = 0.031,
    double characterTitleOffsetY = 0.057)
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
        DrawSyntheticCharacterTitle(
            pixels,
            width,
            panelLeft,
            activeTop,
            activeHeight,
            characterTitleOffsetX,
            characterTitleOffsetY);
    }

    return new PixelFrame(width, height, pixels);
}

static void DrawSyntheticCharacterTitle(
    byte[] pixels,
    int frameWidth,
    int panelLeft,
    int activeTop,
    int activeHeight,
    double titleOffsetX,
    double titleOffsetY)
{
    const ulong characterTitleHash = 0x00140C1C1E1E0200UL;
    var left = (int)Math.Round(panelLeft + activeHeight * titleOffsetX);
    var top = (int)Math.Round(activeTop + activeHeight * titleOffsetY);
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

static PixelFrame CreateSyntheticTransmutationFrame(int width = 1000, int height = 700)
{
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

    var selectedLeft = (int)Math.Round(width * 0.66);
    var selectedRight = (int)Math.Round(width * 0.90);
    var selectedTop = (int)Math.Round(height * (250d / 700));
    var selectedBottom = (int)Math.Round(height * (310d / 700));
    for (var y = selectedTop; y < selectedBottom; y++)
    {
        for (var x = selectedLeft; x < selectedRight; x++)
        {
            var offset = ((y * width) + x) * 4;
            pixels[offset] = 18;
            pixels[offset + 1] = 36;
            pixels[offset + 2] = 182;
        }
    }

    return new PixelFrame(width, height, pixels);
}

static PixelFrame CreateSyntheticCombatFlameFrame()
{
    const int width = 1920;
    const int height = 1080;
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            var offset = ((y * width) + x) * 4;
            pixels[offset] = 46;
            pixels[offset + 1] = 58;
            pixels[offset + 2] = 62;
            pixels[offset + 3] = 255;
        }
    }

    for (var y = 770; y < 850; y++)
    {
        var wave = (int)Math.Round(Math.Sin(y * 0.19) * 42);
        var left = 1320 + wave;
        var right = 1780 - wave / 2;
        for (var x = left; x < right; x++)
        {
            var offset = ((y * width) + x) * 4;
            pixels[offset] = (byte)(20 + (x + y) % 35);
            pixels[offset + 1] = (byte)(65 + (x * 3 + y) % 85);
            pixels[offset + 2] = (byte)(175 + (x + y * 2) % 80);
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

file sealed class ControlledRealtimeVisionAdapter : IRealtimeVisionAdapter
{
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<RealtimeVisionReadout> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _callCount;
    private int _concurrentCalls;
    private int _maximumConcurrentCalls;

    public int CallCount => Volatile.Read(ref _callCount);
    public int MaximumConcurrentCalls => Volatile.Read(ref _maximumConcurrentCalls);

    public async Task<RealtimeVisionReadout> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        var concurrent = Interlocked.Increment(ref _concurrentCalls);
        UpdateMaximum(concurrent);
        _entered.TrySetResult(true);
        try
        {
            return await _completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentCalls);
        }
    }

    public bool WaitUntilEntered() => _entered.Task.Wait(TimeSpan.FromSeconds(3));

    public void Complete(RealtimeVisionReadout readout) => _completion.TrySetResult(readout);

    private void UpdateMaximum(int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref _maximumConcurrentCalls);
            if (value <= current
                || Interlocked.CompareExchange(ref _maximumConcurrentCalls, value, current) == current)
            {
                return;
            }
        }
    }
}

file sealed class ImmediateRealtimeVisionAdapter : IRealtimeVisionAdapter
{
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<RealtimeVisionReadout> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(RealtimeVisionReadout.Empty);
    }
}

file sealed class TimedDamageRealtimeVisionAdapter : IRealtimeVisionAdapter
{
    public RealtimeVisionCapabilities Capabilities => RealtimeVisionCapabilities.DamageOnly;

    public Task<RealtimeVisionReadout> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default)
    {
        var observation = new CombatTextObservation(
            35_800_000,
            timeSeconds,
            500 + (timeSeconds * 10),
            310 - (timeSeconds * 30),
            80,
            32,
            "3.58亿",
            0.9);
        return Task.FromResult(new RealtimeVisionReadout(
            [observation],
            [],
            [],
            [],
            [],
            observation.Confidence,
            RealtimeVisionQuality.Baseline("Timed acceptance fixture.")));
    }
}

file sealed class InspectingRealtimeVisionAdapter : IRealtimeVisionAdapter
{
    private readonly ManualResetEventSlim _read = new();

    public PixelFrame? Frame { get; private set; }

    public Task<RealtimeVisionReadout> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default)
    {
        Frame = frame;
        _read.Set();
        return Task.FromResult(RealtimeVisionReadout.Empty);
    }

    public bool WaitUntilRead() => _read.Wait(TimeSpan.FromSeconds(3));
}

file sealed class FixtureBaselineVisionAdapter : IRealtimeVisionAdapter
{
    public int ReadCount { get; private set; }

    public Task<RealtimeVisionReadout> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default)
    {
        ReadCount++;
        return Task.FromResult(RealtimeVisionReadout.Empty with
        {
            Quality = RealtimeVisionQuality.Baseline("Fixture Windows OCR baseline.")
        });
    }
}

file sealed class FixtureCombatTextSpottingEngine : ICombatTextSpottingEngine
{
    private readonly bool _throwOnRead;

    public FixtureCombatTextSpottingEngine(bool throwOnRead)
    {
        _throwOnRead = throwOnRead;
    }

    public int ReadCount { get; private set; }

    public CombatTextModelAvailability Availability { get; } = new(
        true,
        "runtime-loaded-unvalidated",
        "Fixture runtime only; not calibrated.",
        "fixture",
        null,
        new Dictionary<string, string>());

    public Task<CombatTextSpottingResult> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default)
    {
        ReadCount++;
        if (_throwOnRead)
        {
            throw new InvalidOperationException("fixture inference failed");
        }

        return Task.FromResult(new CombatTextSpottingResult(
            [new CombatTextObservation(
                35_800_000,
                timeSeconds,
                500,
                300,
                80,
                32,
                "3.58亿",
                0.9,
                null,
                0.9)],
            1,
            0,
            new Dictionary<string, int>(),
            TimeSpan.FromMilliseconds(1)));
    }

    public void Dispose()
    {
    }
}

file sealed class ThrowingRealtimeVisionAdapter : IRealtimeVisionAdapter
{
    public Task<RealtimeVisionReadout> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default) =>
        Task.FromException<RealtimeVisionReadout>(new InvalidOperationException("test OCR unavailable"));
}
