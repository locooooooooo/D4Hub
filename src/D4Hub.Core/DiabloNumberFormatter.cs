using System.Globalization;

namespace D4Hub.Core;

public static class DiabloNumberFormatter
{
    private static readonly (long Threshold, string Suffix)[] Units =
    [
        (10_000_000_000_000_000L, "京"),
        (1_000_000_000_000L, "兆"),
        (100_000_000L, "亿"),
        (10_000L, "万")
    ];

    public static string Format(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        foreach (var (threshold, suffix) in Units)
        {
            if (value < threshold)
            {
                continue;
            }

            var scaled = value / (decimal)threshold;
            var decimalPlaces = scaled < 10 ? 2 : scaled < 100 ? 1 : 0;
            return scaled.ToString($"N{decimalPlaces}", CultureInfo.InvariantCulture) + suffix;
        }

        return value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
