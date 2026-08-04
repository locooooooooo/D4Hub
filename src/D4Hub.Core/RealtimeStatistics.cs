namespace D4Hub.Core;

/// <summary>
/// The capture state exposed by the realtime statistics panel.
/// </summary>
public enum RealtimeCaptureStatus
{
    Disabled,
    WaitingForGame,
    Capturing,
    Paused,
    NoData,
    LowConfidence,
    InsufficientEvidence,
    Error
}

public enum RealtimeVisionQualityLevel
{
    Unavailable,
    InsufficientEvidence,
    BaselineScreenEstimate,
    ExperimentalVisualEstimate,
    CalibratedVisualEstimate
}

/// <summary>
/// A data-quality receipt, not an accuracy probability. Coverage is the
/// fraction of parsed observations supporting confirmed events in this run.
/// </summary>
public sealed record RealtimeVisionQuality(
    RealtimeVisionQualityLevel Level,
    string Pipeline,
    double? Coverage,
    string Detail)
{
    public static RealtimeVisionQuality Unavailable(string detail) => new(
        RealtimeVisionQualityLevel.Unavailable,
        "unavailable",
        null,
        detail);

    public static RealtimeVisionQuality Baseline(string detail) => new(
        RealtimeVisionQualityLevel.BaselineScreenEstimate,
        "windows-ocr-baseline",
        null,
        detail);
}

/// <summary>
/// The values a vision/OCR adapter may return for one captured frame.
/// An adapter must leave the collections empty when it cannot establish a
/// trustworthy reading; the session never invents values from an image.
/// </summary>
public sealed record RealtimeVisionReadout(
    IReadOnlyList<CombatTextObservation> Damage,
    IReadOnlyList<VisibleCounterObservation> Counters,
    IReadOnlyList<VisibleProgressObservation> Progress,
    IReadOnlyList<VisibleBuffObservation> Buffs,
    IReadOnlyList<VisibleMapMarkerObservation> MapMarkers,
    double Confidence,
    RealtimeVisionQuality? Quality = null)
{
    public IReadOnlyList<MaterialPickupObservation> MaterialPickups { get; init; } =
        Array.Empty<MaterialPickupObservation>();

    public static RealtimeVisionReadout Empty { get; } = new(
        Array.Empty<CombatTextObservation>(),
        Array.Empty<VisibleCounterObservation>(),
        Array.Empty<VisibleProgressObservation>(),
        Array.Empty<VisibleBuffObservation>(),
        Array.Empty<VisibleMapMarkerObservation>(),
        0);
}

/// <summary>
/// Declares which observation kinds an adapter can actually produce. The UI
/// uses this receipt to distinguish an empty trusted reading from a feature
/// that is not implemented by the active adapter.
/// </summary>
public sealed record RealtimeVisionCapabilities(
    bool Damage,
    bool Counters,
    bool Progress,
    bool Buffs,
    bool Map,
    bool Pickups = false)
{
    public static RealtimeVisionCapabilities None { get; } = new(false, false, false, false, false);
    public static RealtimeVisionCapabilities DamageOnly { get; } = new(true, false, false, false, false);
    public static RealtimeVisionCapabilities DamageWithPickups { get; } = new(true, false, false, false, false, true);
    public static RealtimeVisionCapabilities All { get; } = new(true, true, true, true, true);
}

/// <summary>
/// Boundary for a future OCR/template implementation. Implementations must
/// only report observations that were actually visible in the supplied frame.
/// </summary>
public interface IRealtimeVisionAdapter
{
    RealtimeVisionCapabilities Capabilities => RealtimeVisionCapabilities.None;

    Task<RealtimeVisionReadout> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicit no-op adapter used until an OCR implementation is available.
/// </summary>
public sealed class NoopRealtimeVisionAdapter : IRealtimeVisionAdapter
{
    public Task<RealtimeVisionReadout> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(calibration);
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        return Task.FromResult(RealtimeVisionReadout.Empty);
    }
}

public sealed record RealtimeStatisticsSnapshot(
    RealtimeCaptureStatus Status,
    bool IsEnabled,
    bool HasData,
    double Confidence,
    RealtimeVisionQuality DataQuality,
    double? LastSampleSeconds,
    long CurrentDps,
    long RecentOneSecondDamage,
    long PeakOneSecondDamage,
    long TotalDamage,
    int AcceptedDamageEvents,
    int RejectedDamageObservations,
    VisibleCounterReport Counters,
    VisibleProgressReport Progress,
    VisibleBuffReport Buffs,
    VisibleMapReport Map,
    string StatusDetail,
    long MaximumHit,
    long SessionAverageDps,
    bool IsSessionRateAvailable,
    double TotalRunSeconds,
    double TownSeconds,
    double OutOfTownSeconds,
    double UnknownSeconds,
    VisibleTownState TownState,
    bool IsCombatActive,
    double CombatActiveSeconds,
    double CombatInactiveSeconds)
{
    public MaterialPickupReport MaterialPickups { get; init; } = MaterialPickupReport.Empty;
}

/// <summary>
/// Stateful, resettable aggregation for one realtime capture session.
/// </summary>
public sealed class RealtimeStatisticsSession
{
    private readonly double _minimumConfidence;
    private readonly double _damageTrackLifetimeSeconds;
    private CombatDamageTracker _damage;
    private VisibleCounterTracker _counters;
    private VisibleProgressTracker _progress;
    private VisibleBuffTracker _buffs;
    private VisibleMapTracker _map;
    private MaterialPickupTracker _materialPickups;
    private readonly SessionClock _clock = new();
    private readonly TownStateTracker _town = new();
    private readonly CombatActivityTracker _activity = new();
    private readonly SessionDamageAggregator _sessionDamage = new();
    private double _lastKnownTimeSeconds;
    private bool _isEnabled;
    private double? _lastSampleSeconds;
    private RealtimeVisionQuality _lastInputQuality;
    private RealtimeStatisticsSnapshot _snapshot;

    public RealtimeStatisticsSession(
        double minimumConfidence = 0.8,
        double damageSamplingIntervalSeconds = CombatDamageTracker.DefaultSamplingIntervalSeconds)
    {
        if (!double.IsFinite(minimumConfidence) || minimumConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        }

        if (!double.IsFinite(damageSamplingIntervalSeconds) || damageSamplingIntervalSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(damageSamplingIntervalSeconds));
        }

        _minimumConfidence = minimumConfidence;
        _damageTrackLifetimeSeconds = Math.Max(0.3, damageSamplingIntervalSeconds * 2.25);
        _damage = CreateDamageTracker();
        _counters = new VisibleCounterTracker(minimumConfidence);
        _progress = new VisibleProgressTracker(minimumConfidence);
        _buffs = new VisibleBuffTracker(minimumConfidence);
        _map = new VisibleMapTracker(minimumConfidence);
        _materialPickups = new MaterialPickupTracker(minimumConfidence);
        _lastInputQuality = RealtimeVisionQuality.Unavailable("No frame has been evaluated.");
        _snapshot = CreateSnapshot(
            RealtimeCaptureStatus.Disabled,
            false,
            0,
            RealtimeVisionQuality.Unavailable("Realtime capture is disabled."),
            "Realtime capture is disabled.");
    }

    public RealtimeStatisticsSnapshot Snapshot => _snapshot;

    public void Start()
    {
        _isEnabled = true;
        _clock.Start(_lastKnownTimeSeconds);
        _snapshot = CreateSnapshot(
            RealtimeCaptureStatus.NoData,
            true,
            0,
            RealtimeVisionQuality.Unavailable("No frame has been evaluated."),
            "Waiting for a trusted visible-statistics reading.");
    }

    public void Pause()
    {
        _isEnabled = false;
        _clock.Pause(_lastKnownTimeSeconds);
        _snapshot = CreateSnapshot(
            RealtimeCaptureStatus.Paused,
            false,
            _snapshot.Confidence,
            _snapshot.DataQuality,
            "Realtime capture is paused.");
    }

    public void Reset()
    {
        _damage = CreateDamageTracker();
        _counters = new VisibleCounterTracker(_minimumConfidence);
        _progress = new VisibleProgressTracker(_minimumConfidence);
        _buffs = new VisibleBuffTracker(_minimumConfidence);
        _map = new VisibleMapTracker(_minimumConfidence);
        _materialPickups = new MaterialPickupTracker(_minimumConfidence);
        _clock.Reset(_lastKnownTimeSeconds);
        _town.Reset(_lastKnownTimeSeconds);
        _activity.Reset(_lastKnownTimeSeconds);
        _sessionDamage.Reset();
        _lastSampleSeconds = null;
        _lastInputQuality = RealtimeVisionQuality.Unavailable("Realtime statistics were cleared.");
        _snapshot = CreateSnapshot(
            _isEnabled ? RealtimeCaptureStatus.NoData : RealtimeCaptureStatus.Paused,
            _isEnabled,
            0,
            RealtimeVisionQuality.Unavailable("Realtime statistics were cleared."),
            "Realtime statistics were cleared.");
    }

    public RealtimeStatisticsSnapshot MarkWaitingForGame(double timeSeconds)
    {
        ValidateTime(timeSeconds);
        _lastKnownTimeSeconds = timeSeconds;
        _clock.MarkWaitingForGame(timeSeconds);
        return _isEnabled
            ? SetStatus(
                RealtimeCaptureStatus.WaitingForGame,
                _snapshot.Confidence,
                _snapshot.DataQuality,
                "Waiting for the Diablo IV window.")
            : SetStatus(
                RealtimeCaptureStatus.Paused,
                _snapshot.Confidence,
                _snapshot.DataQuality,
                "Realtime capture is paused.");
    }

    public RealtimeStatisticsSnapshot MarkFocusLost(double timeSeconds)
    {
        ValidateTime(timeSeconds);
        _lastKnownTimeSeconds = timeSeconds;
        _clock.MarkFocusLost(timeSeconds);
        return _isEnabled
            ? SetStatus(
                RealtimeCaptureStatus.WaitingForGame,
                _snapshot.Confidence,
                _snapshot.DataQuality,
                "Focus lost; the statistics clock is not advancing.")
            : SetStatus(
                RealtimeCaptureStatus.Paused,
                _snapshot.Confidence,
                _snapshot.DataQuality,
                "Realtime capture is paused.");
    }

    public RealtimeStatisticsSnapshot AddTownCandidate(double timeSeconds, VisibleTownState candidate, double confidence)
    {
        ValidateTime(timeSeconds);
        _lastKnownTimeSeconds = timeSeconds;
        _town.AddFrame(timeSeconds, [new TownCandidateObservation(candidate, timeSeconds, confidence)]);
        return _isEnabled
            ? SetStatus(_snapshot.Status, _snapshot.Confidence, _snapshot.DataQuality, _snapshot.StatusDetail)
            : SetStatus(
                RealtimeCaptureStatus.Paused,
                _snapshot.Confidence,
                _snapshot.DataQuality,
                "Realtime capture is paused.");
    }

    public RealtimeStatisticsSnapshot AddFrame(double timeSeconds, RealtimeVisionReadout readout)
    {
        ArgumentNullException.ThrowIfNull(readout);
        ValidateTime(timeSeconds);
        if (!_isEnabled)
        {
            return SetStatus(
                RealtimeCaptureStatus.Paused,
                _snapshot.Confidence,
                _snapshot.DataQuality,
                "Realtime capture is paused.");
        }

        _lastSampleSeconds = timeSeconds;
        _lastKnownTimeSeconds = timeSeconds;
        if (_clock.State != SessionClockState.Running)
        {
            _clock.Start(timeSeconds);
        }

        _clock.Tick(timeSeconds);
        _damage.AddFrame(timeSeconds, readout.Damage);
        foreach (var counter in readout.Counters)
        {
            _counters.Add(counter);
        }

        foreach (var progress in readout.Progress)
        {
            _progress.Add(progress);
        }

        _buffs.AddFrame(timeSeconds, readout.Buffs);
        _map.AddFrame(timeSeconds, readout.MapMarkers);
        _materialPickups.AddFrame(timeSeconds, readout.MaterialPickups);
        var report = _damage.BuildReport();
        foreach (var damageEvent in report.Events)
        {
            _sessionDamage.AddConfirmedEvent(damageEvent.Id, damageEvent.Damage);
            _activity.AddDamageEvent(damageEvent.FirstSeenSeconds);
        }

        _activity.AddFrame(timeSeconds);
        var pickupReport = _materialPickups.BuildReport();
        var hasAcceptedData = report.UniqueEventCount > 0
            || _counters.BuildReport().Counters.Count > 0
            || _progress.BuildReport().Progress.Count > 0
            || _buffs.BuildReport().Buffs.Count > 0
            || _map.BuildReport().FreshMarkers.Count > 0
            || pickupReport.ConfirmedEventCount > 0;
        var hasPendingPickupEvidence = pickupReport.PendingObservationCount > 0;
        if (readout.Quality is not null)
        {
            _lastInputQuality = readout.Quality;
        }
        else if (readout.Confidence > 0)
        {
            _lastInputQuality = RealtimeVisionQuality.Baseline(
                "Legacy adapter evidence score is available; it is not a calibrated accuracy probability.");
        }

        var inputQuality = _lastInputQuality;
        var quality = BuildQuality(inputQuality, report);
        var status = hasAcceptedData
            ? RealtimeCaptureStatus.Capturing
             : report.Evidence.State == CombatEvidenceState.InsufficientEvidence
                 || hasPendingPickupEvidence
                ? RealtimeCaptureStatus.InsufficientEvidence
                : readout.Confidence > 0 && readout.Confidence < _minimumConfidence
                ? RealtimeCaptureStatus.LowConfidence
                : RealtimeCaptureStatus.NoData;
        var detail = status switch
        {
            RealtimeCaptureStatus.Capturing => "Visible statistics are being collected.",
            RealtimeCaptureStatus.LowConfidence => "The visible-statistics reading is below the confidence threshold.",
            RealtimeCaptureStatus.InsufficientEvidence =>
                "Visible candidates have not met the multi-frame confirmation requirement.",
            _ => "Screen captured; no trusted OCR statistics are available yet."
        };
        _snapshot = CreateSnapshot(status, true, readout.Confidence, quality, detail);
        return _snapshot;
    }

    private RealtimeStatisticsSnapshot SetStatus(
        RealtimeCaptureStatus status,
        double confidence,
        RealtimeVisionQuality quality,
        string detail)
    {
        _snapshot = CreateSnapshot(status, _isEnabled, confidence, quality, detail);
        return _snapshot;
    }

    private RealtimeStatisticsSnapshot CreateSnapshot(
        RealtimeCaptureStatus status,
        bool isEnabled,
        double confidence,
        RealtimeVisionQuality quality,
        string detail)
    {
        var report = _damage.BuildReport();
        var current = report.Sessions.LastOrDefault();
        var counters = _counters.BuildReport();
        var progress = _progress.BuildReport();
        var buffs = _buffs.BuildReport();
        var map = _map.BuildReport();
        var materialPickups = _materialPickups.BuildReport();
        var sessionDamage = _sessionDamage.BuildSnapshot(_clock.Snapshot.EffectiveSeconds);
        var town = _town.Snapshot;
        var activity = _activity.Snapshot;
        var hasData = report.UniqueEventCount > 0
            || counters.Counters.Count > 0
            || progress.Progress.Count > 0
            || buffs.Buffs.Count > 0
            || map.FreshMarkers.Count > 0
            || materialPickups.ConfirmedEventCount > 0;
        var snapshot = new RealtimeStatisticsSnapshot(
            status,
            isEnabled,
            hasData,
            Math.Clamp(confidence, 0, 1),
            quality,
            _lastSampleSeconds,
            current is null ? 0 : (long)Math.Round(current.AverageDps),
            report.CurrentOneSecondDamage,
            current?.PeakOneSecondDamage ?? 0,
            report.TotalDamage,
            report.UniqueEventCount,
            report.RejectedObservationCount,
            counters,
            progress,
            buffs,
            map,
            detail,
            sessionDamage.MaximumHit,
            sessionDamage.SessionAverageDps,
            sessionDamage.IsRateAvailable,
            sessionDamage.EffectiveSeconds,
            town.TownSeconds,
            town.OutOfTownSeconds,
            town.UnknownSeconds,
            town.State,
            activity.IsActive,
            activity.ActiveSeconds,
            activity.InactiveSeconds);
        return snapshot with { MaterialPickups = materialPickups };
    }

    private CombatDamageTracker CreateDamageTracker() => new(
        trackLifetimeSeconds: _damageTrackLifetimeSeconds,
        minimumObservationConfidence: _minimumConfidence);

    private static RealtimeVisionQuality BuildQuality(
        RealtimeVisionQuality input,
        CombatDamageReport report)
    {
        if (report.Evidence.State == CombatEvidenceState.InsufficientEvidence)
        {
            return new RealtimeVisionQuality(
                RealtimeVisionQualityLevel.InsufficientEvidence,
                input.Pipeline,
                report.Evidence.ConfirmedObservationCoverage,
                report.Evidence.Detail);
        }

        if (report.Evidence.State == CombatEvidenceState.ConfirmedScreenEstimate)
        {
            var level = input.Level switch
            {
                RealtimeVisionQualityLevel.CalibratedVisualEstimate =>
                    RealtimeVisionQualityLevel.CalibratedVisualEstimate,
                RealtimeVisionQualityLevel.ExperimentalVisualEstimate =>
                    RealtimeVisionQualityLevel.ExperimentalVisualEstimate,
                _ => RealtimeVisionQualityLevel.BaselineScreenEstimate
            };
            return new RealtimeVisionQuality(
                level,
                input.Pipeline,
                report.Evidence.ConfirmedObservationCoverage,
                report.Evidence.Detail);
        }

        return input;
    }

    private void ValidateTime(double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds < _lastSampleSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }
    }
}
