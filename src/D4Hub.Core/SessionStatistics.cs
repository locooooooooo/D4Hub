namespace D4Hub.Core;

/// <summary>
/// Town presence state. Unknown must be preserved whenever the evidence is
/// insufficient; Unknown must never be silently treated as town or non-town.
/// </summary>
public enum VisibleTownState
{
    Unknown,
    InTown,
    OutOfTown
}

/// <summary>
/// Mutually exclusive damage classification. Unknown preserves the damage
/// total without fabricating a category. The documented priority for
/// resolving conflicts is: OverpowerCritical, DamageOverTime, Critical,
/// Overpower, Normal, Unknown.
/// </summary>
public enum DamageClassification
{
    Unknown,
    Normal,
    DamageOverTime,
    Critical,
    Overpower,
    OverpowerCritical
}

/// <summary>
/// The session clock distinguishes running time from paused, waiting-for-game,
/// focus-lost and error intervals. Only Running intervals count as effective
/// run time; every state transition produces a receipt so the time ranges can
/// be replayed and verified.
/// </summary>
public enum SessionClockState
{
    Initial,
    Running,
    Paused,
    WaitingForGame,
    FocusLost,
    Error
}

public sealed record SessionClockTransition(
    SessionClockState From,
    SessionClockState To,
    double TimeSeconds);

public sealed record SessionClockSnapshot(
    SessionClockState State,
    double EffectiveSeconds,
    double LastTransitionSeconds);

/// <summary>
/// Stateful clock for one statistics session. Time advances only while the
/// clock is Running. Transitions are rejected when they are not allowed or
/// arrive out of order; rejected transitions are not treated as time.
/// </summary>
public sealed class SessionClock
{
    private SessionClockState _state = SessionClockState.Initial;
    private double _lastTransitionSeconds;
    private double _effectiveSeconds;
    private readonly List<SessionClockTransition> _transitions = new();
    private int _rejectedTransitionCount;

    public SessionClock()
    {
        _lastTransitionSeconds = double.NegativeInfinity;
    }

    public SessionClockSnapshot Snapshot => new(
        _state,
        _effectiveSeconds,
        _lastTransitionSeconds);

    public SessionClockState State => _state;

    public IReadOnlyList<SessionClockTransition> Transitions => _transitions;
    public int RejectedTransitionCount => _rejectedTransitionCount;

    public void Start(double timeSeconds)
    {
        ValidateTime(timeSeconds);
        TransitionTo(SessionClockState.Running, timeSeconds);
    }

    public void Pause(double timeSeconds)
    {
        ValidateTime(timeSeconds);
        if (_state == SessionClockState.Running)
        {
            Accumulate(timeSeconds);
        }

        TransitionTo(SessionClockState.Paused, timeSeconds);
    }

    public void MarkWaitingForGame(double timeSeconds)
    {
        ValidateTime(timeSeconds);
        if (_state == SessionClockState.Running)
        {
            Accumulate(timeSeconds);
        }

        TransitionTo(SessionClockState.WaitingForGame, timeSeconds);
    }

    public void MarkFocusLost(double timeSeconds)
    {
        ValidateTime(timeSeconds);
        if (_state == SessionClockState.Running)
        {
            Accumulate(timeSeconds);
        }

        TransitionTo(SessionClockState.FocusLost, timeSeconds);
    }

    public void MarkError(double timeSeconds)
    {
        ValidateTime(timeSeconds);
        if (_state == SessionClockState.Running)
        {
            Accumulate(timeSeconds);
        }

        TransitionTo(SessionClockState.Error, timeSeconds);
    }

    /// <summary>
    /// Advances the effective time while the clock is running. Called on every
    /// captured frame so the effective run time reflects the live session
    /// instead of only the next transition. Repeated ticks at the same time
    /// never add time twice.
    /// </summary>
    public void Tick(double timeSeconds)
    {
        ValidateTime(timeSeconds);
        if (_state != SessionClockState.Running || timeSeconds < _lastTransitionSeconds)
        {
            return;
        }

        Accumulate(timeSeconds);
        _lastTransitionSeconds = timeSeconds;
    }

    public void Reset(double timeSeconds)
    {
        ValidateTime(timeSeconds);
        if (_state == SessionClockState.Running)
        {
            Accumulate(timeSeconds);
        }

        _effectiveSeconds = 0;
        _transitions.Clear();
        _lastTransitionSeconds = timeSeconds;
        _state = SessionClockState.Initial;
    }

    private void Accumulate(double timeSeconds)
    {
        _effectiveSeconds += Math.Max(0, timeSeconds - _lastTransitionSeconds);
    }

    private void TransitionTo(SessionClockState target, double timeSeconds)
    {
        if (_state == target
            || (timeSeconds < _lastTransitionSeconds && _state != SessionClockState.Initial))
        {
            _rejectedTransitionCount++;
            return;
        }

        var from = _state;
        _state = target;
        _lastTransitionSeconds = timeSeconds;
        _transitions.Add(new SessionClockTransition(from, target, timeSeconds));
    }

    private void ValidateTime(double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }
    }
}

/// <summary>
/// Confirmed town-presence observation candidate. Only observations that
/// passed the caller's confidence gate may be fed into the tracker; the
/// tracker applies its own multi-frame hysteresis before changing state.
/// </summary>
public readonly record struct TownCandidateObservation(
    VisibleTownState Candidate,
    double TimeSeconds,
    double Confidence);

public sealed record TownStateTransition(
    VisibleTownState From,
    VisibleTownState To,
    double TimeSeconds,
    int ConfirmedFrameCount);

public sealed record TownStateSnapshot(
    VisibleTownState State,
    double TownSeconds,
    double OutOfTownSeconds,
    double UnknownSeconds,
    int ConfirmedFrameCount,
    IReadOnlyList<TownStateTransition> Transitions,
    int RejectedCandidateCount);

/// <summary>
/// Three-state town tracker with multi-frame hysteresis. A candidate must be
/// observed for <c>confirmFrames</c> consecutive frames before the state
/// changes; until then the previous state (or Unknown) is preserved. Only
/// the confirmed state accrues time.
/// </summary>
public sealed class TownStateTracker
{
    public const int DefaultConfirmFrames = 3;

    private readonly int _confirmFrames;
    private readonly double _minimumConfidence;
    private VisibleTownState _state = VisibleTownState.Unknown;
    private double _stateSinceSeconds;
    private double _townSeconds;
    private double _outOfTownSeconds;
    private double _unknownSeconds;
    private double? _lastFrameSeconds;
    private VisibleTownState _pendingCandidate;
    private int _pendingCount;
    private int _rejectedCandidateCount;
    private readonly List<TownStateTransition> _transitions = new();

    public TownStateTracker(
        int confirmFrames = DefaultConfirmFrames,
        double minimumConfidence = 0.8)
    {
        if (confirmFrames < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmFrames));
        }

        if (!double.IsFinite(minimumConfidence) || minimumConfidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        }

        _confirmFrames = confirmFrames;
        _minimumConfidence = minimumConfidence;
        _pendingCandidate = VisibleTownState.Unknown;
    }

    public TownStateSnapshot Snapshot => new(
        _state,
        _townSeconds,
        _outOfTownSeconds,
        _unknownSeconds,
        _pendingCount,
        _transitions.ToArray(),
        _rejectedCandidateCount);

    public void AddFrame(double timeSeconds, IEnumerable<TownCandidateObservation> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds < (_lastFrameSeconds ?? double.NegativeInfinity))
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        Accumulate(timeSeconds);

        var best = candidates
            .Where(candidate => double.IsFinite(candidate.TimeSeconds)
                && candidate.TimeSeconds <= timeSeconds
                && double.IsFinite(candidate.Confidence)
                && candidate.Confidence >= _minimumConfidence)
            .OrderByDescending(candidate => candidate.Confidence)
            .FirstOrDefault();

        if (candidates.Any() && best.Candidate == VisibleTownState.Unknown)
        {
            _rejectedCandidateCount++;
        }

        if (best.Candidate == VisibleTownState.Unknown)
        {
            // No trusted candidate this frame: preserve the pending and
            // confirmed state; the gap does not break the hysteresis window.
            return;
        }

        if (best.Candidate != _pendingCandidate)
        {
            _pendingCandidate = best.Candidate;
            _pendingCount = 1;
        }
        else
        {
            _pendingCount++;
        }

        if (_pendingCount < _confirmFrames || _pendingCandidate == _state)
        {
            return;
        }

        TransitionTo(_pendingCandidate, timeSeconds);
    }

    public void Reset(double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        Accumulate(timeSeconds);
        _state = VisibleTownState.Unknown;
        _stateSinceSeconds = timeSeconds;
        _townSeconds = 0;
        _outOfTownSeconds = 0;
        _unknownSeconds = 0;
        _pendingCandidate = VisibleTownState.Unknown;
        _pendingCount = 0;
        _transitions.Clear();
    }

    private void Accumulate(double timeSeconds)
    {
        if (_lastFrameSeconds is { } previous)
        {
            var gap = Math.Max(0, timeSeconds - previous);
            switch (_state)
            {
                case VisibleTownState.InTown:
                    _townSeconds += gap;
                    break;
                case VisibleTownState.OutOfTown:
                    _outOfTownSeconds += gap;
                    break;
                default:
                    _unknownSeconds += gap;
                    break;
            }
        }

        _lastFrameSeconds = timeSeconds;
    }

    private void TransitionTo(VisibleTownState target, double timeSeconds)
    {
        var from = _state;
        _state = target;
        _stateSinceSeconds = timeSeconds;
        _pendingCount = 0;
        _transitions.Add(new TownStateTransition(from, target, timeSeconds, _confirmFrames));
    }
}

/// <summary>
/// Candidate combat-activity rule: the segment is active while a confirmed
/// damage event was seen within the window, with enter/exit hysteresis so a
/// brief silence between packs does not flap the state.
/// </summary>
public sealed record CombatActivitySnapshot(
    bool IsActive,
    double ActiveSeconds,
    double InactiveSeconds,
    int EnterCount,
    int ExitCount);

public sealed class CombatActivityTracker
{
    public const double DefaultActiveWindowSeconds = 4;
    public const double DefaultExitHysteresisSeconds = 1.5;

    private readonly double _activeWindowSeconds;
    private readonly double _exitHysteresisSeconds;
    private bool _isActive;
    private double _stateSinceSeconds;
    private double _activeSeconds;
    private double _inactiveSeconds;
    private double _lastEventSeconds = double.NegativeInfinity;
    private double? _lastFrameSeconds;
    private int _enterCount;
    private int _exitCount;

    public CombatActivityTracker(
        double activeWindowSeconds = DefaultActiveWindowSeconds,
        double exitHysteresisSeconds = DefaultExitHysteresisSeconds)
    {
        if (!double.IsFinite(activeWindowSeconds) || activeWindowSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activeWindowSeconds));
        }

        if (!double.IsFinite(exitHysteresisSeconds) || exitHysteresisSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exitHysteresisSeconds));
        }

        _activeWindowSeconds = activeWindowSeconds;
        _exitHysteresisSeconds = exitHysteresisSeconds;
    }

    public CombatActivitySnapshot Snapshot => new(
        _isActive,
        _activeSeconds,
        _inactiveSeconds,
        _enterCount,
        _exitCount);

    public void AddDamageEvent(double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        _lastEventSeconds = timeSeconds;
        if (!_isActive)
        {
            _isActive = true;
            _stateSinceSeconds = timeSeconds;
            _enterCount++;
        }
    }

    public void AddFrame(double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds < (_lastFrameSeconds ?? double.NegativeInfinity))
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        Accumulate(timeSeconds);
        _lastFrameSeconds = timeSeconds;

        if (!_isActive || !double.IsFinite(_lastEventSeconds))
        {
            return;
        }

        var sinceLastEvent = timeSeconds - _lastEventSeconds;
        if (sinceLastEvent > _activeWindowSeconds + _exitHysteresisSeconds)
        {
            _isActive = false;
            _stateSinceSeconds = timeSeconds;
            _exitCount++;
        }
    }

    public void Reset(double timeSeconds)
    {
        if (!double.IsFinite(timeSeconds) || timeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSeconds));
        }

        Accumulate(timeSeconds);
        _isActive = false;
        _stateSinceSeconds = timeSeconds;
        _lastEventSeconds = double.NegativeInfinity;
        _enterCount = 0;
        _exitCount = 0;
    }

    private void Accumulate(double timeSeconds)
    {
        if (_lastFrameSeconds is { } previous)
        {
            var gap = Math.Max(0, timeSeconds - previous);
            if (_isActive)
            {
                _activeSeconds += gap;
            }
            else
            {
                _inactiveSeconds += gap;
            }
        }
    }
}

/// <summary>
/// Session-wide damage aggregation that is independent from the current
/// damage segment. The denominator is the effective run time supplied by the
/// clock; a zero or unknown denominator keeps the rate unavailable.
/// </summary>
public sealed record SessionDamageSnapshot(
    long TotalDamage,
    long MaximumHit,
    double EffectiveSeconds,
    long SessionAverageDps,
    bool IsRateAvailable);

/// <summary>
/// Aggregates confirmed damage events for the whole session and keeps the
/// maximum single confirmed hit. All inputs must already be confirmed events;
/// this type never re-validates or re-confirms them. Event ids make the
/// aggregator idempotent: re-feeding the same event never double counts.
/// </summary>
public sealed class SessionDamageAggregator
{
    private readonly HashSet<int> _processedIds = new();
    private long _totalDamage;
    private long _maximumHit;
    private int _eventCount;

    public int EventCount => _eventCount;

    public void AddConfirmedEvent(int id, long damage)
    {
        if (id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        if (damage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(damage));
        }

        if (!_processedIds.Add(id))
        {
            return;
        }

        _totalDamage = checked(_totalDamage + damage);
        _maximumHit = Math.Max(_maximumHit, damage);
        _eventCount++;
    }

    public void Reset()
    {
        _processedIds.Clear();
        _totalDamage = 0;
        _maximumHit = 0;
        _eventCount = 0;
    }

    public SessionDamageSnapshot BuildSnapshot(double effectiveSeconds)
    {
        if (!double.IsFinite(effectiveSeconds) || effectiveSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveSeconds));
        }

        var rateAvailable = _totalDamage > 0 && effectiveSeconds > 0;
        return new SessionDamageSnapshot(
            _totalDamage,
            _maximumHit,
            effectiveSeconds,
            rateAvailable ? (long)Math.Round(_totalDamage / effectiveSeconds) : 0,
            rateAvailable);
    }
}
