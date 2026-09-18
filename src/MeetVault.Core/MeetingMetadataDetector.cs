using System.Text.RegularExpressions;

namespace MeetVault.Core;

/// <summary>
/// Determines the meeting date/time, and a human title, from the recording's file name,
/// falling back to the file's last-write time. Supports common naming patterns such as
/// "meet_xxxx_2026-09-17_10-30.mp4" or "Recording 2026-09-17 1030.mp4".
/// </summary>
public static partial class MeetingMetadataDetector
{
    [GeneratedRegex(@"(?:(?<y>20\d{2})[-_.](?<m>\d{2})[-_.](?<d>\d{2}))[\s_T-]*(?<hh>\d{2})?[-_.]?(?<mm>\d{2})?")]
    private static partial Regex DateTimePattern();

    [GeneratedRegex(@"^(?:meet|zoom|teams|rec|recording|untitled)[-_ ]*", RegexOptions.IgnoreCase)]
    private static partial Regex PrefixStripPattern();

    /// <summary>Detects date (always), start time (when present in the name), and a title.</summary>
    public static (DateOnly Date, TimeOnly? Time, string Title) Detect(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var lastWrite = File.GetLastWriteTime(filePath);

        DateOnly date = new(lastWrite.Year, lastWrite.Month, lastWrite.Day);
        TimeOnly? time = null;

        var m = DateTimePattern().Match(fileName);
        if (m.Success)
        {
            var y = int.Parse(m.Groups["y"].Value);
            var mo = int.Parse(m.Groups["m"].Value);
            var d = int.Parse(m.Groups["d"].Value);
            if (mo is >= 1 and <= 12 && d is >= 1 and <= 31)
            {
                date = new DateOnly(y, mo, d);
                var hh = m.Groups["hh"].Value;
                var mm = m.Groups["mm"].Value;
                if (hh.Length == 2 && mm.Length == 2)
                {
                    var h = int.Parse(hh);
                    var mi = int.Parse(mm);
                    if (h is <= 23 && mi is <= 59) time = new TimeOnly(h, mi);
                }
            }
        }

        // Title: strip recognized prefixes and any embedded date/time tokens.
        var title = PrefixStripPattern().Replace(fileName, "").Trim(new[] { '_', '-', ' ' });
        title = DateTimePattern().Replace(title, " ");
        title = Regex.Replace(title, @"\s{2,}", " ").Trim(new[] { '_', '-', ' ' });
        if (title.Length == 0)
        {
            title = date == DateOnly.FromDateTime(DateTime.Now) ? "Meeting" : $"Meeting {date:yyyy-MM-dd}";
        }

        return (date, time, title);
    }
}
