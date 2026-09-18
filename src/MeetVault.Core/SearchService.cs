using System.Text.RegularExpressions;

namespace MeetVault.Core;

/// <summary>
/// Deterministic search across meeting knowledge (title, transcript, summary, decisions,
/// actions, questions, topics, people). Semantic/synthesis questions can be answered by the
/// local LLM separately; this service is fully deterministic and works without models.
/// </summary>
public sealed partial class SearchService
{
    private readonly ISearchRepository _search;
    private readonly IMeetingRepository _meetings;

    public SearchService(ISearchRepository search, IMeetingRepository meetings)
    {
        _search = search;
        _meetings = meetings;
    }

    [GeneratedRegex(@"^(?:what|why|how|who|when|summar\w*|tell me about)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionPattern();

    public static bool LooksLikeQuestion(string query) => QuestionPattern().IsMatch(query.Trim());

    /// <summary>Runs a deterministic search. Empty/short queries return recent meetings.</summary>
    public async Task<SearchResponse> SearchAsync(string query, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var response = new SearchResponse { Query = query, IsQuestion = LooksLikeQuestion(query) };
        if (string.IsNullOrWhiteSpace(query))
        {
            response.Hits = [];
            return response;
        }

        var hits = await _search.SearchAsync(query.Trim(), from, to, ct).ConfigureAwait(false);
        response.Hits = hits.ToList();

        // Group hits per meeting for display.
        response.GroupedByMeeting = response.Hits
            .GroupBy(h => h.MeetingId)
            .Select(g => new SearchMeetingGroup
            {
                MeetingId = g.Key,
                Title = g.First().Title,
                MeetingDate = g.First().MeetingDate,
                Hits = g.ToList(),
            })
            .OrderByDescending(g => g.MeetingDate)
            .ToList();
        return response;
    }

    /// <summary>Returns meetings related to a meeting (from stored deterministic relations).</summary>
    public async Task<IReadOnlyList<Meeting>> RelatedMeetingsAsync(long meetingId, CancellationToken ct = default)
    {
        var relations = await _meetings.GetRelationsAsync(meetingId, ct).ConfigureAwait(false);
        var result = new List<Meeting>();
        foreach (var r in relations)
        {
            var targetId = r.SourceMeetingId == meetingId ? r.TargetMeetingId : r.SourceMeetingId;
            var m = await _meetings.GetAsync(targetId, ct).ConfigureAwait(false);
            if (m is not null) result.Add(m);
        }
        return result;
    }
}

public sealed class SearchResponse
{
    public string Query { get; set; } = string.Empty;
    public bool IsQuestion { get; set; }
    public List<SearchHit> Hits { get; set; } = [];
    public List<SearchMeetingGroup> GroupedByMeeting { get; set; } = [];
}

public sealed class SearchMeetingGroup
{
    public long MeetingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly MeetingDate { get; set; }
    public List<SearchHit> Hits { get; set; } = [];
}
