using MeetVault.Core;
using Microsoft.Data.Sqlite;

namespace MeetVault.Infrastructure;

public sealed partial class SqliteMeetingRepository
{
    public async Task<MeetingAnalysis?> GetAnalysisAsync(long meetingId, CancellationToken ct = default)
    {
        return await _db.WithConnectionAsync<MeetingAnalysis?>(async conn =>
        {
            string? path = null;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT analysis_file_path FROM meetings WHERE id=$id;";
                cmd.Parameters.AddWithValue("$id", meetingId);
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await r.ReadAsync(ct).ConfigureAwait(false) && !r.IsDBNull(0))
                    path = r.GetString(0);
            }

            // Prefer the complete analysis JSON file; fall back to normalized tables.
            if (path is not null && File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                var parsed = JsonUtil.ParseLenient<MeetingAnalysis>(json);
                if (parsed is not null) return parsed;
            }

            var analysis = new MeetingAnalysis();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM topics WHERE meeting_id=$id ORDER BY id;";
                cmd.Parameters.AddWithValue("$id", meetingId);
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await r.ReadAsync(ct).ConfigureAwait(false)) analysis.Topics.Add(r.GetString(0));
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT decision_text FROM decisions WHERE meeting_id=$id ORDER BY id;";
                cmd.Parameters.AddWithValue("$id", meetingId);
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await r.ReadAsync(ct).ConfigureAwait(false))
                    analysis.Decisions.Add(new Decision { DecisionText = r.GetString(0) });
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT task, owner, deadline, status FROM action_items WHERE meeting_id=$id ORDER BY id;";
                cmd.Parameters.AddWithValue("$id", meetingId);
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    analysis.ActionItems.Add(new ActionItem
                    {
                        Task = r.GetString(0),
                        Owner = r.IsDBNull(1) ? null : r.GetString(1),
                        Deadline = r.IsDBNull(2) ? null : r.GetString(2),
                        Status = r.IsDBNull(3) ? "Open" : r.GetString(3),
                    });
                }
            }
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT question, status FROM open_questions WHERE meeting_id=$id ORDER BY id;";
                cmd.Parameters.AddWithValue("$id", meetingId);
                await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await r.ReadAsync(ct).ConfigureAwait(false))
                    analysis.OpenQuestions.Add(new OpenQuestion { Question = r.GetString(0), Status = r.GetString(1) });
            }
            return analysis;
        }).ConfigureAwait(false);
    }
}
