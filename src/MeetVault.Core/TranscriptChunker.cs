namespace MeetVault.Core;

/// <summary>
/// Splits a transcript into chunks small enough for the local LLM context window.
/// Never assumes the full transcript fits in one context.
/// </summary>
public sealed class TranscriptChunker
{
    private readonly int _maxWordsPerChunk;
    private readonly int _maxSecondsPerChunk;

    public TranscriptChunker(int maxWordsPerChunk = 700, int maxSecondsPerChunk = 900)
    {
        _maxWordsPerChunk = Math.Max(50, maxWordsPerChunk);
        _maxSecondsPerChunk = Math.Max(60, maxSecondsPerChunk);
    }

    /// <summary>Groups consecutive segments into chunks bounded by word count and time span.</summary>
    public List<TranscriptChunk> Chunk(IReadOnlyList<TranscriptSegment> segments)
    {
        var chunks = new List<TranscriptChunk>();
        if (segments.Count == 0) return chunks;

        var current = new List<TranscriptSegment>();
        long chunkStart = segments[0].StartMs;
        int wordCount = 0;

        foreach (var seg in segments)
        {
            int segWords = CountWords(seg.Text);
            long span = Math.Max(0, seg.EndMs - chunkStart);

            bool spanTooLong = current.Count > 0 && span > _maxSecondsPerChunk * 1000L;
            bool tooManyWords = current.Count > 0 && wordCount + segWords > _maxWordsPerChunk;

            if (spanTooLong || tooManyWords)
            {
                chunks.Add(ToChunk(chunks.Count + 1, current));
                current = [];
                chunkStart = seg.StartMs;
                wordCount = 0;
            }

            current.Add(seg);
            wordCount += segWords;
        }
        if (current.Count > 0)
            chunks.Add(ToChunk(chunks.Count + 1, current));

        return chunks;
    }

    private static TranscriptChunk ToChunk(int index, List<TranscriptSegment> segments) => new()
    {
        Index = index,
        StartMs = segments[0].StartMs,
        EndMs = segments[^1].EndMs,
        Text = string.Join('\n', segments.Select(s => $"[{TimecodeFormatter.FormatMs(s.StartMs)}] {s.Text.Trim()}")),
        SegmentCount = segments.Count,
    };

    public static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}

/// <summary>A time-bounded slice of the transcript fed to the LLM.</summary>
public sealed class TranscriptChunk
{
    public int Index { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string Text { get; set; } = string.Empty;
    public int SegmentCount { get; set; }
}
