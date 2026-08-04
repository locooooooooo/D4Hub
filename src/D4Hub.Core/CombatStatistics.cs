using System.Globalization;
using System.Text.RegularExpressions;

namespace D4Hub.Core;

public readonly record struct CombatTextObservation(
    long Damage,
    double TimeSeconds,
    double CenterX,
    double CenterY,
    double Width,
    double Height,
    string RawText,
    double Confidence = 1,
    string? RejectionReason = null,
    double RecognitionPosterior = double.NaN);

public readonly record struct CombatParsedDamage(
    long Damage,
    int Start,
    int Length,
    string RawText);

public sealed record CombatDamageEvent(
    int Id,
    long Damage,
    double FirstSeenSeconds,
    double LastSeenSeconds,
    double FirstCenterX,
    double FirstCenterY,
    double LastCenterX,
    double LastCenterY,
    int ObservationCount,
    string RawText,
    double EvidenceScore);

public sealed record CombatSessionSummary(
    int Index,
    double StartSeconds,
    double EndSeconds,
    double ActiveDurationSeconds,
    long TotalDamage,
    int HitCount,
    double AverageDps,
    long RecentOneSecondDamage,
    long PeakOneSecondDamage,
    long MaximumHit,
    bool IsActive,
    double LastDamageSeconds);

public enum CombatEvidenceState
{
    NoCandidates,
    InsufficientEvidence,
    ConfirmedScreenEstimate
}

public sealed record CombatEvidenceSummary(
    CombatEvidenceState State,
    int ConfirmedObservationCount,
    int PendingObservationCount,
    double ConfirmedObservationCoverage,
    string Detail);

public sealed record CombatDamageReport(
    int ReceivedObservationCount,
    int ParsedObservationCount,
    int UniqueEventCount,
    int DuplicateObservationCount,
    int RejectedObservationCount,
    IReadOnlyDictionary<string, int> RejectionReasons,
    long TotalDamage,
    long CurrentOneSecondDamage,
    IReadOnlyList<CombatDamageEvent> Events,
    IReadOnlyList<CombatSessionSummary> Sessions,
    CombatEvidenceSummary Evidence);

public static partial class CombatDamageTextParser
{
    [GeneratedRegex(@"(?<number>\d[\d,，]*(?:[\.．]\d+)?)\s*(?<unit>[万亿兆京])", RegexOptions.CultureInvariant)]
    private static partial Regex DamageValueRegex();

    public static IReadOnlyList<long> ParseAll(string? text)
        => ParseMatches(text).Select(match => match.Damage).ToArray();

    public static IReadOnlyList<CombatParsedDamage> ParseMatches(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<CombatParsedDamage>();
        }

        var values = new List<CombatParsedDamage>();
        foreach (Match match in DamageValueRegex().Matches(text))
        {
            var numberText = match.Groups["number"].Value
                .Replace("，", string.Empty, StringComparison.Ordinal)
                .Replace(",", string.Empty, StringComparison.Ordinal)
                .Replace('．', '.');
            if (!decimal.TryParse(
                    numberText,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var number)
                || number <= 0)
            {
                continue;
            }

            var multiplier = match.Groups["unit"].ValueSpan[0] switch
            {
                '京' => 10_000_000_000_000_000m,
                '兆' => 1_000_000_000_000m,
                '亿' => 100_000_000m,
                _ => 10_000m
            };
            var scaled = decimal.Round(number * multiplier, 0, MidpointRounding.AwayFromZero);
            if (scaled > long.MaxValue)
            {
                continue;
            }

            values.Add(new CombatParsedDamage(
                (long)scaled,
                match.Index,
                match.Length,
                match.Value));
        }

        return values;
    }

    public static bool TryParse(string? text, out long damage)
    {
        var values = ParseAll(text);
        damage = values.Count == 1 ? values[0] : 0;
        return values.Count == 1;
    }
}

/// <summary>
/// Fuses visible combat-text observations into unique damage events. A single
/// OCR result is never counted: an event needs consistent evidence from at
/// least two distinct frames.
/// </summary>
public sealed class CombatDamageTracker
{
    public const double DefaultSamplingIntervalSeconds = 0.6;
    public const double DefaultTrackLifetimeSeconds = 1.35;
    public const double DefaultSessionGapSeconds = 4;
    public const int DefaultMinimumConfirmationFrames = 2;

    private const double DefaultMaximumTrackDistance = 96;
    private const double DefaultMinimumObservationEvidence = 0.5;
    private const double DefaultMinimumConsensus = 2d / 3d;
    private const double MinimumSessionDurationSeconds = 1;
    private const double TimestampToleranceSeconds = 0.001;

    private readonly double _trackLifetimeSeconds;
    private readonly double _maximumTrackDistanceSquared;
    private readonly double _sessionGapSeconds;
    private readonly double _minimumObservationEvidence;
    private readonly double _minimumConsensus;
    private readonly int _minimumConfirmationFrames;
    private readonly List<DamageTrack> _activeTracks = new();
    private readonly List<DamageTrack> _completedTracks = new();
    private readonly Dictionary<string, int> _rejectionReasons = new(StringComparer.Ordinal);
    private int _nextTrackId = 1;
    private int _receivedObservationCount;
    private int _parsedObservationCount;
    private double _lastFrameTime = double.NegativeInfinity;

    public CombatDamageTracker(
        double trackLifetimeSeconds = DefaultTrackLifetimeSeconds,
        double maximumTrackDistance = DefaultMaximumTrackDistance,
        double sessionGapSeconds = DefaultSessionGapSeconds,
        double minimumObservationConfidence = DefaultMinimumObservationEvidence,
        int minimumConfirmationFrames = DefaultMinimumConfirmationFrames,
        double minimumConsensus = DefaultMinimumConsensus)
    {
        if (!double.IsFinite(trackLifetimeSeconds) || trackLifetimeSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trackLifetimeSeconds));
        }

        if (!double.IsFinite(maximumTrackDistance) || maximumTrackDistance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTrackDistance));
        }

        if (!double.IsFinite(sessionGapSeconds) || sessionGapSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionGapSeconds));
        }

        if (!double.IsFinite(minimumObservationConfidence)
            || minimumObservationConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumObservationConfidence));
        }

        if (minimumConfirmationFrames < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfirmationFrames));
        }

        if (!double.IsFinite(minimumConsensus) || minimumConsensus is <= 0.5 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConsensus));
        }

        _trackLifetimeSeconds = trackLifetimeSeconds;
        _maximumTrackDistanceSquared = maximumTrackDistance * maximumTrackDistance;
        _sessionGapSeconds = sessionGapSeconds;
        _minimumObservationEvidence = minimumObservationConfidence;
        _minimumConfirmationFrames = minimumConfirmationFrames;
        _minimumConsensus = minimumConsensus;
    }

    public void AddFrame(double timeSeconds, IEnumerable<CombatTextObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds < _lastFrameTime)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        _lastFrameTime = timeSeconds;
        ExpireTracks(timeSeconds);

        var matchedTracks = new HashSet<DamageTrack>();
        foreach (var observation in observations)
        {
            _receivedObservationCount++;
            if (!TryAcceptObservation(timeSeconds, observation))
            {
                continue;
            }

            _parsedObservationCount++;
            var best = FindBestTrack(observation, matchedTracks);
            if (best is null)
            {
                best = new DamageTrack(_nextTrackId++, observation);
                _activeTracks.Add(best);
            }
            else
            {
                best.Update(observation);
            }

            var trackRejection = best.TryConfirm(_minimumConfirmationFrames, _minimumConsensus);
            if (trackRejection is not null)
            {
                Reject(trackRejection, best.ObservationCount);
                _activeTracks.Remove(best);
            }
            matchedTracks.Add(best);
        }
    }

    public CombatDamageReport BuildReport()
    {
        var allTracks = _completedTracks.Concat(_activeTracks).ToArray();
        var events = allTracks
            .Where(track => track.IsConfirmed)
            .OrderBy(track => track.FirstSeenSeconds)
            .ThenBy(track => track.Id)
            .Select(track => track.ToEvent())
            .ToArray();
        var reportAtSeconds = double.IsFinite(_lastFrameTime) ? _lastFrameTime : 0;
        var sessions = BuildSessions(events, reportAtSeconds);
        var totalDamage = events.Aggregate(0L, (total, item) => checked(total + item.Damage));
        var currentOneSecondDamage = sessions.LastOrDefault()?.RecentOneSecondDamage ?? 0;
        var confirmedObservationCount = allTracks
            .Where(track => track.IsConfirmed)
            .Sum(track => track.ConfirmedObservationCount);
        var pendingObservationCount = _activeTracks
            .Where(track => !track.IsConfirmed)
            .Sum(track => track.ObservationCount);
        var duplicateObservationCount = allTracks
            .Where(track => track.IsConfirmed)
            .Sum(track => Math.Max(0, track.ConfirmedObservationCount - 1));
        var coverage = _parsedObservationCount == 0
            ? 0
            : confirmedObservationCount / (double)_parsedObservationCount;
        var evidenceState = events.Length > 0
            ? CombatEvidenceState.ConfirmedScreenEstimate
            : _parsedObservationCount > 0 || _rejectionReasons.Count > 0
                ? CombatEvidenceState.InsufficientEvidence
                : CombatEvidenceState.NoCandidates;
        var evidenceDetail = evidenceState switch
        {
            CombatEvidenceState.ConfirmedScreenEstimate =>
                "Confirmed multi-frame screen observations; occluded or short-lived hits may be missing.",
            CombatEvidenceState.InsufficientEvidence =>
                "Visible candidates did not meet the multi-frame confirmation requirement.",
            _ => "No visible combat-text candidates were parsed."
        };

        return new CombatDamageReport(
            _receivedObservationCount,
            _parsedObservationCount,
            events.Length,
            duplicateObservationCount,
            _rejectionReasons.Values.Sum(),
            new Dictionary<string, int>(_rejectionReasons, StringComparer.Ordinal),
            totalDamage,
            currentOneSecondDamage,
            events,
            sessions,
            new CombatEvidenceSummary(
                evidenceState,
                confirmedObservationCount,
                pendingObservationCount,
                coverage,
                evidenceDetail));
    }

    private bool TryAcceptObservation(double frameTimeSeconds, CombatTextObservation observation)
    {
        if (observation.Damage <= 0)
        {
            Reject("invalid-damage");
            return false;
        }

        if (!double.IsFinite(observation.TimeSeconds)
            || Math.Abs(observation.TimeSeconds - frameTimeSeconds) > TimestampToleranceSeconds)
        {
            Reject("timestamp-mismatch");
            return false;
        }

        if (!double.IsFinite(observation.CenterX)
            || !double.IsFinite(observation.CenterY)
            || !double.IsFinite(observation.Width)
            || !double.IsFinite(observation.Height)
            || observation.Width <= 0
            || observation.Height <= 0)
        {
            Reject("invalid-geometry");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(observation.RejectionReason))
        {
            Reject(observation.RejectionReason);
            return false;
        }

        if (!double.IsFinite(observation.Confidence)
            || observation.Confidence < _minimumObservationEvidence)
        {
            Reject("low-evidence");
            return false;
        }

        if (double.IsFinite(observation.RecognitionPosterior)
            && observation.RecognitionPosterior is < 0 or > 1)
        {
            Reject("invalid-recognition-posterior");
            return false;
        }

        return true;
    }

    private DamageTrack? FindBestTrack(
        CombatTextObservation observation,
        HashSet<DamageTrack> matchedTracks)
    {
        DamageTrack? best = null;
        var bestScore = double.MaxValue;
        foreach (var track in _activeTracks)
        {
            if (matchedTracks.Contains(track) || (track.IsConfirmed && track.Damage != observation.Damage))
            {
                continue;
            }

            var (predictedX, predictedY) = track.PredictCenter(observation.TimeSeconds);
            var deltaX = predictedX - observation.CenterX;
            var deltaY = predictedY - observation.CenterY;
            var distanceSquared = deltaX * deltaX + deltaY * deltaY;
            var sameCandidate = track.HasCandidate(observation.Damage);
            var allowedDistanceSquared = sameCandidate
                ? _maximumTrackDistanceSquared
                : _maximumTrackDistanceSquared * 0.25;
            if (distanceSquared > allowedDistanceSquared)
            {
                continue;
            }

            var score = distanceSquared + (sameCandidate ? 0 : _maximumTrackDistanceSquared * 0.2);
            if (score < bestScore)
            {
                best = track;
                bestScore = score;
            }
        }

        return best;
    }

    private void Reject(string reason, int count = 1)
    {
        var normalized = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
        _rejectionReasons[normalized] = _rejectionReasons.GetValueOrDefault(normalized) + count;
    }

    private void ExpireTracks(double timeSeconds)
    {
        for (var index = _activeTracks.Count - 1; index >= 0; index--)
        {
            var track = _activeTracks[index];
            if (timeSeconds - track.LastSeenSeconds <= _trackLifetimeSeconds)
            {
                continue;
            }

            if (track.IsConfirmed)
            {
                _completedTracks.Add(track);
            }
            else
            {
                Reject("insufficient-multiframe-evidence", track.ObservationCount);
            }

            _activeTracks.RemoveAt(index);
        }
    }

    private IReadOnlyList<CombatSessionSummary> BuildSessions(
        IReadOnlyList<CombatDamageEvent> events,
        double reportAtSeconds)
    {
        if (events.Count == 0)
        {
            return Array.Empty<CombatSessionSummary>();
        }

        var sessions = new List<CombatSessionSummary>();
        var sessionStart = 0;
        for (var index = 1; index <= events.Count; index++)
        {
            if (index < events.Count
                && events[index].FirstSeenSeconds - events[index - 1].FirstSeenSeconds <= _sessionGapSeconds)
            {
                continue;
            }

            sessions.Add(SummarizeSession(
                sessions.Count + 1,
                events,
                sessionStart,
                index,
                reportAtSeconds,
                _sessionGapSeconds));
            sessionStart = index;
        }

        return sessions;
    }

    private static CombatSessionSummary SummarizeSession(
        int index,
        IReadOnlyList<CombatDamageEvent> events,
        int start,
        int end,
        double reportAtSeconds,
        double sessionGapSeconds)
    {
        var firstSeen = events[start].FirstSeenSeconds;
        var lastDamage = events[start].FirstSeenSeconds;
        var totalDamage = 0L;
        var maximumHit = 0L;
        for (var eventIndex = start; eventIndex < end; eventIndex++)
        {
            var item = events[eventIndex];
            lastDamage = Math.Max(lastDamage, item.FirstSeenSeconds);
            totalDamage = checked(totalDamage + item.Damage);
            maximumHit = Math.Max(maximumHit, item.Damage);
        }

        var peakOneSecondDamage = 0L;
        var rollingDamage = 0L;
        var left = start;
        for (var right = start; right < end; right++)
        {
            rollingDamage = checked(rollingDamage + events[right].Damage);
            while (events[right].FirstSeenSeconds - events[left].FirstSeenSeconds > 1)
            {
                rollingDamage -= events[left].Damage;
                left++;
            }

            peakOneSecondDamage = Math.Max(peakOneSecondDamage, rollingDamage);
        }

        var recentOneSecondDamage = 0L;
        for (var eventIndex = start; eventIndex < end; eventIndex++)
        {
            var secondsAgo = reportAtSeconds - events[eventIndex].FirstSeenSeconds;
            if (secondsAgo is >= 0 and <= 1)
            {
                recentOneSecondDamage = checked(recentOneSecondDamage + events[eventIndex].Damage);
            }
        }

        var frozenEnd = lastDamage + sessionGapSeconds;
        var isActive = reportAtSeconds < frozenEnd && end == events.Count;
        var clockEnd = Math.Min(reportAtSeconds, frozenEnd);
        var activeDuration = Math.Max(MinimumSessionDurationSeconds, clockEnd - firstSeen);
        return new CombatSessionSummary(
            index,
            firstSeen,
            clockEnd,
            activeDuration,
            totalDamage,
            end - start,
            totalDamage / activeDuration,
            recentOneSecondDamage,
            peakOneSecondDamage,
            maximumHit,
            isActive,
            lastDamage);
    }

    private sealed class DamageTrack
    {
        private readonly Dictionary<long, CandidateVote> _votes = new();
        private double _velocityX;
        private double _velocityY;

        public DamageTrack(int id, CombatTextObservation observation)
        {
            Id = id;
            FirstSeenSeconds = LastSeenSeconds = observation.TimeSeconds;
            FirstCenterX = LastCenterX = observation.CenterX;
            FirstCenterY = LastCenterY = observation.CenterY;
            ObservationCount = 1;
            AddVote(observation);
        }

        public int Id { get; }
        public long Damage { get; private set; }
        public bool IsConfirmed { get; private set; }
        public double FirstSeenSeconds { get; }
        public double LastSeenSeconds { get; private set; }
        public double FirstCenterX { get; }
        public double FirstCenterY { get; }
        public double LastCenterX { get; private set; }
        public double LastCenterY { get; private set; }
        public int ObservationCount { get; private set; }
        public int ConfirmedObservationCount { get; private set; }
        public string ConfirmedRawText { get; private set; } = string.Empty;
        public double ConfirmedEvidenceScore { get; private set; }

        public bool HasCandidate(long damage) => _votes.ContainsKey(damage);

        public (double X, double Y) PredictCenter(double timeSeconds)
        {
            var elapsed = Math.Max(0, timeSeconds - LastSeenSeconds);
            return (LastCenterX + _velocityX * elapsed, LastCenterY + _velocityY * elapsed);
        }

        public void Update(CombatTextObservation observation)
        {
            var elapsed = observation.TimeSeconds - LastSeenSeconds;
            if (elapsed > 0)
            {
                var measuredVelocityX = (observation.CenterX - LastCenterX) / elapsed;
                var measuredVelocityY = (observation.CenterY - LastCenterY) / elapsed;
                _velocityX = ObservationCount == 1
                    ? measuredVelocityX
                    : (_velocityX * 0.6) + (measuredVelocityX * 0.4);
                _velocityY = ObservationCount == 1
                    ? measuredVelocityY
                    : (_velocityY * 0.6) + (measuredVelocityY * 0.4);
            }

            LastSeenSeconds = observation.TimeSeconds;
            LastCenterX = observation.CenterX;
            LastCenterY = observation.CenterY;
            ObservationCount++;
            AddVote(observation);
        }

        public string? TryConfirm(int minimumFrames, double minimumConsensus)
        {
            var winner = _votes
                .OrderByDescending(pair => pair.Value.Count)
                .ThenByDescending(pair => pair.Value.PosteriorTotal)
                .First();
            var consensus = winner.Value.Count / (double)ObservationCount;
            if (winner.Value.Count < minimumFrames || consensus < minimumConsensus)
            {
                return null;
            }

            if (IsConfirmed && Damage != winner.Key)
            {
                return null;
            }

            if (_votes.Count > 1)
            {
                var minimumDamage = _votes.Keys.Min();
                var maximumDamage = _votes.Keys.Max();
                if (maximumDamage / (decimal)minimumDamage >= 100)
                {
                    return "candidate-magnitude-conflict";
                }
            }

            IsConfirmed = true;
            Damage = winner.Key;
            ConfirmedObservationCount = winner.Value.Count;
            ConfirmedRawText = winner.Value.RawText;
            ConfirmedEvidenceScore = winner.Value.PosteriorTotal / winner.Value.Count;
            return null;
        }

        public CombatDamageEvent ToEvent()
        {
            if (!IsConfirmed)
            {
                throw new InvalidOperationException("An unconfirmed damage track cannot become an event.");
            }

            return new CombatDamageEvent(
                Id,
                Damage,
                FirstSeenSeconds,
                LastSeenSeconds,
                FirstCenterX,
                FirstCenterY,
                LastCenterX,
                LastCenterY,
                ConfirmedObservationCount,
                ConfirmedRawText,
                ConfirmedEvidenceScore);
        }

        private void AddVote(CombatTextObservation observation)
        {
            var evidence = double.IsFinite(observation.RecognitionPosterior)
                ? observation.RecognitionPosterior
                : observation.Confidence;
            var current = _votes.GetValueOrDefault(observation.Damage);
            _votes[observation.Damage] = new CandidateVote(
                current.Count + 1,
                current.PosteriorTotal + evidence,
                string.IsNullOrWhiteSpace(current.RawText) ? observation.RawText : current.RawText);
        }

        private readonly record struct CandidateVote(int Count, double PosteriorTotal, string RawText);
    }
}
