using System.Text.Json;
using MeetVault.Core;

namespace MeetVault.Infrastructure;

public sealed partial class WhisperTranscriber
{
    /// <summary>
    /// Parses whisper.cpp JSON output. Newer builds emit
    /// {"transcription":[{"offsets":[startMs,endMs],"text":"..."}]};
    /// older formats use "segments":[{"start","end","text"}]. Both are handled.
    /// </summary>
    public static List<TranscriptSegment> ParseWhisperJson(string json, long meetingId = 0)
    {
        var segments = new List<TranscriptSegment>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        JsonElement items = default;
        var haveItems = false;
        if (root.TryGetProperty("transcription", out var transcription))
        {
            items = transcription;
            haveItems = true;
        }
        else if (root.TryGetProperty("segments", out var segs))
        {
            items = segs;
            haveItems = true;
        }
        if (!haveItems) return segments;

        int seq = 1;
        foreach (var item in items.EnumerateArray())
        {
            long startMs = 0, endMs = 0;
            string text = string.Empty;
            double? confidence = null;

            if (item.TryGetProperty("offsets", out var offsets) && offsets.ValueKind == JsonValueKind.Array)
            {
                var arr = offsets.EnumerateArray().ToArray();
                if (arr.Length >= 2)
                {
                    startMs = arr[0].GetInt64();
                    endMs = arr[1].GetInt64();
                }
            }
            else if (item.TryGetProperty("start", out var start) && item.TryGetProperty("end", out var end))
            {
                // Legacy format: timestamps in centiseconds.
                startMs = start.ValueKind == JsonValueKind.Number ? start.GetInt64() * 10 : ParseTime(start.GetString());
                endMs = end.ValueKind == JsonValueKind.Number ? end.GetInt64() * 10 : ParseTime(end.GetString());
            }

            if (item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                text = t.GetString() ?? string.Empty;

            if (item.TryGetProperty("p", out var p) && p.ValueKind == JsonValueKind.Number)
                confidence = p.GetDouble();

            text = DecodeWhisperToken(text).Trim();
            if (text.Length == 0) continue;
            segments.Add(new TranscriptSegment
            {
                MeetingId = meetingId,
                Sequence = seq++,
                StartMs = startMs,
                EndMs = Math.Max(endMs, startMs),
                Text = text,
                Confidence = confidence,
            });
        }
        return segments;
    }

    /// <summary>Converts a whisper.cpp token like "▁Hello" to " Hello".</summary>
    public static string DecodeWhisperToken(string? token)
    {
        if (token is null) return string.Empty;
        return token.Replace("▁", " ").Replace("Ġ", " ");
    }

    private static long ParseTime(string? timecode)
    {
        var parsed = TimecodeFormatter.ParseToMs(timecode);
        return parsed ?? 0;
    }
}
