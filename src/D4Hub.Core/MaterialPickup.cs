using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace D4Hub.Core;

public readonly record struct MaterialPickupParseResult(
    bool IsAccepted,
    string ItemKey,
    string Label,
    long Quantity,
    double Confidence,
    string? RejectionReason);

public readonly record struct MaterialPickupObservation(
    string ItemKey,
    string Label,
    long Quantity,
    double TimeSeconds,
    double CenterX,
    double CenterY,
    double Width,
    double Height,
    string RawText,
    double Confidence = 1,
    string? RejectionReason = null);

public sealed record MaterialPickupEvent(
    int Id,
    string ItemKey,
    string Label,
    long Quantity,
    double FirstSeenSeconds,
    double ConfirmedSeconds,
    double CenterX,
    double CenterY,
    int ObservationCount,
    double EvidenceScore,
    string RawText);

public sealed record MaterialPickupReport(
    int ReceivedObservationCount,
    int ParsedObservationCount,
    int ConfirmedEventCount,
    int DuplicateObservationCount,
    int RejectedObservationCount,
    int PendingObservationCount,
    IReadOnlyList<MaterialPickupEvent> Events)
{
    public static MaterialPickupReport Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        0,
        Array.Empty<MaterialPickupEvent>());

    public long TotalQuantity => Events.Sum(item => item.Quantity);

    public long CurrencyQuantity => Events
        .Where(item => string.Equals(item.Label, "金币", StringComparison.Ordinal))
        .Sum(item => item.Quantity);

    public long ItemQuantity => checked(TotalQuantity - CurrencyQuantity);

    public MaterialPickupRateSummary CalculateRates(double effectiveSeconds) =>
        new(
            ItemQuantity,
            CurrencyQuantity,
            CalculateRate(ItemQuantity, effectiveSeconds),
            CalculateRate(CurrencyQuantity, effectiveSeconds),
            effectiveSeconds);

    private static long CalculateRate(long quantity, double effectiveSeconds)
    {
        if (quantity <= 0 || !double.IsFinite(effectiveSeconds) || effectiveSeconds <= 0)
        {
            return 0;
        }

        var rate = quantity * 60d / effectiveSeconds;
        return rate >= long.MaxValue
            ? long.MaxValue
            : (long)Math.Round(rate, MidpointRounding.AwayFromZero);
    }
}

public sealed record MaterialPickupRateSummary(
    long ItemQuantity,
    long CurrencyQuantity,
    long ItemsPerMinute,
    long CurrencyPerMinute,
    double EffectiveSeconds)
{
    public long ItemsPerHour => checked(ItemsPerMinute * 60);
    public long CurrencyPerHour => checked(CurrencyPerMinute * 60);
}

public static partial class MaterialPickupTextParser
{
    [GeneratedRegex(
        @"^\s*(?<prefix>[+＋])?\s*(?<quantity>\d[\d,，]*)\s*(?<label>[\u3400-\u9fff\uf900-\ufaff][\u3400-\u9fff\uf900-\ufaffA-Za-z0-9·_-]*)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PickupRegex();

    public static MaterialPickupParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Rejected("empty-text");
        }

        var normalized = string.Concat(text
            .Replace('＋', '+')
            .Normalize(NormalizationForm.FormKC)
            .Where(character => !char.IsWhiteSpace(character)));
        var match = PickupRegex().Match(normalized);
        if (!match.Success)
        {
            return Rejected("pickup-shape-not-recognized");
        }

        var digits = match.Groups["quantity"].Value
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("，", string.Empty, StringComparison.Ordinal);
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity)
            || quantity <= 0)
        {
            return Rejected("quantity-out-of-range");
        }

        var label = match.Groups["label"].Value;
        var hasPlusPrefix = match.Groups["prefix"].Success;
        var isCurrency = string.Equals(label, "金币", StringComparison.Ordinal);
        if (!hasPlusPrefix && !isCurrency)
        {
            return Rejected("missing-pickup-marker");
        }

        var confidence = hasPlusPrefix ? 0.92 : 0.88;
        var key = label.Normalize(NormalizationForm.FormKC);
        return new MaterialPickupParseResult(true, key, label, quantity, confidence, null);
    }

    private static MaterialPickupParseResult Rejected(string reason) =>
        new(false, string.Empty, string.Empty, 0, 0, reason);
}

public static class MaterialPickupObservationMapper
{
    public static IReadOnlyList<MaterialPickupObservation> Read(
        IEnumerable<CombatOcrLine> lines,
        double timeSeconds,
        double sourceOffsetX = 0,
        double sourceOffsetY = 0,
        double sourceScaleX = 1,
        double sourceScaleY = 1)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        if (!double.IsFinite(sourceOffsetX)
            || !double.IsFinite(sourceOffsetY)
            || !double.IsFinite(sourceScaleX)
            || !double.IsFinite(sourceScaleY)
            || sourceScaleX <= 0
            || sourceScaleY <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceScaleX));
        }

        var observations = new List<MaterialPickupObservation>();
        foreach (var line in lines)
        {
            if (line.Words.Count == 0)
            {
                continue;
            }

            var rawText = string.Concat(line.Words.Select(word => word.Text));
            var parsed = MaterialPickupTextParser.Parse(rawText);
            var left = line.Words.Min(word => word.X);
            var top = line.Words.Min(word => word.Y);
            var right = line.Words.Max(word => word.X + word.Width);
            var bottom = line.Words.Max(word => word.Y + word.Height);
            observations.Add(new MaterialPickupObservation(
                parsed.ItemKey,
                parsed.Label,
                parsed.Quantity,
                timeSeconds,
                sourceOffsetX + ((left + right) / 2 * sourceScaleX),
                sourceOffsetY + ((top + bottom) / 2 * sourceScaleY),
                (right - left) * sourceScaleX,
                (bottom - top) * sourceScaleY,
                rawText,
                parsed.Confidence,
                parsed.RejectionReason));
        }

        return observations;
    }
}

public sealed class MaterialPickupTracker
{
    public const double DefaultTrackLifetimeSeconds = 1.6;
    public const double DefaultMaximumTrackDistance = 140;
    public const int DefaultMinimumConfirmationFrames = 2;

    private readonly double _minimumConfidence;
    private readonly double _trackLifetimeSeconds;
    private readonly double _maximumTrackDistanceSquared;
    private readonly int _minimumConfirmationFrames;
    private readonly List<PickupTrack> _tracks = new();
    private readonly List<MaterialPickupEvent> _events = new();
    private double _lastFrameTime = double.NegativeInfinity;
    private int _nextTrackId = 1;
    private int _nextEventId = 1;
    private int _receivedObservationCount;
    private int _parsedObservationCount;
    private int _duplicateObservationCount;
    private int _rejectedObservationCount;

    public MaterialPickupTracker(
        double minimumConfidence = 0.8,
        double trackLifetimeSeconds = DefaultTrackLifetimeSeconds,
        double maximumTrackDistance = DefaultMaximumTrackDistance,
        int minimumConfirmationFrames = DefaultMinimumConfirmationFrames)
    {
        if (!double.IsFinite(minimumConfidence) || minimumConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        }

        if (!double.IsFinite(trackLifetimeSeconds) || trackLifetimeSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trackLifetimeSeconds));
        }

        if (!double.IsFinite(maximumTrackDistance) || maximumTrackDistance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumTrackDistance));
        }

        if (minimumConfirmationFrames < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfirmationFrames));
        }

        _minimumConfidence = minimumConfidence;
        _trackLifetimeSeconds = trackLifetimeSeconds;
        _maximumTrackDistanceSquared = maximumTrackDistance * maximumTrackDistance;
        _minimumConfirmationFrames = minimumConfirmationFrames;
    }

    public void AddFrame(double timeSeconds, IEnumerable<MaterialPickupObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (!double.IsFinite(timeSeconds)
            || timeSeconds < 0
            || timeSeconds < _lastFrameTime)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        _lastFrameTime = timeSeconds;
        _tracks.RemoveAll(track => timeSeconds - track.LastSeenSeconds > _trackLifetimeSeconds);
        var matchedTrackIds = new HashSet<int>();
        foreach (var observation in observations)
        {
            _receivedObservationCount++;
            if (!IsValid(observation))
            {
                _rejectedObservationCount++;
                continue;
            }

            _parsedObservationCount++;
            var track = FindTrack(observation, matchedTrackIds);
            if (track is null)
            {
                track = new PickupTrack(_nextTrackId++, observation);
                _tracks.Add(track);
            }
            else
            {
                matchedTrackIds.Add(track.TrackId);
                if (track.IsConfirmed)
                {
                    _duplicateObservationCount++;
                }

                track.Add(observation);
            }

            if (!track.IsConfirmed && track.ObservationCount >= _minimumConfirmationFrames)
            {
                track.IsConfirmed = true;
                _events.Add(new MaterialPickupEvent(
                    _nextEventId++,
                    track.ItemKey,
                    track.Label,
                    track.Quantity,
                    track.FirstSeenSeconds,
                    timeSeconds,
                    track.CenterX,
                    track.CenterY,
                    track.ObservationCount,
                    track.EvidenceScore,
                    track.RawText));
            }
        }
    }

    public MaterialPickupReport BuildReport() => new(
        _receivedObservationCount,
        _parsedObservationCount,
        _events.Count,
        _duplicateObservationCount,
        _rejectedObservationCount,
        _tracks.Count(track => !track.IsConfirmed),
        _events.ToArray());

    private PickupTrack? FindTrack(
        MaterialPickupObservation observation,
        IReadOnlySet<int> matchedTrackIds)
    {
        return _tracks
            .Where(track => !matchedTrackIds.Contains(track.TrackId)
                && string.Equals(track.ItemKey, observation.ItemKey, StringComparison.Ordinal)
                && track.Quantity == observation.Quantity)
            .Select(track => new
            {
                Track = track,
                DistanceSquared = DistanceSquared(track.CenterX, track.CenterY, observation.CenterX, observation.CenterY)
            })
            .Where(candidate => candidate.DistanceSquared <= _maximumTrackDistanceSquared)
            .OrderBy(candidate => candidate.DistanceSquared)
            .Select(candidate => candidate.Track)
            .FirstOrDefault();
    }

    private bool IsValid(MaterialPickupObservation observation) =>
        !string.IsNullOrWhiteSpace(observation.ItemKey)
        && !string.IsNullOrWhiteSpace(observation.Label)
        && observation.Quantity > 0
        && double.IsFinite(observation.TimeSeconds)
        && observation.TimeSeconds >= 0
        && double.IsFinite(observation.CenterX)
        && double.IsFinite(observation.CenterY)
        && double.IsFinite(observation.Width)
        && double.IsFinite(observation.Height)
        && observation.Width > 0
        && observation.Height > 0
        && double.IsFinite(observation.Confidence)
        && observation.Confidence >= _minimumConfidence
        && string.IsNullOrWhiteSpace(observation.RejectionReason);

    private static double DistanceSquared(double leftX, double leftY, double rightX, double rightY)
    {
        var deltaX = leftX - rightX;
        var deltaY = leftY - rightY;
        return deltaX * deltaX + deltaY * deltaY;
    }

    private sealed class PickupTrack
    {
        private double _confidenceTotal;

        public PickupTrack(int trackId, MaterialPickupObservation observation)
        {
            TrackId = trackId;
            ItemKey = observation.ItemKey;
            Label = observation.Label;
            Quantity = observation.Quantity;
            FirstSeenSeconds = observation.TimeSeconds;
            LastSeenSeconds = observation.TimeSeconds;
            CenterX = observation.CenterX;
            CenterY = observation.CenterY;
            ObservationCount = 1;
            _confidenceTotal = observation.Confidence;
            RawText = observation.RawText;
        }

        public int TrackId { get; }
        public string ItemKey { get; }
        public string Label { get; }
        public long Quantity { get; }
        public double FirstSeenSeconds { get; }
        public double LastSeenSeconds { get; private set; }
        public double CenterX { get; private set; }
        public double CenterY { get; private set; }
        public int ObservationCount { get; private set; }
        public bool IsConfirmed { get; set; }
        public string RawText { get; private set; }
        public double EvidenceScore => _confidenceTotal / ObservationCount;

        public void Add(MaterialPickupObservation observation)
        {
            LastSeenSeconds = observation.TimeSeconds;
            CenterX = observation.CenterX;
            CenterY = observation.CenterY;
            ObservationCount++;
            _confidenceTotal += observation.Confidence;
            RawText = observation.RawText;
        }
    }
}
