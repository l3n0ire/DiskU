using System.Globalization;

namespace DiskU.Core;

/// <summary>Number formatting shared by every view, matching the Mac app's output.</summary>
public static class Format
{
    private static readonly string[] Units = ["bytes", "KB", "MB", "GB", "TB", "PB", "EB"];
    // Fraction digits per unit, as Apple's ByteCountFormatter (.file): bytes/KB 0, MB 1, GB and up 2.
    private static readonly int[] Digits = [0, 0, 1, 2, 2, 2, 2];

    /// <summary>
    /// Decimal (1 KB = 1000 bytes) adaptive-precision size, like Apple's ByteCountFormatter with
    /// countStyle .file: "0 bytes", "1 byte", "999 bytes", "1 KB", "20 KB", "44 MB", "996.1 MB", "13.17 GB".
    /// Trailing zero fractions are dropped. Uses the current culture's separators.
    /// </summary>
    public static string Bytes(long count) => Bytes(count, CultureInfo.CurrentCulture);

    public static string Bytes(long count, IFormatProvider culture)
    {
        if (count == 1) return "1 byte";
        if (count == -1) return "-1 byte";
        if (Math.Abs(count) < 1000) return count.ToString("#,##0", culture) + " bytes";

        double value = Math.Abs((double)count);
        int unit = 0;
        while (unit < Units.Length - 1 && value >= 1000)
        {
            value /= 1000;
            unit++;
        }
        double rounded = Math.Round(value, Digits[unit], MidpointRounding.AwayFromZero);
        // 999,999 bytes rounds to "1000 KB"; Apple shows the next unit ("1 MB").
        if (rounded >= 1000 && unit < Units.Length - 1)
        {
            unit++;
            value /= 1000;
            rounded = Math.Round(value, Digits[unit], MidpointRounding.AwayFromZero);
        }
        string pattern = Digits[unit] switch { 0 => "#,##0", 1 => "#,##0.#", _ => "#,##0.##" };
        string sign = count < 0 ? "-" : "";
        return sign + rounded.ToString(pattern, culture) + " " + Units[unit];
    }

    /// <summary>"1 file" or "&lt;n grouped&gt; files" (says "files" for folders' contents too, as on the Mac).</summary>
    public static string Items(int count) => Items(count, CultureInfo.CurrentCulture);

    public static string Items(int count, IFormatProvider culture) =>
        count == 1 ? "1 file" : count.ToString("N0", culture) + " files";

    /// <summary>Integer with the culture's grouping, e.g. "12,345".</summary>
    public static string Count(long count) => count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Share as a percent with exactly one fraction digit, e.g. "38.5%".</summary>
    public static string Percent(double fraction) => fraction.ToString("P1", CultureInfo.CurrentCulture);

    /// <summary>Date like the Mac's abbreviated date + shortened time, e.g. "Oct 14, 2026 at 3:05 PM" (en-US).</summary>
    public static string Date(DateTime local) =>
        local.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) + " at " + local.ToString("t", CultureInfo.CurrentCulture);
}
