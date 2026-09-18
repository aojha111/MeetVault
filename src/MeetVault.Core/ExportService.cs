using System.Text;

namespace MeetVault.Core;

/// <summary>Exports meetings as Markdown minutes, JSON analysis or plain-text transcript.</summary>
public static class ExportService
{
    public static string ToMarkdown(Meeting meeting, MeetingAnalysis? analysis, IReadOnlyList<TranscriptSegment> transcript)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {meeting.Title}");
        sb.AppendLine();
        sb.AppendLine($"- **Date:** {meeting.MeetingDate:yyyy-MM-dd}");
        if (meeting.StartTime is { } st) sb.AppendLine($"- **Start time:** {st:HH:mm}");
        if (meeting.DurationSeconds > 0) sb.AppendLine($"- **Duration:** {TimeSpan.FromSeconds(meeting.DurationSeconds)}");
        sb.AppendLine();

        if (analysis is not null)
        {
            if (!string.IsNullOrWhiteSpace(analysis.Summary))
            {
                sb.AppendLine("## Summary");
                sb.AppendLine(analysis.Summary);
                sb.AppendLine();
            }
            if (analysis.Agenda.Count > 0)
            {
                sb.AppendLine("## Agenda");
                foreach (var a in analysis.Agenda) sb.AppendLine($"- {a}");
                sb.AppendLine();
            }
            if (analysis.KeyDiscussionPoints.Count > 0)
            {
                sb.AppendLine("## Key Discussion Points");
                foreach (var p in analysis.KeyDiscussionPoints) sb.AppendLine($"- {p}");
                sb.AppendLine();
            }
            if (analysis.Decisions.Count > 0)
            {
                sb.AppendLine("## Decisions");
                foreach (var d in analysis.Decisions) sb.AppendLine($"- {d.DecisionText}");
                sb.AppendLine();
            }
            if (analysis.ActionItems.Count > 0)
            {
                sb.AppendLine("## Action Items");
                foreach (var a in analysis.ActionItems)
                {
                    var owner = string.IsNullOrWhiteSpace(a.Owner) ? "" : $" — **{a.Owner}**";
                    var deadline = string.IsNullOrWhiteSpace(a.Deadline) ? "" : $" (due {a.Deadline})";
                    sb.AppendLine($"- [ ] {a.Task}{owner}{deadline}");
                }
                sb.AppendLine();
            }
            if (analysis.Risks.Count > 0)
            {
                sb.AppendLine("## Risks / Blockers");
                foreach (var r in analysis.Risks) sb.AppendLine($"- {r}");
                sb.AppendLine();
            }
            if (analysis.OpenQuestions.Count > 0)
            {
                sb.AppendLine("## Open Questions");
                foreach (var q in analysis.OpenQuestions) sb.AppendLine($"- {q.Question}");
                sb.AppendLine();
            }
            if (analysis.Topics.Count > 0)
            {
                sb.AppendLine("## Topics");
                sb.AppendLine(string.Join(", ", analysis.Topics));
                sb.AppendLine();
            }
            if (analysis.Participants.Count > 0)
            {
                sb.AppendLine("## Participants");
                sb.AppendLine(string.Join(", ", analysis.Participants));
                sb.AppendLine();
            }
        }

        if (transcript.Count > 0)
        {
            sb.AppendLine("## Transcript");
            sb.AppendLine();
            foreach (var seg in transcript)
            {
                var speaker = string.IsNullOrEmpty(seg.Speaker) ? "" : $" {seg.Speaker}:";
                sb.AppendLine($"**[{TimecodeFormatter.FormatMs(seg.StartMs)}]{speaker}** {seg.Text.Trim()}");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    public static string TranscriptToPlainText(IReadOnlyList<TranscriptSegment> transcript)
    {
        var sb = new StringBuilder();
        foreach (var seg in transcript)
        {
            var speaker = string.IsNullOrEmpty(seg.Speaker) ? "" : $" {seg.Speaker}:";
            sb.AppendLine($"[{TimecodeFormatter.FormatMs(seg.StartMs)}]{speaker} {seg.Text.Trim()}");
        }
        return sb.ToString();
    }
}
