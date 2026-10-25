using System.Globalization;

namespace FusionNet.Core.Utilities;

/// <summary>Culture-aware display helpers used across every page.</summary>
public static class Humanize
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Bytes(double bytes)
    {
        if (double.IsNaN(bytes) || double.IsInfinity(bytes) || bytes <= 0) return "0 B";
        var order = 0;
        var value = bytes;
        while (value >= 1024 && order < ByteUnits.Length - 1)
        {
            order++;
            value /= 1024;
        }

        // Metrics are data, not prose: always use Latin digits and the invariant "." separator so a
        // comma-decimal system locale cannot make the UI mix "12.3 ms" with "1,5 KB".
        return value.ToString(order == 0 ? "0" : "0.##", CultureInfo.InvariantCulture) + " " + ByteUnits[order];
    }

    public static string Bytes(ulong bytes) => Bytes((double)bytes);

    public static string Rate(double bytesPerSecond) => Bytes(Math.Max(0, bytesPerSecond)) + "/s";

    public static string Ms(double milliseconds, int decimals = 0) =>
        double.IsFinite(milliseconds)
            ? milliseconds.ToString("F" + Math.Clamp(decimals, 0, 2), CultureInfo.InvariantCulture) + " ms"
            : "—";

    public static string Ms(double? milliseconds, int decimals = 0) =>
        milliseconds.HasValue ? Ms(milliseconds.Value, decimals) : "—";

    public static string Percent(double value, int decimals = 0) =>
        double.IsFinite(value)
            ? value.ToString("F" + Math.Clamp(decimals, 0, 2), CultureInfo.InvariantCulture) + "%"
            : "—";

    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        return $"{span.Minutes:00}:{span.Seconds:00}";
    }

    public static string Speed(long bitsPerSecond) =>
        bitsPerSecond <= 0
            ? "—"
            : (bitsPerSecond / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + " Mbps";

    public static string Quality(double score) => score switch
    {
        >= 90 => "excellent",
        >= 75 => "good",
        >= 50 => "fair",
        > 0 => "poor",
        _ => "unknown"
    };

    public static string Clock(DateTimeOffset time) => time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static string Relative(DateTimeOffset time, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        var delta = reference - time;
        if (delta.TotalSeconds < 10) return "now";
        if (delta.TotalSeconds < 60) return $"{(int)delta.TotalSeconds}s ago";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
        return $"{(int)delta.TotalDays}d ago";
    }
}
