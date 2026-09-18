using MeetVault.Core;
using Microsoft.Data.Sqlite;

namespace MeetVault.Infrastructure;

public sealed partial class SqliteMeetingRepository
{
    private static async Task<List<SearchHit>> FtsKnowledgeSearchAsync(
        SqliteConnection conn, string fts, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var hits = new List<SearchHit>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT k.meeting_id, k.kind, snippet(knowledge_fts, 2, '[', ']', '…', 12), m.title, m.meeting_date
            FROM knowledge_fts k JOIN meetings m ON m.id = k.meeting_id
            WHERE knowledge_fts MATCH $q
              AND ($from IS NULL OR m.meeting_date >= $from)
              AND ($to IS NULL OR m.meeting_date <= $to)
            ORDER BY rank LIMIT 80;
            """;
        cmd.Parameters.AddWithValue("$q", fts);
        cmd.Parameters.AddWithValue("$from", from?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$to", to?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            hits.Add(new SearchHit
            {
                MeetingId = r.GetInt64(0),
                MatchKind = r.GetString(1),
                Snippet = r.GetString(2),
                Title = r.GetString(3),
                MeetingDate = DateOnly.Parse(r.GetString(4)),
            });
        }
        return hits;
    }

    private static async Task<List<SearchHit>> FtsTranscriptSearchAsync(
        SqliteConnection conn, string fts, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var hits = new List<SearchHit>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.meeting_id, snippet(transcript_fts, 1, '[', ']', '…', 10), m.title, m.meeting_date
            FROM transcript_fts t JOIN meetings m ON m.id = t.meeting_id
            WHERE transcript_fts MATCH $q
              AND ($from IS NULL OR m.meeting_date >= $from)
              AND ($to IS NULL OR m.meeting_date <= $to)
            ORDER BY rank LIMIT 60;
            """;
        cmd.Parameters.AddWithValue("$q", fts);
        cmd.Parameters.AddWithValue("$from", from?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$to", to?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            hits.Add(new SearchHit
            {
                MeetingId = r.GetInt64(0),
                MatchKind = "transcript",
                Snippet = r.GetString(1),
                Title = r.GetString(2),
                MeetingDate = DateOnly.Parse(r.GetString(3)),
            });
        }
        return hits;
    }

    public async Task<IReadOnlyList<ActionItem>> GetOpenActionItemsAsync(CancellationToken ct = default)
    {
        return await _db.WithConnectionAsync(async conn =>
        {
            var list = new List<ActionItem>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT task, owner, deadline, status FROM action_items
                WHERE status NOT IN ('Done', 'Completed', 'Closed')
                ORDER BY deadline IS NULL, deadline;
                """;
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new ActionItem
                {
                    Task = r.GetString(0),
                    Owner = r.IsDBNull(1) ? null : r.GetString(1),
                    Deadline = r.IsDBNull(2) ? null : r.GetString(2),
                    Status = r.IsDBNull(3) ? "Open" : r.GetString(3),
                });
            }
            return (IReadOnlyList<ActionItem>)list;
        }).ConfigureAwait(false);
    }
}
