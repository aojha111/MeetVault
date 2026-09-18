using MeetVault.Core;
using Microsoft.Data.Sqlite;

namespace MeetVault.Infrastructure;

public sealed partial class SqliteMeetingRepository
{
    public async Task SaveRelationsAsync(long meetingId, IReadOnlyList<MeetingRelation> relations, CancellationToken ct = default)
    {
        await _db.WithConnectionAsync<bool>(async conn =>
        {
            await using var del = conn.CreateCommand();
            del.CommandText = "DELETE FROM meeting_relations WHERE source_meeting_id=$id;";
            del.Parameters.AddWithValue("$id", meetingId);
            await del.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await using var ins = conn.CreateCommand();
            ins.CommandText = "INSERT INTO meeting_relations (source_meeting_id, target_meeting_id, relation_type, evidence) VALUES ($s, $t, $rt, $e);";
            var pS = ins.Parameters.Add("$s", SqliteType.Integer);
            var pT = ins.Parameters.Add("$t", SqliteType.Integer);
            var pRt = ins.Parameters.Add("$rt", SqliteType.Text);
            var pE = ins.Parameters.Add("$e", SqliteType.Text);
            foreach (var r in relations)
            {
                pS.Value = r.SourceMeetingId;
                pT.Value = r.TargetMeetingId;
                pRt.Value = r.RelationType;
                pE.Value = r.Evidence;
                await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            return true;
        }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MeetingRelation>> GetRelationsAsync(long meetingId, CancellationToken ct = default)
    {
        return await _db.WithConnectionAsync(async conn =>
        {
            var list = new List<MeetingRelation>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT source_meeting_id, target_meeting_id, relation_type, evidence
                FROM meeting_relations WHERE source_meeting_id=$id OR target_meeting_id=$id;
                """;
            cmd.Parameters.AddWithValue("$id", meetingId);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new MeetingRelation
                {
                    SourceMeetingId = r.GetInt64(0),
                    TargetMeetingId = r.GetInt64(1),
                    RelationType = r.GetString(2),
                    Evidence = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                });
            }
            return (IReadOnlyList<MeetingRelation>)list;
        }).ConfigureAwait(false);
    }
}
