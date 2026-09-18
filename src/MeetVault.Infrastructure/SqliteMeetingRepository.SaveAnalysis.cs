using MeetVault.Core;
using Microsoft.Data.Sqlite;

namespace MeetVault.Infrastructure;

public sealed partial class SqliteMeetingRepository
{
    public async Task SaveAnalysisAsync(long meetingId, MeetingAnalysis analysis, string analysisPath, CancellationToken ct = default)
    {
        var json = JsonUtil.ToPrettyJson(analysis);
        await _db.WithConnectionAsync<bool>(async conn =>
        {
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (var (deleteSql, insertSql, insert) in AnalysisWriters)
                {
                    await using (var del = conn.CreateCommand())
                    {
                        del.Transaction = tx;
                        del.CommandText = deleteSql;
                        del.Parameters.AddWithValue("$id", meetingId);
                        await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    foreach (var row in insert(analysis))
                    {
                        await using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = insertSql;
                        BindRow(cmd, meetingId, row);
                        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
                }

                await using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM meeting_entities WHERE meeting_id=$id;";
                    del.Parameters.AddWithValue("$id", meetingId);
                    await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                await using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT INTO meeting_entities (meeting_id, entity_type, entity_name) VALUES ($id, $type, $name);";
                    var pId = ins.Parameters.Add("$id", SqliteType.Integer);
                    var pType = ins.Parameters.Add("$type", SqliteType.Text);
                    var pName = ins.Parameters.Add("$name", SqliteType.Text);
                    foreach (var e in analysis.Entities)
                    {
                        pId.Value = meetingId;
                        pType.Value = e.EntityType;
                        pName.Value = e.EntityName;
                        await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
                }

                await using (var upd = conn.CreateCommand())
                {
                    upd.Transaction = tx;
                    upd.CommandText = "UPDATE meetings SET analysis_file_path=$p, updated_at=$u WHERE id=$id;";
                    upd.Parameters.AddWithValue("$p", analysisPath);
                    upd.Parameters.AddWithValue("$u", DateTimeOffset.Now.ToString("O"));
                    upd.Parameters.AddWithValue("$id", meetingId);
                    await upd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await WriteKnowledgeFtsAsync(conn, tx, meetingId, analysis).ConfigureAwait(false);

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
        }).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(analysisPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(analysisPath)!);
            await File.WriteAllTextAsync(analysisPath, json, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Refreshes the analysis side of the FTS knowledge index.</summary>
    private static async Task WriteKnowledgeFtsAsync(SqliteConnection conn, SqliteTransaction tx, long meetingId, MeetingAnalysis analysis)
    {
        await using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM knowledge_fts WHERE meeting_id=$id;";
        del.Parameters.AddWithValue("$id", meetingId);
        await del.ExecuteNonQueryAsync().ConfigureAwait(false);

        var entries = new List<(string Kind, string Content)>
        {
            ("summary", analysis.Summary ?? string.Empty),
            ("topics", string.Join('\n', analysis.Topics)),
            ("decisions", string.Join('\n', analysis.Decisions.Select(d => d.DecisionText))),
            ("actions", string.Join('\n', analysis.ActionItems.Select(a => $"{a.Task} {a.Owner} {a.Deadline}"))),
            ("questions", string.Join('\n', analysis.OpenQuestions.Select(q => q.Question))),
            ("risks", string.Join('\n', analysis.Risks)),
            ("people", string.Join('\n', analysis.Participants.Concat(analysis.Entities.Select(e => e.EntityName)))),
        };

        await using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO knowledge_fts (meeting_id, kind, content) VALUES ($id, $kind, $content);";
        var pId = ins.Parameters.Add("$id", SqliteType.Integer);
        var pKind = ins.Parameters.Add("$kind", SqliteType.Text);
        var pContent = ins.Parameters.Add("$content", SqliteType.Text);
        foreach (var (kind, content) in entries)
        {
            if (string.IsNullOrWhiteSpace(content)) continue;
            pId.Value = meetingId;
            pKind.Value = kind;
            pContent.Value = content;
            await ins.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
