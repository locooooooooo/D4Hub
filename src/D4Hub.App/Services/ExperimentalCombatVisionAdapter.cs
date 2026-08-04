using D4Hub.Core;

namespace D4Hub.App.Services;

/// <summary>
/// Runs an explicitly selected experimental combat engine and permanently
/// falls back to Windows OCR after any initialization or inference failure.
/// </summary>
public sealed class ExperimentalCombatVisionAdapter : IRealtimeVisionAdapter, IDisposable
{
    private readonly IRealtimeVisionAdapter _baseline;
    private readonly Func<ICombatTextSpottingEngine> _engineFactory;
    private ICombatTextSpottingEngine? _engine;
    private string? _fallbackReason;

    public ExperimentalCombatVisionAdapter(
        IRealtimeVisionAdapter? baseline = null,
        Func<ICombatTextSpottingEngine>? engineFactory = null)
    {
        _baseline = baseline ?? new WindowsRealtimeOcrAdapter();
        _engineFactory = engineFactory ?? CreateDefaultEngine;
    }

    public RealtimeVisionCapabilities Capabilities => RealtimeVisionCapabilities.DamageOnly;
    public string ActivePipeline => _engine is not null ? "paddleocr-v5-experimental" : "windows-ocr-baseline";
    public string? FallbackReason => _fallbackReason;

    public async Task<RealtimeVisionReadout> ReadAsync(
        PixelFrame frame,
        VisionCalibrationProfile calibration,
        double timeSeconds,
        CancellationToken cancellationToken = default)
    {
        if (_fallbackReason is null && _engine is null)
        {
            try
            {
                _engine = _engineFactory();
            }
            catch (Exception exception)
            {
                DisableExperimentalEngine("initialization", exception);
            }
        }

        if (_engine is not null)
        {
            try
            {
                var result = await _engine.ReadAsync(frame, calibration, timeSeconds, cancellationToken);
                var evidence = result.Observations.Count == 0
                    ? 0
                    : result.Observations.Average(observation => observation.Confidence);
                return new RealtimeVisionReadout(
                    result.Observations,
                    Array.Empty<VisibleCounterObservation>(),
                    Array.Empty<VisibleProgressObservation>(),
                    Array.Empty<VisibleBuffObservation>(),
                    Array.Empty<VisibleMapMarkerObservation>(),
                    evidence,
                    new RealtimeVisionQuality(
                        RealtimeVisionQualityLevel.ExperimentalVisualEstimate,
                        "paddleocr-v5-experimental",
                        null,
                        _engine.Availability.Detail));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                DisableExperimentalEngine("inference", exception);
            }
        }

        var baseline = await _baseline.ReadAsync(frame, calibration, timeSeconds, cancellationToken);
        return baseline with
        {
            Quality = RealtimeVisionQuality.Baseline(
                $"PaddleOCR experimental pipeline was disabled; Windows OCR baseline is active. {_fallbackReason}")
        };
    }

    public void Dispose()
    {
        _engine?.Dispose();
        if (_baseline is IDisposable disposableBaseline)
        {
            disposableBaseline.Dispose();
        }
    }

    private static ICombatTextSpottingEngine CreateDefaultEngine()
    {
#if PADDLE_COMBAT_EXPERIMENT
        return new PaddleCombatTextSpottingEngine();
#else
        throw new NotSupportedException(
            "PaddleOCR is not included in this production build. Use an explicit experimental build.");
#endif
    }

    private void DisableExperimentalEngine(string stage, Exception exception)
    {
        _fallbackReason ??= $"{stage}: {exception.GetType().Name}: {exception.Message}";
        _engine?.Dispose();
        _engine = null;
    }
}
