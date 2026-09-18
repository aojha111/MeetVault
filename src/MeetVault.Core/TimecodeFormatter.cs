namespace MeetVault.Core;

/// <summary>Formats millisecond offsets as <c>HH:MM:SS</c> or <c>MM:SS</c> timecodes.</summary>
public static class TimecodeFormatter
{
    /// <summary>Formats an offset in milliseconds as HH:MM:SS when ≥ 1h, otherwise MM:SS.</summary>
    public static string FormatMs(long milliseconds)
    {
        if (milliseconds < 0) milliseconds = 0;
        var total = TimeSpan.FromMilliseconds(milliseconds);
        int h = total.Hours, m = total.Minutes, s = total.Seconds;
        return h > 0
            ? $"{h:D2}:{m:D2}:{s:D2}"
            : $"{m:D2}:{s:D2}";
    }

    /// <summary>Formats an offset in milliseconds as a full HH:MM:SS string (hours always shown).</summary>
    public static string FormatMsLong(long milliseconds)
    {
        if (milliseconds < 0) milliseconds = 0;
        var total = TimeSpan.FromMilliseconds(milliseconds);
        return $"{total.Hours:D2}:{total.Minutes:D2}:{total.Seconds:D2}";
    }

    /// <summary>Parses HH:MM:SS(.mmm), MM:SS(.mmm) or SS(.mmm) into milliseconds; returns null when invalid.</summary>
    public static long? ParseToMs(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Trim().Split(':');
        if (parts.Length is < 1 or > 3) return null;

        double seconds = 0;
        foreach (var raw in parts)
        {
            if (!double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var part))
                return null;
            seconds = seconds * 60 + part;
        }
        return (long)Math.Round(seconds * 1000);
    }
}
