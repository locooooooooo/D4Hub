namespace D4Hub.Core;

public readonly record struct TransmutationSceneDetection(
    bool IsTransmutationVisible,
    double ContextConfidence,
    NormalizedRect SelectedRecipeBounds);

public sealed class TransmutationSceneDetector
{
    public TransmutationSceneDetection Detect(PixelFrame frame)
    {
        var recipe = FindSelectedRecipe(frame);
        if (recipe is null)
        {
            return new TransmutationSceneDetection(
                false,
                0,
                default);
        }

        return new TransmutationSceneDetection(
            recipe.Value.Confidence >= 0.82,
            recipe.Value.Confidence,
            recipe.Value.Bounds);
    }

    private static Candidate? FindSelectedRecipe(PixelFrame frame)
    {
        var lines = new List<RedLine>();
        for (var y = 0; y < frame.Height; y += 2)
        {
            RedLine? bestLine = null;
            var segmentStart = -1;
            var lastRed = -1;
            var redCount = 0;
            for (var x = frame.Width / 2; x < frame.Width; x += 2)
            {
                if (IsCrimson(frame, x, y))
                {
                    if (segmentStart < 0 || x - lastRed > 16)
                    {
                        bestLine = SelectBetterLine(bestLine, CreateLine(y, segmentStart, lastRed, redCount));
                        segmentStart = x;
                        redCount = 0;
                    }

                    lastRed = x;
                    redCount++;
                    continue;
                }
            }

            bestLine = SelectBetterLine(bestLine, CreateLine(y, segmentStart, lastRed, redCount));
            if (bestLine is not { } line)
            {
                continue;
            }

            if (line.Right - line.Left + 2 >= frame.Width * 0.13 && line.Density >= 0.28)
            {
                lines.Add(line);
            }
        }

        Candidate? best = null;
        foreach (var group in GroupLines(lines))
        {
            var bounds = new NormalizedRect(
                group.Left / (double)frame.Width,
                group.Top / (double)frame.Height,
                (group.Right - group.Left + 2d) / frame.Width,
                (group.Bottom - group.Top + 2d) / frame.Height);
            var confidence = ScoreRecipeBounds(bounds, group.Density, group.LineCount);
            if (confidence < 0.45 || (best is not null && confidence <= best.Value.Confidence))
            {
                continue;
            }

            best = new Candidate(NormalizedRect.Clamp(bounds), confidence);
        }

        return best;
    }

    private static IEnumerable<RedGroup> GroupLines(IReadOnlyList<RedLine> lines)
    {
        if (lines.Count == 0)
        {
            yield break;
        }

        var group = new RedGroup(lines[0]);
        for (var index = 1; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Y <= group.Bottom + 5 && line.Left <= group.Right && line.Right >= group.Left)
            {
                group.Add(line);
                continue;
            }

            yield return group;
            group = new RedGroup(line);
        }

        yield return group;
    }

    private static double ScoreRecipeBounds(NormalizedRect bounds, double density, int lineCount)
    {
        var widthScore = RangeScore(bounds.Width, 0.13, 0.38);
        var heightScore = RangeScore(bounds.Height, 0.025, 0.12);
        var positionScore = bounds.X >= 0.55 && bounds.X <= 0.80 && bounds.Y >= 0.12 && bounds.Y <= 0.82
            ? 1
            : 0;
        var rowScore = Math.Clamp(lineCount / 14d, 0, 1);
        return density * 0.34 + widthScore * 0.18 + heightScore * 0.18 + positionScore * 0.18 + rowScore * 0.12;
    }

    private static double RangeScore(double value, double minimum, double maximum)
    {
        if (value < minimum || value > maximum)
        {
            return 0;
        }

        var midpoint = (minimum + maximum) / 2;
        var halfRange = (maximum - minimum) / 2;
        return 1 - Math.Abs(value - midpoint) / halfRange;
    }

    private static bool IsCrimson(PixelFrame frame, int x, int y)
    {
        var (blue, green, red) = GetColor(frame, x, y);
        return red >= 115 && red - green >= 42 && red - blue >= 48;
    }

    private static RedLine? CreateLine(int y, int start, int end, int count)
    {
        if (start < 0 || end < start || count == 0)
        {
            return null;
        }

        var span = end - start + 2;
        return new RedLine(y, start, end, count * 2d / span);
    }

    private static RedLine? SelectBetterLine(RedLine? current, RedLine? candidate)
    {
        if (candidate is null)
        {
            return current;
        }

        if (current is null)
        {
            return candidate;
        }

        var currentScore = (current.Value.Right - current.Value.Left + 2) * current.Value.Density;
        var candidateScore = (candidate.Value.Right - candidate.Value.Left + 2) * candidate.Value.Density;
        return candidateScore > currentScore ? candidate : current;
    }

    private static (byte Blue, byte Green, byte Red) GetColor(PixelFrame frame, int x, int y)
    {
        x = Math.Clamp(x, 0, frame.Width - 1);
        y = Math.Clamp(y, 0, frame.Height - 1);
        var offset = (y * frame.Width + x) * 4;
        return (frame.Pixels[offset], frame.Pixels[offset + 1], frame.Pixels[offset + 2]);
    }

    private readonly record struct Candidate(NormalizedRect Bounds, double Confidence);

    private readonly record struct RedLine(int Y, int Left, int Right, double Density);

    private sealed class RedGroup
    {
        private double _densityTotal;

        public RedGroup(RedLine line)
        {
            Top = Bottom = line.Y;
            Left = line.Left;
            Right = line.Right;
            _densityTotal = line.Density;
            LineCount = 1;
        }

        public int Top { get; }
        public int Bottom { get; private set; }
        public int Left { get; private set; }
        public int Right { get; private set; }
        public int LineCount { get; private set; }
        public double Density => _densityTotal / LineCount;

        public void Add(RedLine line)
        {
            Bottom = line.Y;
            Left = Math.Min(Left, line.Left);
            Right = Math.Max(Right, line.Right);
            _densityTotal += line.Density;
            LineCount++;
        }
    }
}
