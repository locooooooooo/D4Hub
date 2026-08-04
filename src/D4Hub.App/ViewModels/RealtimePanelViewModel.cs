using D4Hub.Core;

namespace D4Hub.App.ViewModels;

/// <summary>
/// P0 realtime statistics surface. It owns the user-facing controls while
/// the Core session remains responsible for aggregation and confidence rules.
/// </summary>
public sealed class RealtimePanelViewModel : ObservableObject
{
    private readonly RealtimeStatisticsSession _session;
    private readonly IRealtimeVisionAdapter _visionAdapter;
    private readonly RealtimeVisionCapabilities _capabilities;
    private readonly double _minimumOcrIntervalSeconds;
    private readonly string _languageTag;
    private readonly VisionDisplayMode _displayMode;
    private bool _isPanelVisible = true;
    private RealtimeStatisticsSnapshot _snapshot;
    private double _lastOcrScheduledSeconds = double.NegativeInfinity;
    private int _captureGeneration;
    private int _ocrInFlight;
    private int _scheduledFrameCount;
    private int _droppedBusyFrameCount;
    private int _droppedThrottledFrameCount;

    public RealtimePanelViewModel(
        IRealtimeVisionAdapter? visionAdapter = null,
        double minimumOcrIntervalSeconds = CombatDamageTracker.DefaultSamplingIntervalSeconds,
        string languageTag = "zh-CN",
        VisionDisplayMode displayMode = VisionDisplayMode.StandardDynamicRange)
    {
        if (!double.IsFinite(minimumOcrIntervalSeconds) || minimumOcrIntervalSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumOcrIntervalSeconds));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);

        _visionAdapter = visionAdapter ?? new NoopRealtimeVisionAdapter();
        _capabilities = _visionAdapter.Capabilities ?? RealtimeVisionCapabilities.None;
        _minimumOcrIntervalSeconds = minimumOcrIntervalSeconds;
        _languageTag = languageTag.Trim();
        _displayMode = displayMode;
        _session = new RealtimeStatisticsSession(
            damageSamplingIntervalSeconds: minimumOcrIntervalSeconds);
        _session.Start();
        _snapshot = _session.Snapshot;

        TogglePanelCommand = new RelayCommand(() => IsPanelVisible = !IsPanelVisible);
        ToggleCollectionCommand = new RelayCommand(ToggleCollection);
        ResetStatisticsCommand = new RelayCommand(ResetStatistics);
    }

    public bool IsPanelVisible
    {
        get => _isPanelVisible;
        set
        {
            if (SetProperty(ref _isPanelVisible, value))
            {
                OnPropertyChanged(nameof(PanelVisibilityText));
            }
        }
    }

    public RealtimeCaptureStatus Status => _snapshot.Status;
    public bool IsCollectionEnabled => _snapshot.IsEnabled;
    public bool HasData => _snapshot.HasData;
    public double Confidence => _snapshot.Confidence;
    public RealtimeVisionQuality DataQuality => _snapshot.DataQuality;
    public string StatusDetail => _snapshot.StatusDetail;
    public double? LastSampleSeconds => _snapshot.LastSampleSeconds;
    public long CurrentDps => _snapshot.CurrentDps;
    public long RecentOneSecondDamage => _snapshot.RecentOneSecondDamage;
    public long PeakOneSecondDamage => _snapshot.PeakOneSecondDamage;
    public long TotalDamage => _snapshot.TotalDamage;
    public long MaximumHit => _snapshot.MaximumHit;
    public long SessionAverageDps => _snapshot.SessionAverageDps;
    public bool IsSessionRateAvailable => _snapshot.IsSessionRateAvailable;
    public double TotalRunSeconds => _snapshot.TotalRunSeconds;
    public double TownSeconds => _snapshot.TownSeconds;
    public double OutOfTownSeconds => _snapshot.OutOfTownSeconds;
    public double UnknownSeconds => _snapshot.UnknownSeconds;
    public VisibleTownState TownState => _snapshot.TownState;
    public bool IsCombatActive => _snapshot.IsCombatActive;
    public double CombatActiveSeconds => _snapshot.CombatActiveSeconds;
    public double CombatInactiveSeconds => _snapshot.CombatInactiveSeconds;
    public int AcceptedDamageEvents => _snapshot.AcceptedDamageEvents;
    public int RejectedDamageObservations => _snapshot.RejectedDamageObservations;
    public bool IsOcrInFlight => Volatile.Read(ref _ocrInFlight) != 0;
    public int ScheduledFrameCount => Volatile.Read(ref _scheduledFrameCount);
    public int DroppedBusyFrameCount => Volatile.Read(ref _droppedBusyFrameCount);
    public int DroppedThrottledFrameCount => Volatile.Read(ref _droppedThrottledFrameCount);
    public bool SupportsDamage => _capabilities.Damage;
    public bool SupportsCounters => _capabilities.Counters;
    public bool SupportsProgress => _capabilities.Progress;
    public bool SupportsBuffs => _capabilities.Buffs;
    public bool SupportsMap => _capabilities.Map;
    public bool SupportsPickups => _capabilities.Pickups;
    public bool SupportsTownState => false;
    public bool IsAutomationEnabled => false;

    public string StatusText => IsOcrInFlight && Status == RealtimeCaptureStatus.NoData
        ? "正在识别"
        : Status switch
    {
        RealtimeCaptureStatus.Capturing => "实时采集",
        RealtimeCaptureStatus.Paused => "已暂停",
        RealtimeCaptureStatus.WaitingForGame => "等待游戏",
        RealtimeCaptureStatus.LowConfidence => "置信度不足",
        RealtimeCaptureStatus.InsufficientEvidence => "证据不足",
        RealtimeCaptureStatus.Error => "采集错误",
        _ => "暂无可信数据"
    };

    public string CollectionButtonText => IsCollectionEnabled ? "暂停采集" : "开始采集";
    public string PanelVisibilityText => IsPanelVisible ? "隐藏实时面板" : "显示实时面板";
    public string DpsText => FormatMetric(CurrentDps);
    public string RecentOneSecondDamageText => FormatMetric(RecentOneSecondDamage);
    public string PeakOneSecondDamageText => FormatMetric(PeakOneSecondDamage);
    public string TotalDamageText => FormatMetric(TotalDamage);
    public string MaximumHitText => FormatMetric(MaximumHit);
    public string SessionAverageDpsText => IsSessionRateAvailable ? FormatMetric(SessionAverageDps) : "暂无可信数据";
    public string TotalRunTimeText => FormatElapsed(TotalRunSeconds);
    public string TownTimeText => !SupportsTownState ? "不可用" : FormatElapsed(TownSeconds);
    public string CombatTimeText => FormatElapsed(CombatActiveSeconds);
    public string TownStateText => !SupportsTownState ? "不可用" : TownState switch
    {
        VisibleTownState.InTown => "城镇",
        VisibleTownState.OutOfTown => "野外",
        _ => "未知"
    };
    public string CombatStateText => IsCombatActive ? "战斗中" : "非战斗";
    public string DataQualityText => DataQuality.Level switch
    {
        RealtimeVisionQualityLevel.CalibratedVisualEstimate => "校准视觉",
        RealtimeVisionQualityLevel.ExperimentalVisualEstimate => "实验视觉",
        RealtimeVisionQualityLevel.BaselineScreenEstimate => "基线估算",
        RealtimeVisionQualityLevel.InsufficientEvidence => "低覆盖",
        _ => "不可用"
    };
    public string ConfidenceText => DataQualityText;
    public string ExperienceText => FormatCounterGain(VisibleCounterKind.Experience);
    public string GoldText => FormatCounterGain(VisibleCounterKind.Gold);
    public string MaterialsText => FormatCounterGain(VisibleCounterKind.Material);
    public string MaterialPickupTotalText => FormatMetric(MaterialPickups.ItemQuantity);
    public string GoldPickupTotalText => FormatMetric(MaterialPickups.CurrencyQuantity);
    public string MaterialPickupPerMinuteText => FormatMetric(MaterialPickupRates.ItemsPerMinute);
    public string MaterialPickupPerHourText => FormatMetric(MaterialPickupRates.ItemsPerHour);
    public string MaterialPickupStatusText => !SupportsPickups
        ? "不可用"
        : MaterialPickups.ConfirmedEventCount == 0
            ? "无可信数据/基线估算"
            : "基线估算 · 多帧确认";
    public string BuffStatusText => FormatBuffStatus();
    public string ProgressStatusText => FormatProgressStatus();
    public string MapStatusText => FormatMapStatus();
    public string AutomationStatusText => "已禁用";
    public string DataAvailabilityText => Status == RealtimeCaptureStatus.Error
        ? StatusDetail
        : IsOcrInFlight && !HasData
            ? "正在读取战斗文字"
            : HasData
                ? "已获得多帧确认的屏幕估算"
                : DataQuality.Detail;

    public VisibleCounterReport Counters => _snapshot.Counters;
    public VisibleProgressReport Progress => _snapshot.Progress;
    public VisibleBuffReport Buffs => _snapshot.Buffs;
    public VisibleMapReport Map => _snapshot.Map;
    public MaterialPickupReport MaterialPickups => _snapshot.MaterialPickups;
    public MaterialPickupRateSummary MaterialPickupRates =>
        MaterialPickups.CalculateRates(TotalRunSeconds);

    public RelayCommand TogglePanelCommand { get; }
    public RelayCommand ToggleCollectionCommand { get; }
    public RelayCommand ResetStatisticsCommand { get; }

    public void SetCollectionEnabled(bool enabled)
    {
        if (enabled == IsCollectionEnabled)
        {
            return;
        }

        InvalidatePendingReadout();
        if (enabled)
        {
            _session.Start();
            _lastOcrScheduledSeconds = double.NegativeInfinity;
        }
        else
        {
            _session.Pause();
        }

        Apply(_session.Snapshot);
    }

    public void MarkWaitingForGame(double timeSeconds)
    {
        InvalidatePendingReadout();
        Apply(_session.MarkWaitingForGame(timeSeconds));
    }

    public void InvalidatePendingReadout() => Interlocked.Increment(ref _captureGeneration);

    public bool CaptureFrame(PixelFrame frame, double timeSeconds, PixelRect? exclusion = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!IsCollectionEnabled)
        {
            return false;
        }

        var calibration = VisionCalibrationCatalog.SelectClosest(
            D4VisionCalibrationProfiles.All,
            frame.Width,
            frame.Height,
            _languageTag,
            _displayMode);
        if (calibration is null)
        {
            Apply(_session.AddFrame(
                timeSeconds,
                RealtimeVisionReadout.Empty with
                {
                    Quality = RealtimeVisionQuality.Unavailable(
                        $"No calibration supports {frame.Width}x{frame.Height}, {_languageTag}, {_displayMode}.")
                }));
            return false;
        }

        if (Volatile.Read(ref _ocrInFlight) != 0)
        {
            Interlocked.Increment(ref _droppedBusyFrameCount);
            OnPropertyChanged(nameof(DroppedBusyFrameCount));
            return false;
        }

        if (timeSeconds - _lastOcrScheduledSeconds < _minimumOcrIntervalSeconds)
        {
            Interlocked.Increment(ref _droppedThrottledFrameCount);
            OnPropertyChanged(nameof(DroppedThrottledFrameCount));
            return false;
        }

        if (Interlocked.CompareExchange(ref _ocrInFlight, 1, 0) != 0)
        {
            Interlocked.Increment(ref _droppedBusyFrameCount);
            OnPropertyChanged(nameof(DroppedBusyFrameCount));
            return false;
        }

        _lastOcrScheduledSeconds = timeSeconds;
        Interlocked.Increment(ref _scheduledFrameCount);
        OnPropertyChanged(nameof(IsOcrInFlight));
        OnPropertyChanged(nameof(ScheduledFrameCount));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DataAvailabilityText));
        var ocrFrame = exclusion is { } excludedRegion
            ? VisionRegionPixels.MaskBgra(frame, excludedRegion)
            : frame;
        var generation = Volatile.Read(ref _captureGeneration);
        _ = ProcessFrameAsync(ocrFrame, calibration, timeSeconds, generation);
        return true;
    }

    public void ApplyReadout(double timeSeconds, RealtimeVisionReadout readout) =>
        Apply(_session.AddFrame(timeSeconds, readout));

    private void ToggleCollection()
    {
        SetCollectionEnabled(!IsCollectionEnabled);
    }

    private void ResetStatistics()
    {
        InvalidatePendingReadout();
        _lastOcrScheduledSeconds = double.NegativeInfinity;
        _session.Reset();
        Apply(_session.Snapshot);
    }

    private async Task ProcessFrameAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        int generation)
    {
        try
        {
            var readout = await Task.Run(() =>
                _visionAdapter.ReadAsync(frame, calibration, timeSeconds));
            if (generation == Volatile.Read(ref _captureGeneration) && IsCollectionEnabled)
            {
                Apply(_session.AddFrame(timeSeconds, readout));
            }
        }
        catch (Exception exception)
        {
            if (generation == Volatile.Read(ref _captureGeneration) && IsCollectionEnabled)
            {
                ApplyError(exception.Message);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _ocrInFlight, 0);
            OnPropertyChanged(nameof(IsOcrInFlight));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(DataAvailabilityText));
        }
    }

    private void ApplyError(string detail)
    {
        _snapshot = _snapshot with
        {
            Status = RealtimeCaptureStatus.Error,
            StatusDetail = $"实时采集失败：{detail}",
            HasData = false,
            DataQuality = RealtimeVisionQuality.Unavailable($"Realtime capture failed: {detail}")
        };
        NotifySnapshotChanged();
    }

    private void Apply(RealtimeStatisticsSnapshot snapshot)
    {
        _snapshot = snapshot;
        NotifySnapshotChanged();
    }

    private void NotifySnapshotChanged()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsCollectionEnabled));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(Confidence));
        OnPropertyChanged(nameof(DataQuality));
        OnPropertyChanged(nameof(StatusDetail));
        OnPropertyChanged(nameof(LastSampleSeconds));
        OnPropertyChanged(nameof(CurrentDps));
        OnPropertyChanged(nameof(RecentOneSecondDamage));
        OnPropertyChanged(nameof(PeakOneSecondDamage));
        OnPropertyChanged(nameof(TotalDamage));
        OnPropertyChanged(nameof(MaximumHit));
        OnPropertyChanged(nameof(SessionAverageDps));
        OnPropertyChanged(nameof(IsSessionRateAvailable));
        OnPropertyChanged(nameof(TotalRunSeconds));
        OnPropertyChanged(nameof(TownSeconds));
        OnPropertyChanged(nameof(OutOfTownSeconds));
        OnPropertyChanged(nameof(UnknownSeconds));
        OnPropertyChanged(nameof(TownState));
        OnPropertyChanged(nameof(IsCombatActive));
        OnPropertyChanged(nameof(CombatActiveSeconds));
        OnPropertyChanged(nameof(CombatInactiveSeconds));
        OnPropertyChanged(nameof(AcceptedDamageEvents));
        OnPropertyChanged(nameof(RejectedDamageObservations));
        OnPropertyChanged(nameof(IsOcrInFlight));
        OnPropertyChanged(nameof(ScheduledFrameCount));
        OnPropertyChanged(nameof(DroppedBusyFrameCount));
        OnPropertyChanged(nameof(DroppedThrottledFrameCount));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CollectionButtonText));
        OnPropertyChanged(nameof(DpsText));
        OnPropertyChanged(nameof(RecentOneSecondDamageText));
        OnPropertyChanged(nameof(PeakOneSecondDamageText));
        OnPropertyChanged(nameof(TotalDamageText));
        OnPropertyChanged(nameof(MaximumHitText));
        OnPropertyChanged(nameof(SessionAverageDpsText));
        OnPropertyChanged(nameof(TotalRunTimeText));
        OnPropertyChanged(nameof(TownTimeText));
        OnPropertyChanged(nameof(CombatTimeText));
        OnPropertyChanged(nameof(TownStateText));
        OnPropertyChanged(nameof(CombatStateText));
        OnPropertyChanged(nameof(ConfidenceText));
        OnPropertyChanged(nameof(DataQualityText));
        OnPropertyChanged(nameof(ExperienceText));
        OnPropertyChanged(nameof(GoldText));
        OnPropertyChanged(nameof(MaterialsText));
        OnPropertyChanged(nameof(MaterialPickupTotalText));
        OnPropertyChanged(nameof(GoldPickupTotalText));
        OnPropertyChanged(nameof(MaterialPickupPerMinuteText));
        OnPropertyChanged(nameof(MaterialPickupPerHourText));
        OnPropertyChanged(nameof(MaterialPickupStatusText));
        OnPropertyChanged(nameof(BuffStatusText));
        OnPropertyChanged(nameof(ProgressStatusText));
        OnPropertyChanged(nameof(MapStatusText));
        OnPropertyChanged(nameof(AutomationStatusText));
        OnPropertyChanged(nameof(DataAvailabilityText));
        OnPropertyChanged(nameof(Counters));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(Buffs));
        OnPropertyChanged(nameof(Map));
    }

    private string FormatCounterGain(VisibleCounterKind kind)
    {
        var summaries = Counters.Counters.Where(counter => counter.Kind == kind).ToArray();
        if (summaries.Length == 0)
        {
            return SupportsCounters ? "--" : "不可用";
        }

        var totalGain = summaries.Aggregate(0L, (total, counter) => checked(total + counter.TotalGain));
        return totalGain == 0 ? "0" : DiabloNumberFormatter.Format(totalGain);
    }

    private string FormatBuffStatus()
    {
        var active = Buffs.Buffs.Where(buff => buff.IsPresentInLatestFrame).ToArray();
        if (active.Length == 0)
        {
            return SupportsBuffs ? "--" : "不可用";
        }

        return active.Length == 1
            ? $"{active[0].Label} x{active[0].LastStackCount}"
            : $"{active.Length} 个生效中";
    }

    private string FormatProgressStatus()
    {
        var progress = Progress.Progress.FirstOrDefault();
        if (progress is null)
        {
            return SupportsProgress ? "--" : "不可用";
        }

        return $"{progress.Label} {progress.Current.Fraction:P0}";
    }

    private string FormatMapStatus()
    {
        if (Map.FreshMarkers.Count == 0)
        {
            return SupportsMap ? "--" : "不可用";
        }

        return $"{Map.FreshMarkers.Count} 个可见标记";
    }

    private static string FormatMetric(long value) => value <= 0 ? "--" : DiabloNumberFormatter.Format(value);

    private static string FormatElapsed(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
        {
            return "暂无可信数据";
        }

        var total = (long)Math.Round(seconds);
        var hours = total / 3600;
        var minutes = (total % 3600) / 60;
        var remainingSeconds = total % 60;
        return hours > 0
            ? $"{hours} 小时 {minutes:00} 分 {remainingSeconds:00} 秒"
            : minutes > 0
                ? $"{minutes} 分 {remainingSeconds:00} 秒"
                : $"{remainingSeconds} 秒";
    }
}
