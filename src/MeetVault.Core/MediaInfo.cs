namespace MeetVault.Core;

/// <summary>Media metadata read with ffprobe (or estimated) for an imported recording.</summary>
public sealed class MediaInfo
{
    public long DurationSeconds { get; set; }
    public double DurationMs { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public long SizeBytes { get; set; }
    /// <summary>Raw ffprobe JSON when available, for debugging.</summary>
    public string? RawJson { get; set; }
}

/// <summary>Reads media metadata for meeting files.</summary>
public interface IMediaInfoReader
{
    Task<MediaInfo> GetMediaInfoAsync(string filePath, CancellationToken cancellationToken = default);
}
