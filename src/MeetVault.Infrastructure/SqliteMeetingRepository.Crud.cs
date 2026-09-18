using MeetVault.Core;
using Microsoft.Data.Sqlite;

namespace MeetVault.Infrastructure;

public sealed partial class SqliteMeetingRepository
{
    public async Task<Meeting> CreateAsync(Meeting meeting, CancellationToken ct = default)
    {
        return await _db.WithConnectionAsync(async conn =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO meetings (title, meeting_date, start_time, duration_seconds, source_file_path,
                    audio_file_path, transcript_file_path, analysis_file_path, audio_brief_path,
                    status, completed_stage, last_error, created_at, updated_at)
                VALUES ($title, $meeting_date, $start_time, $duration_seconds, $source_file_path,
                    $audio_file_path, $transcript_file_path, $analysis_file_path, $audio_brief_path,
                    $status, $completed_stage, $last_error, $created_at, $updated_at);
                SELECT last_insert_rowid();
                """;
            BindMeeting(cmd, meeting);
            meeting.Id = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            return meeting;
        }).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Meeting meeting, CancellationToken ct = default)
    {
        await _db.WithConnectionAsync(async conn =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                UPDATE meetings SET
                    title=$title, meeting_date=$meeting_date, start_time=$start_time,
                    duration_seconds=$duration_seconds, source_file_path=$source_file_path,
                    audio_file_path=$audio_file_path, transcript_file_path=$transcript_file_path,
                    analysis_file_path=$analysis_file_path, audio_brief_path=$audio_brief_path,
                    status=$status, completed_stage=$completed_stage, last_error=$last_error,
                    created_at=$created_at, updated_at=$updated_at
                WHERE id=$id;
                """;
            BindMeeting(cmd, meeting);
            cmd.Parameters.AddWithValue("$id", meeting.Id);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    public async Task<Meeting?> GetAsync(long id, CancellationToken ct = default)
    {
        return await _db.WithConnectionAsync(async conn =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {MeetingColumns} FROM meetings WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", id);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await r.ReadAsync(ct).ConfigureAwait(false) ? MapMeeting(r) : null;
        }).ConfigureAwait(false);
    }

    public async Task<Meeting?> GetBySourcePathAsync(string sourceFilePath, CancellationToken ct = default)
    {
        return await _db.WithConnectionAsync(async conn =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {MeetingColumns} FROM meetings WHERE source_file_path=$p ORDER BY id LIMIT 1;";
            cmd.Parameters.AddWithValue("$p", Path.GetFullPath(sourceFilePath));
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            return await r.ReadAsync(ct).ConfigureAwait(false) ? MapMeeting(r) : null;
        }).ConfigureAwait(false);
    }

    public Task DeleteAsync(long id, CancellationToken ct = default) =>
        _db.WithConnectionAsync<bool>(async conn =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM meetings WHERE id=$id; DELETE FROM transcript_fts WHERE meeting_id=$id; DELETE FROM knowledge_fts WHERE meeting_id=$id;";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        });
}
