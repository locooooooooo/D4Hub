using System.Globalization;
using System.Text.RegularExpressions;

namespace D4Hub.Core;

public enum VisionDisplayMode
{
    StandardDynamicRange,
    HighDynamicRange
}

public enum VisionRegionKind
{
    CombatText,
    Experience,
    Currency,
    Materials,
    Buffs,
    Progress,
    Minimap
}

public sealed record VisionRegionDefinition(
    string Name,
    VisionRegionKind Kind,
    NormalizedRect Bounds);

public sealed class VisionCalibrationProfile
{
    public VisionCalibrationProfile(
        string id,
        int referenceWidth,
        int referenceHeight,
        string languageTag,
        VisionDisplayMode displayMode,
        byte brightnessThreshold,
        double minimumOcrConfidence,
        double templateSimilarityThreshold,
        IEnumerable<VisionRegionDefinition> regions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        ArgumentNullException.ThrowIfNull(regions);
        if (referenceWidth <= 0 || referenceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(referenceWidth));
        }

        if (!double.IsFinite(minimumOcrConfidence) || minimumOcrConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumOcrConfidence));
        }

        if (!double.IsFinite(templateSimilarityThreshold) || templateSimilarityThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(templateSimilarityThreshold));
        }

        var regionMap = new Dictionary<string, VisionRegionDefinition>(StringComparer.Ordinal);
        foreach (var region in regions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(region.Name);
            if (!double.IsFinite(region.Bounds.X)
                || !double.IsFinite(region.Bounds.Y)
                || !double.IsFinite(region.Bounds.Width)
                || !double.IsFinite(region.Bounds.Height))
            {
                throw new ArgumentOutOfRangeException(nameof(regions), $"Region '{region.Name}' contains a non-finite coordinate.");
            }

            var clamped = NormalizedRect.Clamp(region.Bounds);
            if (clamped != region.Bounds || clamped.Width <= 0 || clamped.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(regions), $"Region '{region.Name}' is outside the visible frame.");
            }

            if (!regionMap.TryAdd(region.Name, region))
            {
                throw new ArgumentException($"Duplicate vision region '{region.Name}'.", nameof(regions));
            }
        }

        Id = id.Trim();
        ReferenceWidth = referenceWidth;
        ReferenceHeight = referenceHeight;
        LanguageTag = languageTag.Trim();
        DisplayMode = displayMode;
        BrightnessThreshold = brightnessThreshold;
        MinimumOcrConfidence = minimumOcrConfidence;
        TemplateSimilarityThreshold = templateSimilarityThreshold;
        Regions = regionMap;
    }

    public string Id { get; }
    public int ReferenceWidth { get; }
    public int ReferenceHeight { get; }
    public string LanguageTag { get; }
    public VisionDisplayMode DisplayMode { get; }
    public byte BrightnessThreshold { get; }
    public double MinimumOcrConfidence { get; }
    public double TemplateSimilarityThreshold { get; }
    public IReadOnlyDictionary<string, VisionRegionDefinition> Regions { get; }
}

public static class VisionCalibrationCatalog
{
    public static VisionCalibrationProfile? SelectClosest(
        IEnumerable<VisionCalibrationProfile> profiles,
        int width,
        int height,
        string languageTag,
        VisionDisplayMode displayMode)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        return profiles
            .Where(profile => profile.DisplayMode == displayMode
                && string.Equals(profile.LanguageTag, languageTag, StringComparison.OrdinalIgnoreCase))
            .OrderBy(profile => Math.Abs(profile.ReferenceHeight - height))
            .ThenBy(profile => Math.Abs(profile.ReferenceWidth - width))
            .FirstOrDefault();
    }
}

public static class D4VisionCalibrationProfiles
{
    private static readonly IReadOnlyList<VisionCalibrationProfile> Profiles =
    [
        CreateChineseSdr("1080p-zhCN-sdr", 1920, 1080),
        CreateChineseSdr("1440p-zhCN-sdr", 2560, 1440)
    ];

    public static IReadOnlyList<VisionCalibrationProfile> All => Profiles;

    private static VisionCalibrationProfile CreateChineseSdr(string id, int width, int height) => new(
        id,
        width,
        height,
        "zh-CN",
        VisionDisplayMode.StandardDynamicRange,
        70,
        0.80,
        0.05,
        [
            new VisionRegionDefinition(
                "combat",
                VisionRegionKind.CombatText,
                new NormalizedRect(100d / 1920, 0, 1400d / 1920, 800d / 1080)),
            new VisionRegionDefinition(
                "material-pickups",
                VisionRegionKind.Materials,
                new NormalizedRect(80d / 1920, 560d / 1080, 1120d / 1920, 260d / 1080))
        ]);
}

public enum VisibleCounterKind
{
    Experience,
    Gold,
    Material
}

public enum VisibleCounterChangeKind
{
    Baseline,
    Gain,
    Outflow,
    Reset
}

public readonly record struct VisibleCounterObservation(
    string Key,
    string Label,
    VisibleCounterKind Kind,
    long Value,
    double TimeSeconds,
    double Confidence);

public sealed record VisibleCounterChange(
    string Key,
    VisibleCounterKind Kind,
    VisibleCounterChangeKind ChangeKind,
    long PreviousValue,
    long CurrentValue,
    long Amount,
    double TimeSeconds);

public sealed record VisibleCounterSummary(
    string Key,
    string Label,
    VisibleCounterKind Kind,
    long BaselineValue,
    long CurrentValue,
    long TotalGain,
    long TotalOutflow,
    int ResetCount,
    int AcceptedObservationCount,
    double LastObservedSeconds);

public sealed record VisibleCounterReport(
    int RejectedObservationCount,
    IReadOnlyList<VisibleCounterSummary> Counters,
    IReadOnlyList<VisibleCounterChange> Changes);

public sealed class VisibleCounterTracker
{
    private readonly double _minimumConfidence;
    private readonly Dictionary<string, CounterState> _states = new(StringComparer.Ordinal);
    private readonly List<VisibleCounterChange> _changes = new();
    private int _rejectedObservationCount;

    public VisibleCounterTracker(double minimumConfidence = 0.8)
    {
        if (!double.IsFinite(minimumConfidence) || minimumConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        }

        _minimumConfidence = minimumConfidence;
    }

    public void Add(VisibleCounterObservation observation)
    {
        if (string.IsNullOrWhiteSpace(observation.Key)
            || observation.Value < 0
            || !double.IsFinite(observation.TimeSeconds)
            || observation.TimeSeconds < 0
            || !double.IsFinite(observation.Confidence)
            || observation.Confidence < _minimumConfidence)
        {
            _rejectedObservationCount++;
            return;
        }

        if (!_states.TryGetValue(observation.Key, out var state))
        {
            state = new CounterState(observation);
            _states.Add(observation.Key, state);
            _changes.Add(new VisibleCounterChange(
                observation.Key,
                observation.Kind,
                VisibleCounterChangeKind.Baseline,
                observation.Value,
                observation.Value,
                0,
                observation.TimeSeconds));
            return;
        }

        if (state.Kind != observation.Kind || observation.TimeSeconds < state.LastObservedSeconds)
        {
            _rejectedObservationCount++;
            return;
        }

        var previous = state.CurrentValue;
        var delta = observation.Value - previous;
        var changeKind = VisibleCounterChangeKind.Baseline;
        var amount = 0L;
        if (delta > 0)
        {
            state.TotalGain = checked(state.TotalGain + delta);
            changeKind = VisibleCounterChangeKind.Gain;
            amount = delta;
        }
        else if (delta < 0 && observation.Kind == VisibleCounterKind.Experience)
        {
            state.ResetCount++;
            changeKind = VisibleCounterChangeKind.Reset;
            amount = checked(-delta);
        }
        else if (delta < 0)
        {
            amount = checked(-delta);
            state.TotalOutflow = checked(state.TotalOutflow + amount);
            changeKind = VisibleCounterChangeKind.Outflow;
        }

        state.Update(observation);
        if (delta != 0)
        {
            _changes.Add(new VisibleCounterChange(
                observation.Key,
                observation.Kind,
                changeKind,
                previous,
                observation.Value,
                amount,
                observation.TimeSeconds));
        }
    }

    public VisibleCounterReport BuildReport() => new(
        _rejectedObservationCount,
        _states.Values
            .OrderBy(state => state.Kind)
            .ThenBy(state => state.Key, StringComparer.Ordinal)
            .Select(state => state.ToSummary())
            .ToArray(),
        _changes.ToArray());

    private sealed class CounterState
    {
        public CounterState(VisibleCounterObservation observation)
        {
            Key = observation.Key;
            Label = observation.Label;
            Kind = observation.Kind;
            BaselineValue = CurrentValue = observation.Value;
            LastObservedSeconds = observation.TimeSeconds;
            AcceptedObservationCount = 1;
        }

        public string Key { get; }
        public string Label { get; private set; }
        public VisibleCounterKind Kind { get; }
        public long BaselineValue { get; }
        public long CurrentValue { get; private set; }
        public long TotalGain { get; set; }
        public long TotalOutflow { get; set; }
        public int ResetCount { get; set; }
        public int AcceptedObservationCount { get; private set; }
        public double LastObservedSeconds { get; private set; }

        public void Update(VisibleCounterObservation observation)
        {
            Label = string.IsNullOrWhiteSpace(observation.Label) ? Label : observation.Label;
            CurrentValue = observation.Value;
            LastObservedSeconds = observation.TimeSeconds;
            AcceptedObservationCount++;
        }

        public VisibleCounterSummary ToSummary() => new(
            Key,
            Label,
            Kind,
            BaselineValue,
            CurrentValue,
            TotalGain,
            TotalOutflow,
            ResetCount,
            AcceptedObservationCount,
            LastObservedSeconds);
    }
}

public readonly record struct VisibleProgressValue(double Current, double Target)
{
    public double Fraction => Target <= 0 ? 0 : Math.Clamp(Current / Target, 0, 1);
}

public static partial class VisibleProgressTextParser
{
    [GeneratedRegex(@"(?<current>\d[\d,，]*(?:[\.．]\d+)?)\s*/\s*(?<target>\d[\d,，]*(?:[\.．]\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex FractionRegex();

    [GeneratedRegex(@"(?<percent>\d+(?:[\.．]\d+)?)\s*%", RegexOptions.CultureInvariant)]
    private static partial Regex PercentRegex();

    public static bool TryParse(string? text, out VisibleProgressValue value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var fraction = FractionRegex().Match(text);
        if (fraction.Success
            && TryParseNumber(fraction.Groups["current"].Value, out var current)
            && TryParseNumber(fraction.Groups["target"].Value, out var target)
            && current >= 0
            && target > 0
            && current <= target)
        {
            value = new VisibleProgressValue(current, target);
            return true;
        }

        var percent = PercentRegex().Match(text);
        if (percent.Success
            && TryParseNumber(percent.Groups["percent"].Value, out var percentValue)
            && percentValue is >= 0 and <= 100)
        {
            value = new VisibleProgressValue(percentValue, 100);
            return true;
        }

        return false;
    }

    private static bool TryParseNumber(string text, out double value) => double.TryParse(
        text.Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("，", string.Empty, StringComparison.Ordinal)
            .Replace('．', '.'),
        NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture,
        out value);
}

public readonly record struct VisibleProgressObservation(
    string Key,
    string Label,
    VisibleProgressValue Value,
    double TimeSeconds,
    double Confidence);

public sealed record VisibleProgressSummary(
    string Key,
    string Label,
    VisibleProgressValue First,
    VisibleProgressValue Current,
    double TotalPositiveProgress,
    int ResetCount,
    int AcceptedObservationCount,
    double LastObservedSeconds);

public sealed record VisibleProgressReport(
    int RejectedObservationCount,
    IReadOnlyList<VisibleProgressSummary> Progress);

public sealed class VisibleProgressTracker
{
    private readonly double _minimumConfidence;
    private readonly Dictionary<string, ProgressState> _states = new(StringComparer.Ordinal);
    private int _rejectedObservationCount;

    public VisibleProgressTracker(double minimumConfidence = 0.8)
    {
        if (!double.IsFinite(minimumConfidence) || minimumConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        }

        _minimumConfidence = minimumConfidence;
    }

    public void Add(VisibleProgressObservation observation)
    {
        if (string.IsNullOrWhiteSpace(observation.Key)
            || observation.Value.Current < 0
            || observation.Value.Target <= 0
            || observation.Value.Current > observation.Value.Target
            || !double.IsFinite(observation.Value.Current)
            || !double.IsFinite(observation.Value.Target)
            || !double.IsFinite(observation.TimeSeconds)
            || observation.TimeSeconds < 0
            || !double.IsFinite(observation.Confidence)
            || observation.Confidence < _minimumConfidence)
        {
            _rejectedObservationCount++;
            return;
        }

        if (!_states.TryGetValue(observation.Key, out var state))
        {
            _states.Add(observation.Key, new ProgressState(observation));
            return;
        }

        if (observation.TimeSeconds < state.LastObservedSeconds)
        {
            _rejectedObservationCount++;
            return;
        }

        state.Update(observation);
    }

    public VisibleProgressReport BuildReport() => new(
        _rejectedObservationCount,
        _states.Values
            .OrderBy(state => state.Key, StringComparer.Ordinal)
            .Select(state => state.ToSummary())
            .ToArray());

    private sealed class ProgressState
    {
        public ProgressState(VisibleProgressObservation observation)
        {
            Key = observation.Key;
            Label = observation.Label;
            First = Current = observation.Value;
            LastObservedSeconds = observation.TimeSeconds;
            AcceptedObservationCount = 1;
        }

        public string Key { get; }
        public string Label { get; private set; }
        public VisibleProgressValue First { get; }
        public VisibleProgressValue Current { get; private set; }
        public double TotalPositiveProgress { get; private set; }
        public int ResetCount { get; private set; }
        public int AcceptedObservationCount { get; private set; }
        public double LastObservedSeconds { get; private set; }

        public void Update(VisibleProgressObservation observation)
        {
            var previousFraction = Current.Fraction;
            var nextFraction = observation.Value.Fraction;
            if (nextFraction < previousFraction)
            {
                ResetCount++;
            }
            else
            {
                TotalPositiveProgress += nextFraction - previousFraction;
            }

            Label = string.IsNullOrWhiteSpace(observation.Label) ? Label : observation.Label;
            Current = observation.Value;
            LastObservedSeconds = observation.TimeSeconds;
            AcceptedObservationCount++;
        }

        public VisibleProgressSummary ToSummary() => new(
            Key,
            Label,
            First,
            Current,
            TotalPositiveProgress,
            ResetCount,
            AcceptedObservationCount,
            LastObservedSeconds);
    }
}

public readonly record struct VisibleBuffObservation(
    string Key,
    string Label,
    int StackCount,
    double? RemainingSeconds,
    double Confidence);

public sealed record VisibleBuffSummary(
    string Key,
    string Label,
    double ActiveObservedSeconds,
    double ObservedSeconds,
    double? UptimeFraction,
    int MaximumStackCount,
    int LastStackCount,
    double? LastRemainingSeconds,
    int AcceptedObservationCount,
    double LastObservedSeconds,
    bool IsPresentInLatestFrame);

public sealed record VisibleBuffReport(
    int RejectedObservationCount,
    double ObservedSeconds,
    IReadOnlyList<VisibleBuffSummary> Buffs);

public sealed class VisibleBuffTracker
{
    private readonly double _minimumConfidence;
    private readonly double _maximumSampleGapSeconds;
    private readonly Dictionary<string, BuffState> _states = new(StringComparer.Ordinal);
    private HashSet<string> _previousFrameKeys = new(StringComparer.Ordinal);
    private double? _lastFrameTime;
    private double _observedSeconds;
    private int _rejectedObservationCount;

    public VisibleBuffTracker(double minimumConfidence = 0.8, double maximumSampleGapSeconds = 1)
    {
        if (!double.IsFinite(minimumConfidence) || minimumConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        }

        if (!double.IsFinite(maximumSampleGapSeconds) || maximumSampleGapSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSampleGapSeconds));
        }

        _minimumConfidence = minimumConfidence;
        _maximumSampleGapSeconds = maximumSampleGapSeconds;
    }

    public void AddFrame(double timeSeconds, IEnumerable<VisibleBuffObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds < _lastFrameTime)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        if (_lastFrameTime is { } previousTime)
        {
            var gap = timeSeconds - previousTime;
            if (gap <= _maximumSampleGapSeconds)
            {
                _observedSeconds += gap;
                foreach (var key in _previousFrameKeys)
                {
                    _states[key].ActiveObservedSeconds += gap;
                }
            }
        }

        var accepted = new Dictionary<string, VisibleBuffObservation>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            if (string.IsNullOrWhiteSpace(observation.Key)
                || observation.StackCount < 0
                || observation.RemainingSeconds is < 0
                || observation.RemainingSeconds is { } remainingSeconds && !double.IsFinite(remainingSeconds)
                || !double.IsFinite(observation.Confidence)
                || observation.Confidence < _minimumConfidence)
            {
                _rejectedObservationCount++;
                continue;
            }

            if (!accepted.TryGetValue(observation.Key, out var existing)
                || observation.Confidence > existing.Confidence)
            {
                accepted[observation.Key] = observation;
            }
        }

        foreach (var observation in accepted.Values)
        {
            if (!_states.TryGetValue(observation.Key, out var state))
            {
                state = new BuffState(observation.Key, observation.Label);
                _states.Add(observation.Key, state);
            }

            state.Observe(observation, timeSeconds);
        }

        _previousFrameKeys = accepted.Keys.ToHashSet(StringComparer.Ordinal);
        _lastFrameTime = timeSeconds;
    }

    public VisibleBuffReport BuildReport() => new(
        _rejectedObservationCount,
        _observedSeconds,
        _states.Values
            .OrderBy(state => state.Key, StringComparer.Ordinal)
            .Select(state => state.ToSummary(_observedSeconds, _previousFrameKeys.Contains(state.Key)))
            .ToArray());

    private sealed class BuffState
    {
        public BuffState(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public string Key { get; }
        public string Label { get; private set; }
        public double ActiveObservedSeconds { get; set; }
        public int MaximumStackCount { get; private set; }
        public int LastStackCount { get; private set; }
        public double? LastRemainingSeconds { get; private set; }
        public int AcceptedObservationCount { get; private set; }
        public double LastObservedSeconds { get; private set; }

        public void Observe(VisibleBuffObservation observation, double timeSeconds)
        {
            Label = string.IsNullOrWhiteSpace(observation.Label) ? Label : observation.Label;
            MaximumStackCount = Math.Max(MaximumStackCount, observation.StackCount);
            LastStackCount = observation.StackCount;
            LastRemainingSeconds = observation.RemainingSeconds;
            LastObservedSeconds = timeSeconds;
            AcceptedObservationCount++;
        }

        public VisibleBuffSummary ToSummary(double observedSeconds, bool present) => new(
            Key,
            Label,
            ActiveObservedSeconds,
            observedSeconds,
            observedSeconds <= 0 ? null : ActiveObservedSeconds / observedSeconds,
            MaximumStackCount,
            LastStackCount,
            LastRemainingSeconds,
            AcceptedObservationCount,
            LastObservedSeconds,
            present);
    }
}

public readonly record struct VisibleMapMarkerObservation(
    string Key,
    string MarkerKind,
    string Label,
    double X,
    double Y,
    double Confidence);

public sealed record VisibleMapMarker(
    string Key,
    string MarkerKind,
    string Label,
    double X,
    double Y,
    double LastObservedSeconds,
    int ObservationCount);

public sealed record VisibleMapReport(
    int RejectedObservationCount,
    IReadOnlyList<VisibleMapMarker> FreshMarkers);

public sealed class VisibleMapTracker
{
    private readonly double _minimumConfidence;
    private readonly double _markerLifetimeSeconds;
    private readonly Dictionary<string, MarkerState> _states = new(StringComparer.Ordinal);
    private int _rejectedObservationCount;
    private double _lastFrameTime;

    public VisibleMapTracker(double minimumConfidence = 0.8, double markerLifetimeSeconds = 2)
    {
        if (!double.IsFinite(minimumConfidence) || minimumConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        }

        if (!double.IsFinite(markerLifetimeSeconds) || markerLifetimeSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(markerLifetimeSeconds));
        }

        _minimumConfidence = minimumConfidence;
        _markerLifetimeSeconds = markerLifetimeSeconds;
    }

    public void AddFrame(double timeSeconds, IEnumerable<VisibleMapMarkerObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds < _lastFrameTime)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        _lastFrameTime = timeSeconds;
        foreach (var observation in observations)
        {
            if (string.IsNullOrWhiteSpace(observation.Key)
                || !double.IsFinite(observation.X)
                || !double.IsFinite(observation.Y)
                || observation.X is < 0 or > 1
                || observation.Y is < 0 or > 1
                || !double.IsFinite(observation.Confidence)
                || observation.Confidence < _minimumConfidence)
            {
                _rejectedObservationCount++;
                continue;
            }

            if (!_states.TryGetValue(observation.Key, out var state))
            {
                state = new MarkerState(observation, timeSeconds);
                _states.Add(observation.Key, state);
            }
            else
            {
                state.Update(observation, timeSeconds);
            }
        }
    }

    public VisibleMapReport BuildReport() => new(
        _rejectedObservationCount,
        _states.Values
            .Where(state => _lastFrameTime - state.LastObservedSeconds <= _markerLifetimeSeconds)
            .OrderBy(state => state.MarkerKind, StringComparer.Ordinal)
            .ThenBy(state => state.Key, StringComparer.Ordinal)
            .Select(state => state.ToMarker())
            .ToArray());

    private sealed class MarkerState
    {
        public MarkerState(VisibleMapMarkerObservation observation, double timeSeconds)
        {
            Key = observation.Key;
            MarkerKind = observation.MarkerKind;
            Label = observation.Label;
            Update(observation, timeSeconds);
        }

        public string Key { get; }
        public string MarkerKind { get; private set; }
        public string Label { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double LastObservedSeconds { get; private set; }
        public int ObservationCount { get; private set; }

        public void Update(VisibleMapMarkerObservation observation, double timeSeconds)
        {
            MarkerKind = observation.MarkerKind;
            Label = observation.Label;
            X = observation.X;
            Y = observation.Y;
            LastObservedSeconds = timeSeconds;
            ObservationCount++;
        }

        public VisibleMapMarker ToMarker() => new(
            Key,
            MarkerKind,
            Label,
            X,
            Y,
            LastObservedSeconds,
            ObservationCount);
    }
}

public enum LocalAutomationAction
{
    ShowNotification,
    ExportLocalSnapshot,
    AddSessionBookmark
}

public enum AutomationComparison
{
    AtLeast,
    AtMost
}

public sealed record LocalAutomationRule(
    string Id,
    string MetricKey,
    AutomationComparison Comparison,
    double Threshold,
    LocalAutomationAction Action,
    string Message);

public sealed record LocalAutomationEvent(
    string RuleId,
    LocalAutomationAction Action,
    string Message,
    string MetricKey,
    double MetricValue,
    double TimeSeconds);

public sealed class LocalAutomationRuleEngine
{
    private readonly IReadOnlyList<LocalAutomationRule> _rules;
    private readonly Dictionary<string, bool> _activeConditions = new(StringComparer.Ordinal);
    private double _lastEvaluationTime;

    public LocalAutomationRuleEngine(IEnumerable<LocalAutomationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var list = rules.ToArray();
        if (list.Any(rule => string.IsNullOrWhiteSpace(rule.Id)
                || string.IsNullOrWhiteSpace(rule.MetricKey)
                || !double.IsFinite(rule.Threshold)
                || !Enum.IsDefined(rule.Comparison)
                || !Enum.IsDefined(rule.Action))
            || list.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != list.Length)
        {
            throw new ArgumentException("Automation rules must have unique ids, known local actions, and finite thresholds.", nameof(rules));
        }

        _rules = list;
    }

    public IReadOnlyList<LocalAutomationEvent> Evaluate(
        double timeSeconds,
        IReadOnlyDictionary<string, double> metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds < _lastEvaluationTime)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        _lastEvaluationTime = timeSeconds;
        var events = new List<LocalAutomationEvent>();
        foreach (var rule in _rules)
        {
            var hasMetric = metrics.TryGetValue(rule.MetricKey, out var value) && double.IsFinite(value);
            var condition = hasMetric && (rule.Comparison == AutomationComparison.AtLeast
                ? value >= rule.Threshold
                : value <= rule.Threshold);
            var wasActive = _activeConditions.GetValueOrDefault(rule.Id);
            _activeConditions[rule.Id] = condition;
            if (condition && !wasActive)
            {
                events.Add(new LocalAutomationEvent(
                    rule.Id,
                    rule.Action,
                    rule.Message,
                    rule.MetricKey,
                    value,
                    timeSeconds));
            }
        }

        return events;
    }
}
