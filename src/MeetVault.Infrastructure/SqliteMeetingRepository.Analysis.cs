using System.Text.Json;
using MeetVault.Core;
using Microsoft.Data.Sqlite;

namespace MeetVault.Infrastructure;

public sealed partial class SqliteMeetingRepository
{
    private static readonly (string DeleteSql, string InsertSql, Func<MeetingAnalysis, IEnumerable<object?[]>> Insert)[] AnalysisWriters =
    [
        ("DELETE FROM topics WHERE meeting_id=$id;",
            "INSERT INTO topics (meeting_id, name, description) VALUES ($id, $v1, $v2);",
            a => a.Topics.Select(t => new object?[] { t, null })),

        ("DELETE FROM decisions WHERE meeting_id=$id;",
            "INSERT INTO decisions (meeting_id, decision_text, source_segment_ids, created_at) VALUES ($id, $v1, $v2, $v3);",
            a => a.Decisions.Select(d => new object?[]
            {
                d.DecisionText,
                JsonSerializer.Serialize(d.SourceSegmentIds),
                DateTimeOffset.Now.ToString("O"),
            })),

        ("DELETE FROM action_items WHERE meeting_id=$id;",
            "INSERT INTO action_items (meeting_id, task, owner, deadline, status, source_segment_ids) VALUES ($id, $v1, $v2, $v3, $v4, $v5);",
            a => a.ActionItems.Select(x => new object?[]
            {
                x.Task, x.Owner, x.Deadline, x.Status,
                JsonSerializer.Serialize(x.SourceSegmentIds),
            })),

        ("DELETE FROM open_questions WHERE meeting_id=$id;",
            "INSERT INTO open_questions (meeting_id, question, status) VALUES ($id, $v1, $v2);",
            a => a.OpenQuestions.Select(q => new object?[] { q.Question, q.Status })),
    ];

    private static void BindRow(SqliteCommand cmd, long meetingId, object?[] values)
    {
        cmd.Parameters.AddWithValue("$id", meetingId);
        for (int i = 0; i < values.Length; i++)
        {
            cmd.Parameters.AddWithValue($"$v{i + 1}", (object?)values[i] ?? DBNull.Value);
        }
    }
}
