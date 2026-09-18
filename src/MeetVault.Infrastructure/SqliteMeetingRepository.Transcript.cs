using MeetVault.Core;
using Microsoft.Data.Sqlite;

namespace MeetVault.Infrastructure;

public sealed partial class SqliteMeetingRepository
{
    public async Task ReplaceTranscriptAsync(long meetingId, IReadOnlyList<TranscriptSegment> segments, string transcriptPath, CancellationToken ct = default)
    {
        await _db.WithConnectionAsync<bool>(async conn =>
        {
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                await using (var del = conn.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM transcript_segments WHERE meeting_id=$id; DELETE FROM transcript_fts WHERE meeting_id=$id;";
                    del.Parameters.AddWithValue("$id", meetingId);
                    await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = tx;
                    ins.CommandText = """
                        INSERT INTO transcript_segments (meeting_id, sequence, start_ms, end_ms, speaker, text, confidence)
                        VALUES ($mid, $seq, $start, $end, $speaker, $text, $conf);
                        """;
                    var pMid = ins.Parameters.Add("$mid", SqliteType.Integer);
                    var pSeq = ins.Parameters.Add("$seq", SqliteType.Integer);
                    var pStart = ins.Parameters.Add("$start", SqliteType.Integer);
                    var pEnd = ins.Parameters.Add("$end", SqliteType.Integer);
                    var pSpeaker = ins.Parameters.Add("$speaker", SqliteType.Text);
                    var pText = ins.Parameters.Add("$text", SqliteType.Text);
                    var pConf = new SqliteParameter { ParameterName = "$conf" };
                    ins.Parameters.Add(pConf);

                    int seq = 1;
                    foreach (var seg in segments)
                    {
                        pMid.Value = meetingId;
                        pSeq.Value = seq++;
                        pStart.Value = seg.StartMs;
                        pEnd.Value = seg.EndMs;
                        pSpeaker.Value = (object?)seg.Speaker ?? DBNull.Value;
                        pText.Value = seg.Text;
                        object confValue = seg.Confidence.HasValue ? (object)seg.Confidence.Value : DBNull.Value;
                        pConf.Value = confValue;
                        await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
                }

                await using (var fts = conn.CreateCommand())
                {
                    fts.Transaction = tx;
                    fts.CommandText = "INSERT INTO transcript_fts (meeting_id, text) VALUES ($mid, $text);";
                    fts.Parameters.AddWithValue("$mid", meetingId);
                    fts.Parameters.AddWithValue("$text", string.Join('\n', segments.Select(s => s.Text)));
                    await fts.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(transcriptPath))
                {
                    await using (var upd = conn.CreateCommand())
                    {
                        upd.Transaction = tx;
                        upd.CommandText = "UPDATE meetings SET transcript_file_path=$p, updated_at=$u WHERE id=$id;";
                        upd.Parameters.AddWithValue("$p", transcriptPath);
                        upd.Parameters.AddWithValue("$u", DateTimeOffset.Now.ToString("O"));
                        upd.Parameters.AddWithValue("$id", meetingId);
                        await upd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TranscriptSegment>> GetTranscriptAsync(long meetingId, CancellationToken ct = default)
    {
        return await _db.WithConnectionAsync(async conn =>
        {
            var list = new List<TranscriptSegment>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, meeting_id, sequence, start_ms, end_ms, speaker, text, confidence
                FROM transcript_segments WHERE meeting_id=$id ORDER BY sequence;
                """;
            cmd.Parameters.AddWithValue("$id", meetingId);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new TranscriptSegment
                {
                    Id = r.GetInt64(0),
                    MeetingId = r.GetInt64(1),
                    Sequence = r.GetInt32(2),
                    StartMs = r.GetInt64(3),
                    EndMs = r.GetInt64(4),
                    Speaker = r.IsDBNull(5) ? null : r.GetString(5),
                    Text = r.GetString(6),
                    Confidence = r.IsDBNull(7) ? null : r.GetDouble(7),
                });
            }
            return (IReadOnlyList<TranscriptSegment>)list;
        }).ConfigureAwait(false);
    }
}
