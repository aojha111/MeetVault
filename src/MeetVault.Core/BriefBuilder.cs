namespace MeetVault.Core;

public sealed partial class MeetingProcessor
{
    /// <summary>Spoken brief script via the local LLM, with a deterministic fallback.</summary>
    private async Task<string> GenerateBriefScriptAsync(Meeting meeting, MeetingAnalysis analysis, CancellationToken ct)
    {
        Report(meeting.Id, "Generating meeting brief", -1, "Writing brief script…", ProcessingStatus.Processing);
        var script = string.Empty;
        try
        {
            var raw = await _llm.CompleteAsync(
                AnalysisPrompts.BriefSystemPrompt,
                AnalysisPrompts.BriefUserPrompt(analysis, meeting.MeetingDate, _settings.BriefDurationSeconds),
                AnalysisSchemas.Brief,
                temperature: 0.3,
                maxTokens: 700,
                cancellationToken: ct).ConfigureAwait(false);
            var parsed = JsonUtil.ParseLenient<BriefScript>(raw);
            script = parsed?.Script?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _log($"[{meeting.Id}] LLM brief generation failed, using deterministic fallback: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(script))
            script = BriefBuilder.BuildDeterministic(analysis, meeting.MeetingDate);
        return script;
    }
}

/// <summary>Deterministic brief script builder (used when the LLM is unavailable or fails).</summary>
public static class BriefBuilder
{
    /// <summary>Builds a crisp point-to-point spoken brief from the analysis without an LLM.</summary>
    public static string BuildDeterministic(MeetingAnalysis analysis, DateOnly date)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Meeting brief for ");
        sb.Append(date.ToString("MMMM d, yyyy"));
        sb.Append(". ");
        if (!string.IsNullOrWhiteSpace(analysis.Summary))
        {
            sb.Append(analysis.Summary.Trim());
            sb.Append(' ');
        }
        if (analysis.Agenda.Count > 0)
        {
            sb.Append("Agenda: ");
            sb.Append(string.Join("; ", analysis.Agenda.Take(5).Select(a => a.Trim().TrimEnd('.'))));
            sb.Append(". ");
        }
        if (analysis.Decisions.Count > 0)
        {
            sb.Append("Decisions: ");
            sb.Append(string.Join("; ", analysis.Decisions.Take(5).Select(d => d.DecisionText.Trim().TrimEnd('.'))));
            sb.Append(". ");
        }
        var owned = analysis.ActionItems.Where(a => !string.IsNullOrWhiteSpace(a.Owner)).Take(5).ToList();
        if (owned.Count > 0)
        {
            sb.Append("Action items: ");
            sb.Append(string.Join("; ", owned.Select(a =>
            {
                var text = $"{a.Task.Trim().TrimEnd('.')}, owned by {a.Owner!.Trim()}";
                if (!string.IsNullOrWhiteSpace(a.Deadline)) text += $", due {a.Deadline!.Trim()}";
                return text;
            })));
            sb.Append(". ");
        }
        if (analysis.Risks.Count > 0)
        {
            sb.Append("Open risk: ");
            sb.Append(analysis.Risks[0].Trim().TrimEnd('.'));
            sb.Append(". ");
        }
        if (analysis.OpenQuestions.Count > 0)
        {
            sb.Append("Open question: ");
            sb.Append(analysis.OpenQuestions[0].Question.Trim().TrimEnd('.'));
            sb.Append('.');
        }
        return sb.ToString();
    }
}
