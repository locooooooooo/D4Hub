using D4Hub.Core;

var tests = new (string Name, Action Run)[]
{
    ("mixed grouped decimal fails closed", MixedGroupedDecimalFailsClosed),
    ("long decimal mantissa fails closed", LongDecimalMantissaFailsClosed),
    ("valid grouped huge hit has no absolute cap", ValidGroupedHugeHitHasNoAbsoluteCap),
    ("stable grouped huge hit confirms", StableGroupedHugeHitConfirms),
    ("track magnitude conflict fails closed", TrackMagnitudeConflictFailsClosed),
    ("stable moving track confirms", StableMovingTrackConfirms),
    ("session clock excludes paused and focus-lost time", SessionClockRunningPauseFocusLostReplay),
    ("session clock reset clears time and receipts", SessionClockResetClearsEffectiveTime),
    ("session clock live ticks advance time", SessionClockTickAdvancesLiveTime),
    ("session aggregator maximum hit and idempotency", SessionDamageAggregatorMaximumHitAndIdempotency),
    ("session DPS uses the clock denominator", SessionDamageRateUsesClockDenominator),
    ("town tracker requires hysteresis and keeps unknown", TownStateTrackerRequiresHysteresisAndKeepsUnknown),
    ("town tracker rejects weak candidates", TownStateTrackerRejectsWeakCandidates),
    ("combat activity tracker exit hysteresis", CombatActivityTrackerEnterExitHysteresis),
    ("realtime session exposes p1 fields", RealtimeStatisticsSessionExposesP1Fields)
};

var failures = new List<string>();
foreach (var (name, run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {name}: {exception.Message}");
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

Console.WriteLine($"PASS all {tests.Length} combat algorithm checks");
return 0;

static void MixedGroupedDecimalFailsClosed()
{
    var assessment = Assess("7,334.80亿");
    Assert(assessment.RejectionReason == "mixed-grouped-decimal-risk",
        "a grouped decimal combat label is a likely overlap merge");
}

static void LongDecimalMantissaFailsClosed()
{
    var assessment = Assess("12345.6万");
    Assert(assessment.RejectionReason == "implausible-mantissa-shape",
        "an overlong compact mantissa should not enter event fusion");
}

static void ValidGroupedHugeHitHasNoAbsoluteCap()
{
    var match = CombatDamageTextParser.ParseMatches("1,234亿").Single();
    var assessment = CombatOcrObservationMapper.AssessDamageCandidate(match, 1, 1);
    Assert(match.Damage == 123_400_000_000 && assessment.RejectionReason is null,
        "a syntactically valid huge hit should not be rejected by an absolute damage ceiling");
}

static void StableGroupedHugeHitConfirms()
{
    var first = Map("1,234亿", 0, 500, 300);
    var second = Map("1,234亿", 0.1, 505, 285);
    var tracker = new CombatDamageTracker(minimumObservationConfidence: 0.8);
    tracker.AddFrame(0, [first]);
    tracker.AddFrame(0.1, [second]);
    var report = tracker.BuildReport();
    Assert(report.UniqueEventCount == 1 && report.TotalDamage == 123_400_000_000,
        "stable multi-frame evidence should confirm a valid grouped huge hit");
}

static void TrackMagnitudeConflictFailsClosed()
{
    var tracker = new CombatDamageTracker();
    tracker.AddFrame(0, [Observation(1_000_000, 0, 500, 300, "100.0万")]);
    tracker.AddFrame(0.1, [Observation(100_000_000, 0.1, 502, 290, "1.00亿")]);
    tracker.AddFrame(0.2, [Observation(1_000_000, 0.2, 504, 280, "100.0万")]);
    var report = tracker.BuildReport();
    Assert(report.UniqueEventCount == 0 && report.TotalDamage == 0,
        "a hundred-fold candidate jump on one unconfirmed trajectory should fail closed");
    Assert(report.RejectionReasons["candidate-magnitude-conflict"] == 3,
        "every observation consumed by the unstable track should have a rejection receipt");
}

static void StableMovingTrackConfirms()
{
    var tracker = new CombatDamageTracker();
    tracker.AddFrame(0, [Observation(35_800_000, 0, 500, 300, "3.58亿")]);
    tracker.AddFrame(0.1, [Observation(35_800_000, 0.1, 504, 287, "3.58亿")]);
    var report = tracker.BuildReport();
    Assert(report.UniqueEventCount == 1 && report.TotalDamage == 35_800_000,
        "the anomaly gate must preserve a stable moving candidate");
}

static DamageCandidateAssessment Assess(string text)
{
    var match = CombatDamageTextParser.ParseMatches(text).Single();
    return CombatOcrObservationMapper.AssessDamageCandidate(match, 1, 1);
}

static CombatTextObservation Map(string text, double time, double x, double y)
{
    var match = CombatDamageTextParser.ParseMatches(text).Single();
    var assessment = CombatOcrObservationMapper.AssessDamageCandidate(match, 1, 1);
    return new CombatTextObservation(
        match.Damage,
        time,
        x,
        y,
        100,
        32,
        text,
        assessment.EvidenceScore,
        assessment.RejectionReason);
}

static CombatTextObservation Observation(
    long damage,
    double time,
    double x,
    double y,
    string text) => new(
        damage,
        time,
        x,
        y,
        100,
        32,
        text,
        0.9);

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void SessionClockRunningPauseFocusLostReplay()
{
    var clock = new SessionClock();
    clock.Start(0);
    clock.Pause(5);
    clock.MarkFocusLost(6);
    clock.Start(8);
    clock.Pause(10);
    var snapshot = clock.Snapshot;
    Assert(snapshot.State == SessionClockState.Paused, "the clock settles in the expected state");
    Assert(Math.Abs(snapshot.EffectiveSeconds - 7) < 0.001,
        "effective time excludes paused and focus-lost intervals");
    Assert(clock.Transitions.Count == 5, "every transition has a replayable receipt");
}

static void SessionClockResetClearsEffectiveTime()
{
    var clock = new SessionClock();
    clock.Start(0);
    clock.Pause(10);
    clock.Reset(12);
    Assert(Math.Abs(clock.Snapshot.EffectiveSeconds) < 0.001, "reset clears effective time");
    Assert(clock.Snapshot.State == SessionClockState.Initial, "reset returns to the initial state");
    Assert(clock.Transitions.Count == 0, "reset clears transition receipts");
}

static void SessionClockTickAdvancesLiveTime()
{
    var clock = new SessionClock();
    clock.Start(0);
    clock.Tick(1);
    clock.Tick(2);
    clock.Tick(2);
    Assert(Math.Abs(clock.Snapshot.EffectiveSeconds - 2) < 0.001,
        "live ticks advance effective time without double counting the same timestamp");
}

static void SessionDamageAggregatorMaximumHitAndIdempotency()
{
    var aggregator = new SessionDamageAggregator();
    aggregator.AddConfirmedEvent(1, 100);
    aggregator.AddConfirmedEvent(2, 500);
    aggregator.AddConfirmedEvent(1, 100);
    var snapshot = aggregator.BuildSnapshot(10);
    Assert(snapshot.MaximumHit == 500, "maximum hit tracks the largest confirmed event");
    Assert(snapshot.TotalDamage == 600, "duplicate event ids never double count");
    Assert(aggregator.EventCount == 2, "event count reflects unique ids");
}

static void SessionDamageRateUsesClockDenominator()
{
    var clock = new SessionClock();
    clock.Start(0);
    clock.Pause(5);
    var aggregator = new SessionDamageAggregator();
    aggregator.AddConfirmedEvent(1, 100);
    aggregator.AddConfirmedEvent(2, 100);
    var snapshot = aggregator.BuildSnapshot(clock.Snapshot.EffectiveSeconds);
    Assert(snapshot.IsRateAvailable, "a positive total with a positive denominator yields a rate");
    Assert(snapshot.SessionAverageDps == 40,
        "session DPS divides total by effective run time only (200 over 5s)");
}

static void TownStateTrackerRequiresHysteresisAndKeepsUnknown()
{
    var tracker = new TownStateTracker(confirmFrames: 3);
    Assert(tracker.Snapshot.State == VisibleTownState.Unknown, "no evidence keeps Unknown");
    tracker.AddFrame(0, [new TownCandidateObservation(VisibleTownState.InTown, 0, 0.9)]);
    tracker.AddFrame(1, [new TownCandidateObservation(VisibleTownState.InTown, 1, 0.9)]);
    Assert(tracker.Snapshot.State == VisibleTownState.Unknown, "two frames stay below the hysteresis window");
    tracker.AddFrame(2, [new TownCandidateObservation(VisibleTownState.InTown, 2, 0.9)]);
    Assert(tracker.Snapshot.State == VisibleTownState.InTown, "three consecutive frames confirm the state");
    Assert(Math.Abs(tracker.Snapshot.UnknownSeconds - 2) < 0.001,
        "only the confirmed state accrues town time");
}

static void TownStateTrackerRejectsWeakCandidates()
{
    var tracker = new TownStateTracker(confirmFrames: 2, minimumConfidence: 0.8);
    tracker.AddFrame(0, [new TownCandidateObservation(VisibleTownState.InTown, 0, 0.5)]);
    Assert(tracker.Snapshot.State == VisibleTownState.Unknown, "a low-confidence candidate never changes state");
    Assert(tracker.Snapshot.RejectedCandidateCount == 1, "the weak candidate has a rejection receipt");
}

static void CombatActivityTrackerEnterExitHysteresis()
{
    var tracker = new CombatActivityTracker(activeWindowSeconds: 4, exitHysteresisSeconds: 1.5);
    tracker.AddDamageEvent(0);
    tracker.AddFrame(0);
    Assert(tracker.Snapshot.IsActive, "a confirmed event enters combat activity");
    tracker.AddFrame(5);
    Assert(tracker.Snapshot.IsActive, "short silence stays inside the hysteresis window");
    tracker.AddFrame(6);
    Assert(!tracker.Snapshot.IsActive, "silence beyond the window plus hysteresis exits activity");
    Assert(tracker.Snapshot.ExitCount == 1, "the exit transition has a receipt");
}

static void RealtimeStatisticsSessionExposesP1Fields()
{
    var session = new RealtimeStatisticsSession(minimumConfidence: 0.8);
    session.Start();
    var first = Observation(35_800_000, 1, 500, 300, "3.58亿");
    var second = Observation(35_800_000, 1.1, 502, 288, "3.58亿");
    session.AddFrame(1, Readout([first]));
    session.AddFrame(1.1, Readout([second]));
    var snapshot = session.Snapshot;
    Assert(snapshot.MaximumHit == 35_800_000, "maximum hit reaches the snapshot");
    Assert(snapshot.TotalDamage == 35_800_000, "confirmed damage reaches the session snapshot");
    Assert(snapshot.TownState == VisibleTownState.Unknown, "no town evidence keeps Unknown");
    Assert(snapshot.TotalRunSeconds > 0, "the session clock advances with frames");
    Assert(snapshot.IsSessionRateAvailable && snapshot.SessionAverageDps > 0,
        "session DPS is available after confirmed damage");
}

static RealtimeVisionReadout Readout(IReadOnlyList<CombatTextObservation> damage) => new(
    damage,
    Array.Empty<VisibleCounterObservation>(),
    Array.Empty<VisibleProgressObservation>(),
    Array.Empty<VisibleBuffObservation>(),
    Array.Empty<VisibleMapMarkerObservation>(),
    0.9);
