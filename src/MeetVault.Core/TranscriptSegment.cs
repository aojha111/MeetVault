using System.Text.Json.Serialization;

namespace MeetVault.Core;

/// <summary>A single timestamped transcript segment for a meeting.</summary>
public sealed class TranscriptSegment
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("meetingId")]
    public long MeetingId { get; set; }

    [JsonPropertyName("sequence")]
    public int Sequence { get; set; }

    /// <summary>Segment start offset from the beginning of the recording, in milliseconds.</summary>
    [JsonPropertyName("startMs")]
    public long StartMs { get; set; }

    /// <summary>Segment end offset from the beginning of the recording, in milliseconds.</summary>
    [JsonPropertyName("endMs")]
    public long EndMs { get; set; }

    /// <summary>Speaker label such as "Speaker 1", or null when diarization is unavailable.</summary>
    [JsonPropertyName("speaker")]
    public string? Speaker { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Average token probability reported by whisper.cpp (0-1), when available.</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }

    [JsonIgnore]
    public string Timecode => TimecodeFormatter.FormatMs(StartMs);
}
