using MeetVault.Core;
using Microsoft.Data.Sqlite;

namespace MeetVault.Infrastructure;

public sealed partial class SqliteMeetingRepository
{
    private async Task<IReadOnlyList<Meeting>> QueryListAsync(string sql, Action<SqliteCommand>? bind, CancellationToken ct)
    {
        return await _db.WithConnectionAsync(async conn =>
        {
            var list = new List<Meeting>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            bind?.Invoke(cmd);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(MapMeeting(r));
            return (IReadOnlyList<Meeting>)list;
        }).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<Meeting>> GetAllAsync(CancellationToken ct = default) =>
        QueryListAsync($"SELECT {MeetingColumns} FROM meetings ORDER BY meeting_date DESC, start_time DESC;", null, ct);

    public Task<IReadOnlyList<Meeting>> GetByDateAsync(DateOnly date, CancellationToken ct = default) =>
        QueryListAsync(
            $"SELECT {MeetingColumns} FROM meetings WHERE meeting_date=$d ORDER BY start_time DESC;",
            cmd => cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd")), ct);

    public Task<IReadOnlyList<Meeting>> GetByStatusAsync(ProcessingStatus status, CancellationToken ct = default) =>
        QueryListAsync(
            $"SELECT {MeetingColumns} FROM meetings WHERE status=$s ORDER BY updated_at DESC;",
            cmd => cmd.Parameters.AddWithValue("$s", (long)status), ct);
}
