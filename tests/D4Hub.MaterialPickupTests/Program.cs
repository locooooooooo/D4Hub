using D4Hub.Core;

Assert(MaterialPickupTextParser.Parse("+180 希望微块") is
{
    IsAccepted: true,
    Label: "希望微块",
    Quantity: 180,
    Confidence: >= 0.9
}, "parses material pickup");

Assert(MaterialPickupTextParser.Parse("3,548 金币") is
{
    IsAccepted: true,
    Label: "金币",
    Quantity: 3548
}, "parses currency pickup without plus marker");

Assert(!MaterialPickupTextParser.Parse("获得压制").IsAccepted, "rejects non-pickup status text");
Assert(!MaterialPickupTextParser.Parse("2.89亿").IsAccepted, "rejects combat damage text");

var mapped = MaterialPickupObservationMapper.Read(
    [new CombatOcrLine([
        new CombatOcrWord("+180", 10, 20, 30, 10),
        new CombatOcrWord("希望微块", 45, 20, 60, 10)
    ])],
    1,
    sourceOffsetX: 100,
    sourceOffsetY: 200,
    sourceScaleX: 2,
    sourceScaleY: 3);
Assert(mapped.Count == 1 && mapped[0].CenterX == 200 && mapped[0].CenterY == 275,
    "maps pickup coordinates back to source pixels");

var tracker = new MaterialPickupTracker();
tracker.AddFrame(0, [Observation("+180 希望微块", 0)]);
tracker.AddFrame(0.6, [Observation("+180 希望微块", 0.6)]);
tracker.AddFrame(1.2, [Observation("+180 希望微块", 1.2)]);
tracker.AddFrame(3, [Observation("+180 希望微块", 3)]);
tracker.AddFrame(3.6, [Observation("+180 希望微块", 3.6)]);
var report = tracker.BuildReport();
Assert(report.ConfirmedEventCount == 2, "confirms two separated pickup events");
Assert(report.TotalQuantity == 360 && report.DuplicateObservationCount == 1,
    "deduplicates a lingering pickup prompt");
Assert(report.PendingObservationCount == 0, "does not leave confirmed tracks pending");
var rate = report.CalculateRates(60);
Assert(rate.ItemQuantity == 360 && rate.ItemsPerMinute == 360 && rate.ItemsPerHour == 21_600,
    "calculates item rates from effective session time");

var lowConfidence = Observation("+180 希望微块", 5, confidence: 0.4);
var rejectedTracker = new MaterialPickupTracker();
rejectedTracker.AddFrame(5, [lowConfidence]);
Assert(rejectedTracker.BuildReport().RejectedObservationCount == 1,
    "rejects below-threshold pickup observations");

Console.WriteLine("PASS material pickup parser, mapper, tracker, deduplication, and rates");

static MaterialPickupObservation Observation(string text, double timeSeconds, double x = 400, double y = 700, double confidence = 0.92)
{
    var parsed = MaterialPickupTextParser.Parse(text);
    return new MaterialPickupObservation(
        parsed.ItemKey,
        parsed.Label,
        parsed.Quantity,
        timeSeconds,
        x,
        y,
        120,
        24,
        text,
        confidence,
        parsed.RejectionReason);
}

static void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL {name}");
    }

    Console.WriteLine($"PASS {name}");
}
