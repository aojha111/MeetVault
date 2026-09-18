namespace MeetVault.Core;

/// <summary>A node of the date-wise meeting tree: All Meetings → Year → Month/Day → Meetings.</summary>
public sealed class DateGroupNode
{
    public required string Label { get; set; }
    /// <summary>"year", "month-day" or "meeting".</summary>
    public required string Kind { get; set; }
    public DateOnly? Date { get; set; }
    public Meeting? Meeting { get; set; }
    public List<DateGroupNode> Children { get; set; } = [];

    public static DateGroupNode BuildTree(IEnumerable<Meeting> meetings)
    {
        var root = new DateGroupNode { Label = "All Meetings", Kind = "root" };
        foreach (var g in meetings
                     .GroupBy(m => m.MeetingDate.Year)
                     .OrderByDescending(g => g.Key))
        {
            var yearNode = new DateGroupNode { Label = g.Key.ToString(), Kind = "year" };
            foreach (var dayGroup in g
                         .GroupBy(m => m.MeetingDate)
                         .OrderByDescending(g => g.Key))
            {
                var dayLabel = dayGroup.Key.ToString("MMMM d");
                var dayNode = new DateGroupNode { Label = dayLabel, Kind = "month-day", Date = dayGroup.Key };
                foreach (var meeting in dayGroup.OrderByDescending(m => m.StartTime ?? TimeOnly.MinValue))
                {
                    dayNode.Children.Add(new DateGroupNode
                    {
                        Label = meeting.Title,
                        Kind = "meeting",
                        Date = dayGroup.Key,
                        Meeting = meeting,
                    });
                }
                yearNode.Children.Add(dayNode);
            }
            root.Children.Add(yearNode);
        }
        return root;
    }

    /// <summary>Filters meetings by a quick range: today, yesterday, this week, this month, or all.</summary>
    public static Func<Meeting, bool> RangeFilter(string range, DateOnly today)
    {
        return range switch
        {
            "today" => m => m.MeetingDate == today,
            "yesterday" => m => m.MeetingDate == today.AddDays(-1),
            "week" => m => m.MeetingDate >= today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday - (today.DayOfWeek == DayOfWeek.Sunday ? 7 : 0)) && m.MeetingDate <= today,
            "month" => m => m.MeetingDate.Year == today.Year && m.MeetingDate.Month == today.Month,
            _ => _ => true,
        };
    }
}
