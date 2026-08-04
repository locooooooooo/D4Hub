namespace D4Hub.Core;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
}

public sealed record ExtractedPixelRegion(
    PixelRect SourceBounds,
    int Width,
    int Height,
    double SourcePixelsPerOutputPixelX,
    double SourcePixelsPerOutputPixelY,
    byte[] BgraPixels);

public static class VisionRegionPixels
{
    public static byte[] ApplyBrightnessThreshold(byte[] bgraPixels, byte threshold)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);
        if (bgraPixels.Length % 4 != 0)
        {
            throw new ArgumentException("BGRA pixel data must contain complete pixels.", nameof(bgraPixels));
        }

        var output = (byte[])bgraPixels.Clone();
        for (var offset = 0; offset < output.Length; offset += 4)
        {
            var luminance = ((output[offset + 2] * 54)
                + (output[offset + 1] * 183)
                + (output[offset] * 19)) >> 8;
            if (luminance < threshold)
            {
                output[offset] = 0;
                output[offset + 1] = 0;
                output[offset + 2] = 0;
            }

            output[offset + 3] = 255;
        }

        return output;
    }

    public static PixelFrame MaskBgra(PixelFrame frame, PixelRect region)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var left = Math.Clamp(region.X, 0, frame.Width);
        var top = Math.Clamp(region.Y, 0, frame.Height);
        var right = Math.Clamp((long)region.X + region.Width, 0, frame.Width);
        var bottom = Math.Clamp((long)region.Y + region.Height, 0, frame.Height);
        if (right <= left || bottom <= top)
        {
            return frame;
        }

        var pixels = (byte[])frame.Pixels.Clone();
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = ((y * frame.Width) + x) * 4;
                pixels[offset] = 0;
                pixels[offset + 1] = 0;
                pixels[offset + 2] = 0;
                pixels[offset + 3] = 255;
            }
        }

        return new PixelFrame(frame.Width, frame.Height, pixels);
    }

    public static PixelRect GetPixelBounds(PixelFrame frame, NormalizedRect region)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var clamped = NormalizedRect.Clamp(region);
        if (clamped.Width <= 0 || clamped.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(region));
        }

        var left = Math.Clamp((int)Math.Floor(clamped.X * frame.Width), 0, frame.Width - 1);
        var top = Math.Clamp((int)Math.Floor(clamped.Y * frame.Height), 0, frame.Height - 1);
        var right = Math.Clamp(
            (int)Math.Ceiling((clamped.X + clamped.Width) * frame.Width),
            left + 1,
            frame.Width);
        var bottom = Math.Clamp(
            (int)Math.Ceiling((clamped.Y + clamped.Height) * frame.Height),
            top + 1,
            frame.Height);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    public static ExtractedPixelRegion ExtractBgra(
        PixelFrame frame,
        NormalizedRect region,
        int maximumDimension = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (maximumDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        }

        var bounds = GetPixelBounds(frame, region);
        var scale = Math.Min(
            1,
            Math.Min(maximumDimension / (double)bounds.Width, maximumDimension / (double)bounds.Height));
        var outputWidth = Math.Max(1, (int)Math.Floor(bounds.Width * scale));
        var outputHeight = Math.Max(1, (int)Math.Floor(bounds.Height * scale));
        var sourcePerOutputX = bounds.Width / (double)outputWidth;
        var sourcePerOutputY = bounds.Height / (double)outputHeight;
        var output = new byte[checked(outputWidth * outputHeight * 4)];
        for (var outputY = 0; outputY < outputHeight; outputY++)
        {
            var sourceY = bounds.Y + Math.Min(
                bounds.Height - 1,
                (int)Math.Floor((outputY + 0.5) * sourcePerOutputY));
            for (var outputX = 0; outputX < outputWidth; outputX++)
            {
                var sourceX = bounds.X + Math.Min(
                    bounds.Width - 1,
                    (int)Math.Floor((outputX + 0.5) * sourcePerOutputX));
                var sourceOffset = ((sourceY * frame.Width) + sourceX) * 4;
                var outputOffset = ((outputY * outputWidth) + outputX) * 4;
                output[outputOffset] = frame.Pixels[sourceOffset];
                output[outputOffset + 1] = frame.Pixels[sourceOffset + 1];
                output[outputOffset + 2] = frame.Pixels[sourceOffset + 2];
                output[outputOffset + 3] = frame.Pixels[sourceOffset + 3];
            }
        }

        return new ExtractedPixelRegion(
            bounds,
            outputWidth,
            outputHeight,
            sourcePerOutputX,
            sourcePerOutputY,
            output);
    }
}

public readonly record struct CombatOcrWord(
    string Text,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record CombatOcrLine(IReadOnlyList<CombatOcrWord> Words);

public readonly record struct DamageCandidateAssessment(
    double EvidenceScore,
    string? RejectionReason);

public static class CombatOcrObservationMapper
{
    public static IReadOnlyList<CombatTextObservation> ReadDamageObservations(
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

        var observations = new List<CombatTextObservation>();
        foreach (var line in lines)
        {
            var text = string.Concat(line.Words.Select(word => word.Text));
            var matches = CombatDamageTextParser.ParseMatches(text);
            foreach (var match in matches)
            {
                var matchEnd = match.Start + match.Length;
                var characterOffset = 0;
                var matchedWords = new List<CombatOcrWord>();
                foreach (var word in line.Words)
                {
                    var wordEnd = characterOffset + word.Text.Length;
                    if (characterOffset < matchEnd && wordEnd > match.Start)
                    {
                        matchedWords.Add(word);
                    }

                    characterOffset = wordEnd;
                }

                if (matchedWords.Count == 0)
                {
                    continue;
                }

                var left = matchedWords.Min(word => word.X);
                var top = matchedWords.Min(word => word.Y);
                var right = matchedWords.Max(word => word.X + word.Width);
                var bottom = matchedWords.Max(word => word.Y + word.Height);
                var assessment = AssessDamageCandidate(match, matches.Count, matchedWords.Count);
                observations.Add(new CombatTextObservation(
                    match.Damage,
                    timeSeconds,
                    sourceOffsetX + ((left + right) / 2 * sourceScaleX),
                    sourceOffsetY + ((top + bottom) / 2 * sourceScaleY),
                    (right - left) * sourceScaleX,
                    (bottom - top) * sourceScaleY,
                    match.RawText,
                    assessment.EvidenceScore,
                    assessment.RejectionReason));
            }
        }

        return observations;
    }

    public static DamageCandidateAssessment AssessDamageCandidate(
        CombatParsedDamage match,
        int matchesOnLine,
        int matchedWordCount)
    {
        var rawNumberText = match.RawText[..^1].Trim();
        var hasGroupSeparator = rawNumberText.Contains(',') || rawNumberText.Contains('，');
        var numberText = rawNumberText
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("，", string.Empty, StringComparison.Ordinal)
            .Trim();
        var hasDecimal = numberText.Contains('.') || numberText.Contains('．');
        var digitCount = numberText.Count(char.IsDigit);
        var confidence = hasDecimal
            ? 0.90
            : hasGroupSeparator
                ? 0.85
            : digitCount <= 3
                ? 0.80
                : digitCount == 4
                    ? 0.60
                    : 0.35;
        if (matchesOnLine > 1)
        {
            confidence -= 0.10;
        }

        if (matchedWordCount > 2)
        {
            confidence -= 0.05;
        }

        confidence = Math.Clamp(confidence, 0, 1);
        if (hasDecimal && hasGroupSeparator)
        {
            return new DamageCandidateAssessment(confidence, "mixed-grouped-decimal-risk");
        }

        if (hasDecimal && digitCount > 4)
        {
            return new DamageCandidateAssessment(confidence, "implausible-mantissa-shape");
        }

        if (hasGroupSeparator && !HasValidDigitGrouping(rawNumberText))
        {
            return new DamageCandidateAssessment(confidence, "invalid-digit-grouping");
        }

        // Windows OCR does not expose a character posterior. Compact D4
        // values such as 3.58亿 and 17.0万 become catastrophic 358亿/170万
        // errors when the decimal point disappears, so ambiguous ungrouped
        // three-plus digit integers fail closed in the baseline pipeline.
        return digitCount >= 3 && !hasDecimal && !hasGroupSeparator
            ? new DamageCandidateAssessment(confidence, "missing-decimal-risk")
            : new DamageCandidateAssessment(confidence, null);
    }

    private static bool HasValidDigitGrouping(string rawNumberText)
    {
        var normalized = rawNumberText.Replace('，', ',');
        var groups = normalized.Split(',');
        return groups.Length >= 2
            && groups[0].Length is >= 1 and <= 3
            && groups.Select((group, index) => (group, index)).All(item =>
                item.group.All(char.IsDigit)
                && (item.index == 0 || item.group.Length == 3));
    }
}
