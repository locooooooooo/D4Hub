namespace D4Hub.Core;

public readonly record struct TransmutationReminderState(
    bool IsVisible,
    bool VisibilityChanged,
    double Confidence,
    NormalizedRect SelectedRecipeBounds);

public sealed class TransmutationReminderStateMachine
{
    private readonly int _enterObservationCount;
    private readonly int _exitMissCount;
    private int _consecutiveEnterObservations;
    private int _consecutiveMisses;
    private bool _isVisible;
    private double _confidence;
    private NormalizedRect _stableBounds;

    public TransmutationReminderStateMachine(
        int enterObservationCount = 3,
        int exitMissCount = 2)
    {
        if (enterObservationCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(enterObservationCount));
        }

        if (exitMissCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(exitMissCount));
        }

        _enterObservationCount = enterObservationCount;
        _exitMissCount = exitMissCount;
    }

    public TransmutationReminderState Advance(TransmutationSceneDetection detection)
    {
        var hasBounds = detection.SelectedRecipeBounds.Width > 0
            && detection.SelectedRecipeBounds.Height > 0;
        var spatiallyConsistent = !_isVisible && _consecutiveEnterObservations == 0
            || IsSpatiallyConsistent(_stableBounds, detection.SelectedRecipeBounds);
        var enterEvidence = detection.IsTransmutationVisible && hasBounds && spatiallyConsistent;
        var holdEvidence = hasBounds
            && detection.ContextConfidence >= 0.64
            && IsSpatiallyConsistent(_stableBounds, detection.SelectedRecipeBounds);

        if (!_isVisible)
        {
            if (!enterEvidence)
            {
                _consecutiveEnterObservations = 0;
                _stableBounds = default;
                _confidence = 0;
                return Current(false);
            }

            _stableBounds = _consecutiveEnterObservations == 0
                ? detection.SelectedRecipeBounds
                : Blend(_stableBounds, detection.SelectedRecipeBounds, 0.5);
            _confidence = Math.Max(_confidence, detection.ContextConfidence);
            _consecutiveEnterObservations++;
            if (_consecutiveEnterObservations < _enterObservationCount)
            {
                return Current(false);
            }

            _isVisible = true;
            _consecutiveMisses = 0;
            return Current(true);
        }

        if (holdEvidence)
        {
            _consecutiveMisses = 0;
            _stableBounds = Blend(_stableBounds, detection.SelectedRecipeBounds, 0.22);
            _confidence = detection.ContextConfidence;
            return Current(false);
        }

        _consecutiveMisses++;
        if (_consecutiveMisses < _exitMissCount)
        {
            return Current(false);
        }

        return Reset();
    }

    public TransmutationReminderState Reset()
    {
        var changed = _isVisible;
        _isVisible = false;
        _consecutiveEnterObservations = 0;
        _consecutiveMisses = 0;
        _confidence = 0;
        _stableBounds = default;
        return Current(changed);
    }

    private TransmutationReminderState Current(bool changed) =>
        new(_isVisible, changed, _confidence, _stableBounds);

    private static bool IsSpatiallyConsistent(NormalizedRect current, NormalizedRect candidate)
    {
        if (current.Width <= 0 || current.Height <= 0)
        {
            return true;
        }

        var currentCenterX = current.X + current.Width / 2;
        var currentCenterY = current.Y + current.Height / 2;
        var candidateCenterX = candidate.X + candidate.Width / 2;
        var candidateCenterY = candidate.Y + candidate.Height / 2;
        return Math.Abs(currentCenterX - candidateCenterX) <= 0.035
            && Math.Abs(currentCenterY - candidateCenterY) <= 0.035
            && Math.Abs(current.Width - candidate.Width) <= 0.06
            && Math.Abs(current.Height - candidate.Height) <= 0.035;
    }

    private static NormalizedRect Blend(NormalizedRect current, NormalizedRect next, double nextWeight)
    {
        var currentWeight = 1 - nextWeight;
        return new NormalizedRect(
            current.X * currentWeight + next.X * nextWeight,
            current.Y * currentWeight + next.Y * nextWeight,
            current.Width * currentWeight + next.Width * nextWeight,
            current.Height * currentWeight + next.Height * nextWeight);
    }
}
