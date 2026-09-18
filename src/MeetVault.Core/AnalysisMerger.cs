namespace MeetVault.Core;

/// <summary>
/// Deterministically merges chunk-level extractions into one compact intermediate representation,
/// deduplicating near-identical items so the final LLM synthesis pass stays small.
/// </summary>
public static class AnalysisMerger
{
    public static ChunkExtraction Merge(IReadOnlyList<ChunkExtraction> chunks)
    {
        var merged = new ChunkExtraction();
        foreach (var chunk in chunks)
        {
            merged.Topics = merged.Topics.Concat(chunk.Topics).ToList();
            merged.KeyDiscussionPoints = merged.KeyDiscussionPoints.Concat(chunk.KeyDiscussionPoints).ToList();
            merged.Decisions = merged.Decisions.Concat(chunk.Decisions).ToList();
            merged.ActionItems = merged.ActionItems.Concat(chunk.ActionItems).ToList();
            merged.Deadlines = merged.Deadlines.Concat(chunk.Deadlines).ToList();
            merged.Risks = merged.Risks.Concat(chunk.Risks).ToList();
            merged.OpenQuestions = merged.OpenQuestions.Concat(chunk.OpenQuestions).ToList();
            merged.FollowUps = merged.FollowUps.Concat(chunk.FollowUps).ToList();
            merged.Participants = merged.Participants.Concat(chunk.Participants).ToList();
            merged.Entities = merged.Entities.Concat(chunk.Entities).ToList();
        }

        merged.Topics = DedupeStrings(merged.Topics, 20);
        merged.KeyDiscussionPoints = DedupeStrings(merged.KeyDiscussionPoints, 25);
        merged.Deadlines = DedupeStrings(merged.Deadlines, 15);
        merged.Risks = DedupeStrings(merged.Risks, 15);
        merged.Participants = DedupeStrings(merged.Participants, 20);
        merged.Decisions = DedupeBy(merged.Decisions, d => d.DecisionText, 25);
        merged.ActionItems = DedupeBy(merged.ActionItems, a => a.Task, 25);
        merged.OpenQuestions = DedupeBy(merged.OpenQuestions, q => q.Question, 25);
        merged.FollowUps = DedupeBy(merged.FollowUps, f => f.Description, 25);
        merged.Entities = merged.Entities
            .Where(e => !string.IsNullOrWhiteSpace(e.EntityName))
            .GroupBy(e => e.EntityName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(60)
            .ToList();
        return merged;
    }

    private static List<string> DedupeStrings(IEnumerable<string> items, int cap)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(SimilarityComparer.Instance);
        foreach (var item in items.Where(i => !string.IsNullOrWhiteSpace(i)))
        {
            var normalized = item.Trim();
            if (seen.Add(normalized))
                result.Add(normalized);
            if (result.Count >= cap) break;
        }
        return result;
    }

    private static List<T> DedupeBy<T>(IEnumerable<T> items, Func<T, string> keySelector, int cap)
    {
        var result = new List<T>();
        var seen = new HashSet<string>(SimilarityComparer.Instance);
        foreach (var item in items)
        {
            var key = keySelector(item);
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (seen.Add(key.Trim()))
                result.Add(item);
            if (result.Count >= cap) break;
        }
        return result;
    }
}

/// <summary>Case-insensitive, token-overlap similarity used for deterministic dedupe.</summary>
public sealed class SimilarityComparer : IEqualityComparer<string>
{
    public static readonly SimilarityComparer Instance = new();

    public bool Equals(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        var nx = Normalize(x);
        var ny = Normalize(y);
        if (nx == ny) return true;
        // Jaccard over word tokens; 0.8 treats near-identical phrasing as duplicates.
        var tx = new HashSet<string>(nx.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var ty = new HashSet<string>(ny.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (tx.Count == 0 || ty.Count == 0) return false;
        int inter = tx.Count(t => ty.Contains(t));
        double union = tx.Count + ty.Count - inter;
        return inter / union >= 0.8;
    }

    // Fuzzy equality cannot be mapped to a single hash per equal-group, so every entry
    // lands in one bucket and Equals decides. Collections here are small (≤ ~60 items),
    // making this the correct and cheap option.
    public int GetHashCode(string obj) => 0;

    private static string Normalize(string s) =>
        string.Join(' ', s.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string(t.Where(char.IsLetterOrDigit).ToArray()))
            .Where(t => t.Length > 0));
}
