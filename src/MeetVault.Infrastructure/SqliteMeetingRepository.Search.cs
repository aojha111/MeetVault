using MeetVault.Core;
using Microsoft.Data.Sqlite;

namespace MeetVault.Infrastructure;

public sealed partial class SqliteMeetingRepository
{
    /// <summary>Builds a safe FTS5 query: quoted terms (AND), escaping embedded quotes.</summary>
    public static string BuildFtsQuery(string query)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 0)
            .Select(t => "\"" + t.Replace("\"", "\"\"") + "\"")
            .Take(12);
        return string.Join(' ', terms);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var fts = BuildFtsQuery(query);
        if (fts.Length == 0) return [];

        return await _db.WithConnectionAsync(async conn =>
        {
            var hits = new List<SearchHit>();

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT id, title, meeting_date FROM meetings
                    WHERE title LIKE $like
                      AND ($from IS NULL OR meeting_date >= $from)
                      AND ($to IS NULL OR meeting_date <= $to)
                    LIMIT 50;
                    """;
                cmd.Parameters.AddWithValue("$like", $"%{query}%");
                cmd.Parameters.AddWithValue("$from", from?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$to", to?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    hits.Add(new SearchHit
                    {
                        MeetingId = r.GetInt64(0),
                        Title = r.GetString(1),
                        MeetingDate = DateOnly.Parse(r.GetString(2)),
                        MatchKind = "title",
                        Snippet = r.GetString(1),
                    });
                }
            }

            hits.AddRange(await FtsKnowledgeSearchAsync(conn, fts, from, to, ct).ConfigureAwait(false));
            hits.AddRange(await FtsTranscriptSearchAsync(conn, fts, from, to, ct).ConfigureAwait(false));
            return (IReadOnlyList<SearchHit>)hits;
        }).ConfigureAwait(false);
    }
}
