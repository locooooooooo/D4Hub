using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using D4Hub.App.Services;
using D4Hub.Core;

const double DefaultPanelThreshold = 0.55;
const double MinimumPanelThreshold = 0.35;
const double MaximumPanelThreshold = 0.95;
const int PanelRejectedExitCode = 4;
const int CaptureFailedExitCode = 5;
const int TransmutationRejectedExitCode = 6;

if (args.Length == 1 && string.Equals(args[0], "--list-window-titles", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(JsonSerializer.Serialize(
        new GameWindowLocator().ListVisibleWindows()
            .OrderByDescending(window => (long)window.Width * window.Height)
            .Select(window => new
            {
                window.Title,
                window.Left,
                window.Top,
                window.Width,
                window.Height,
                window.IsForeground,
                window.IsMinimized
            }),
        new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

if (args.Length == 3
    && (string.Equals(args[0], "--capture-title", StringComparison.OrdinalIgnoreCase)
        || string.Equals(args[0], "--capture-screen-title", StringComparison.OrdinalIgnoreCase)))
{
    var target = new GameWindowLocator().FindWindowByTitle(args[1]);
    if (target is null)
    {
        Console.Error.WriteLine($"Window not found: {args[1]}");
        return 3;
    }

    try
    {
        var frames = new ScreenFrameService();
        var useScreenCopy = string.Equals(args[0], "--capture-screen-title", StringComparison.OrdinalIgnoreCase);
        var capturedFrame = useScreenCopy
            ? frames.Capture(target.Value)
            : frames.CaptureWindow(target.Value);
        var receipt = SaveVerifiedCapture(
            frames,
            capturedFrame,
            useScreenCopy ? "client-rect-screen-copy" : "print-window-client-only",
            args[2]);
        Console.WriteLine(JsonSerializer.Serialize(receipt, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Client-surface capture failed: {exception.Message}");
        return CaptureFailedExitCode;
    }
}

if (args.Length == 2 && string.Equals(args[0], "--transmutation", StringComparison.OrdinalIgnoreCase))
{
    if (!File.Exists(args[1]))
    {
        Console.Error.WriteLine($"Screenshot not found: {args[1]}");
        return 2;
    }

    var screenshot = Path.GetFullPath(args[1]);
    var transmutationFrame = new ScreenFrameService().Load(screenshot);
    var detection = new TransmutationSceneDetector().Detect(transmutationFrame);
    var transmutationAccepted = detection.IsTransmutationVisible;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        mode = "transmutation-reminder",
        image = new { width = transmutationFrame.Width, height = transmutationFrame.Height },
        context = new
        {
            detected = detection.IsTransmutationVisible,
            confidence = detection.ContextConfidence,
            selectedRecipeBounds = detection.SelectedRecipeBounds
        },
        decision = new
        {
            status = transmutationAccepted ? "show-reminder" : "hide-reminder",
            accepted = transmutationAccepted,
            reason = transmutationAccepted
                ? "selected-transmutation-recipe-detected"
                : "selected-transmutation-recipe-below-threshold"
        }
    }, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    }));
    return transmutationAccepted ? 0 : TransmutationRejectedExitCode;
}

if (!TryParseScreenshotArguments(args, out var screenshotPath, out var panelThreshold, out var argumentError))
{
    Console.Error.WriteLine(argumentError);
    WriteUsage();
    return 2;
}

var frame = new ScreenFrameService().Load(screenshotPath);
var panel = new CharacterPanelDetector().Detect(frame);
var fingerprintService = new BuildFingerprintService();
var skillBar = BuildFingerprintService.GetSkillBarBounds(panel.Bounds);
var accepted = panel.Confidence >= panelThreshold;
var fingerprint = accepted ? fingerprintService.Capture(frame, panel) : null;

Console.WriteLine(JsonSerializer.Serialize(new
{
    image = new { width = frame.Width, height = frame.Height },
    panel = new
    {
        x = panel.Bounds.X,
        y = panel.Bounds.Y,
        width = panel.Bounds.Width,
        height = panel.Bounds.Height,
        confidence = panel.Confidence,
        threshold = panelThreshold
    },
    decision = new
    {
        status = accepted ? "accepted" : "rejected",
        accepted,
        reason = accepted
            ? "panel-confidence-meets-threshold"
            : "panel-confidence-below-threshold"
    },
    skillBar,
    fingerprint
}, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
}));

return accepted ? 0 : PanelRejectedExitCode;

static CaptureReceipt SaveVerifiedCapture(
    ScreenFrameService frames,
    PixelFrame capturedFrame,
    string captureMethod,
    string outputPath)
{
    var fullOutputPath = Path.GetFullPath(outputPath);
    frames.SavePng(capturedFrame, fullOutputPath);

    var saved = ReadSavedCapture(frames, fullOutputPath);
    if (saved.Width != capturedFrame.Width || saved.Height != capturedFrame.Height)
    {
        throw new InvalidOperationException("The saved PNG dimensions do not match the captured client surface.");
    }

    var receipt = new CaptureReceipt(
        SchemaVersion: 1,
        Mode: "client-surface-capture",
        CaptureSpace: "client-device-pixels",
        CaptureMethod: captureMethod,
        Width: saved.Width,
        Height: saved.Height,
        ByteLength: saved.ByteLength,
        Sha256: saved.Sha256,
        OutputPath: fullOutputPath);

    var confirmation = ReadSavedCapture(frames, fullOutputPath);
    if (confirmation.Width != receipt.Width
        || confirmation.Height != receipt.Height
        || confirmation.ByteLength != receipt.ByteLength
        || !string.Equals(confirmation.Sha256, receipt.Sha256, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The saved PNG changed while its capture receipt was being verified.");
    }

    return receipt;
}

static SavedCaptureEvidence ReadSavedCapture(ScreenFrameService frames, string path)
{
    var savedFrame = frames.Load(path);
    using var stream = File.OpenRead(path);
    var byteLength = stream.Length;
    if (byteLength <= 0)
    {
        throw new InvalidOperationException("The saved PNG is empty.");
    }

    var sha256 = Convert.ToHexString(SHA256.HashData(stream));
    return new SavedCaptureEvidence(savedFrame.Width, savedFrame.Height, byteLength, sha256);
}

static bool TryParseScreenshotArguments(
    string[] arguments,
    out string screenshotPath,
    out double panelThreshold,
    out string error)
{
    screenshotPath = string.Empty;
    panelThreshold = DefaultPanelThreshold;
    error = string.Empty;

    if (arguments.Length == 1)
    {
        screenshotPath = arguments[0];
    }
    else if (arguments.Length == 3
        && string.Equals(arguments[1], "--panel-threshold", StringComparison.OrdinalIgnoreCase))
    {
        screenshotPath = arguments[0];
        if (!double.TryParse(arguments[2], NumberStyles.Float, CultureInfo.InvariantCulture, out panelThreshold)
            || !double.IsFinite(panelThreshold)
            || panelThreshold is < MinimumPanelThreshold or > MaximumPanelThreshold)
        {
            error = $"Panel threshold must be a finite number from {MinimumPanelThreshold:F2} through {MaximumPanelThreshold:F2}.";
            return false;
        }
    }
    else
    {
        error = "Invalid arguments.";
        return false;
    }

    if (!File.Exists(screenshotPath))
    {
        error = $"Screenshot not found: {screenshotPath}";
        return false;
    }

    screenshotPath = Path.GetFullPath(screenshotPath);
    return true;
}

static void WriteUsage()
{
    Console.Error.WriteLine("Usage: D4Hub.VisionProbe <screenshot-path> [--panel-threshold <0.35-0.95>]");
    Console.Error.WriteLine("   or: D4Hub.VisionProbe --transmutation <screenshot-path>");
    Console.Error.WriteLine("   or: D4Hub.VisionProbe --list-window-titles");
    Console.Error.WriteLine("   or: D4Hub.VisionProbe --capture-title <window-title> <output.png>");
    Console.Error.WriteLine("   or: D4Hub.VisionProbe --capture-screen-title <window-title> <output.png>");
}

internal sealed record CaptureReceipt(
    int SchemaVersion,
    string Mode,
    string CaptureSpace,
    string CaptureMethod,
    int Width,
    int Height,
    long ByteLength,
    string Sha256,
    string OutputPath);

internal readonly record struct SavedCaptureEvidence(
    int Width,
    int Height,
    long ByteLength,
    string Sha256);
