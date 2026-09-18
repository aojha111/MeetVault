using System.Data;
using System.Text.Json;
using MeetVault.Core;
using Microsoft.Data.Sqlite;

namespace MeetVault.Infrastructure;

/// <summary>SQLite implementation of IMeetingRepository + ISearchRepository.</summary>
public sealed partial class SqliteMeetingRepository : IMeetingRepository, ISearchRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly Database _db;

    public SqliteMeetingRepository(Database db) => _db = db;

    // ---------- helpers ----------

    private static Meeting MapMeeting(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        Title = r.GetString(r.GetOrdinal("title")),
        MeetingDate = DateOnly.Parse(r.GetString(r.GetOrdinal("meeting_date"))),
        StartTime = r.IsDBNull(r.GetOrdinal("start_time")) ? null : TimeOnly.Parse(r.GetString(r.GetOrdinal("start_time"))),
        DurationSeconds = r.GetInt64(r.GetOrdinal("duration_seconds")),
        SourceFilePath = r.IsDBNull(r.GetOrdinal("source_file_path")) ? null : r.GetString(r.GetOrdinal("source_file_path")),
        AudioFilePath = r.IsDBNull(r.GetOrdinal("audio_file_path")) ? null : r.GetString(r.GetOrdinal("audio_file_path")),
        TranscriptFilePath = r.IsDBNull(r.GetOrdinal("transcript_file_path")) ? null : r.GetString(r.GetOrdinal("transcript_file_path")),
        AnalysisFilePath = r.IsDBNull(r.GetOrdinal("analysis_file_path")) ? null : r.GetString(r.GetOrdinal("analysis_file_path")),
        AudioBriefPath = r.IsDBNull(r.GetOrdinal("audio_brief_path")) ? null : r.GetString(r.GetOrdinal("audio_brief_path")),
        Status = (ProcessingStatus)r.GetInt64(r.GetOrdinal("status")),
        CompletedStage = (PipelineStage)r.GetInt64(r.GetOrdinal("completed_stage")),
        LastError = r.IsDBNull(r.GetOrdinal("last_error")) ? null : r.GetString(r.GetOrdinal("last_error")),
        CreatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("created_at"))),
        UpdatedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("updated_at"))),
    };

    private const string MeetingColumns = """
        id, title, meeting_date, start_time, duration_seconds, source_file_path, audio_file_path,
        transcript_file_path, analysis_file_path, audio_brief_path, status, completed_stage,
        last_error, created_at, updated_at
        """;

    private static void BindMeeting(SqliteCommand cmd, Meeting m)
    {
        cmd.Parameters.AddWithValue("$title", m.Title);
        cmd.Parameters.AddWithValue("$meeting_date", m.MeetingDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$start_time", (object?)m.StartTime?.ToString("HH:mm") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$duration_seconds", m.DurationSeconds);
        cmd.Parameters.AddWithValue("$source_file_path", (object?)m.SourceFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$audio_file_path", (object?)m.AudioFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$transcript_file_path", (object?)m.TranscriptFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$analysis_file_path", (object?)m.AnalysisFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$audio_brief_path", (object?)m.AudioBriefPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (long)m.Status);
        cmd.Parameters.AddWithValue("$completed_stage", (long)m.CompletedStage);
        cmd.Parameters.AddWithValue("$last_error", (object?)m.LastError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created_at", m.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated_at", m.UpdatedAt.ToString("O"));
    }
}
