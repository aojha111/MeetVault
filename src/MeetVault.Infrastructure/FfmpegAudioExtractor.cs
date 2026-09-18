using System.Diagnostics;
using System.Text.Json;
using MeetVault.Core;

namespace MeetVault.Infrastructure;

/// <summary>
/// FFmpeg-backed audio extraction. Produces the normalized transcription input:
/// mono, 16 kHz, 16-bit PCM WAV. Also reads media metadata via ffprobe.
/// </summary>
public sealed class FfmpegAudioExtractor : IAudioExtractor, IMediaInfoReader
{
    private readonly Func<string?> _ffmpegPath;
    private readonly Func<string?> _ffprobePath;
    private readonly Action<string> _log;

    public FfmpegAudioExtractor(Func<string?> ffmpegPath, Func<string?> ffprobePath, Action<string>? log = null)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
        _log = log ?? (_ => { });
    }

    public bool IsAvailable => _ffmpegPath() is { } ff && File.Exists(ff) && _ffprobePath() is { } fp && File.Exists(fp);

    /// <summary>
    /// Extracts/converts to 16 kHz mono 16-bit PCM WAV.
    /// When the target already is a .wav, the file is still re-encoded to the normalized profile.
    /// An .mp3 target (used for audio brief compression) is encoded at 64 kbps mono.
    /// </summary>
    public async Task ExtractWavAsync(string inputPath, string outputWavPath, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("FFmpeg is not installed. Install the FFmpeg runtime from Model Manager.");
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            throw new FileNotFoundException("Input media file not found.", inputPath);

        try { Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!); } catch (IOException) { }

        // Sanitize: enforce a known extension on the output argument.
        var ext = Path.GetExtension(outputWavPath).ToLowerInvariant();
        if (ext is not (".wav" or ".mp3"))
            throw new ArgumentException($"Unsupported output extension '{ext}'.");

        string[] args = ext == ".mp3"
            ? ["-y", "-i", inputPath, "-vn", "-ac", "1", "-ar", "22050", "-codec:a", "libmp3lame", "-b:a", "64k", outputWavPath]
            : ["-y", "-i", inputPath, "-vn", "-ac", "1", "-ar", "16000", "-acodec", "pcm_s16le", outputWavPath];

        _log($"ffmpeg {string.Join(' ', args.Take(4))}…");
        var result = await ProcessRunner.RunAsync(_ffmpegPath()!, args, cancellationToken, timeoutSeconds: 7200).ConfigureAwait(false);
        if (!result.Success || !File.Exists(outputWavPath))
            throw new InvalidOperationException($"FFmpeg failed ({result.ExitCode}): {Truncate(result.StandardError)}");
    }

    public async Task<MediaInfo> GetMediaInfoAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var info = new MediaInfo();
        if (!IsAvailable || !File.Exists(inputPath))
        {
            if (File.Exists(inputPath)) info.SizeBytes = new FileInfo(inputPath).Length;
            return info;
        }

        var result = await ProcessRunner.RunAsync(_ffprobePath()!,
        [
            "-v", "error", "-print_format", "json", "-show_format", "-show_streams", inputPath,
        ], cancellationToken).ConfigureAwait(false);

        info.SizeBytes = new FileInfo(inputPath).Length;
        info.RawJson = result.StandardOutput;
        try
        {
            using var doc = JsonDocument.Parse(result.StandardOutput);
            var root = doc.RootElement;
            if (root.TryGetProperty("format", out var fmt))
            {
                if (fmt.TryGetProperty("duration", out var dur) && dur.TryGetDouble(out var d))
                {
                    info.DurationMs = d * 1000;
                    info.DurationSeconds = (long)Math.Round(d);
                }
            }
            foreach (var stream in root.GetProperty("streams").EnumerateArray())
            {
                var type = stream.TryGetProperty("codec_type", out var ct2) ? ct2.GetString() : null;
                var codec = stream.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
                if (type == "video" && info.VideoCodec is null)
                {
                    info.VideoCodec = codec;
                    if (stream.TryGetProperty("width", out var w)) info.Width = w.GetInt32();
                    if (stream.TryGetProperty("height", out var h)) info.Height = h.GetInt32();
                }
                else if (type == "audio" && info.AudioCodec is null)
                {
                    info.AudioCodec = codec;
                    if (stream.TryGetProperty("duration", out var dur) && dur.TryGetDouble(out var d) && info.DurationSeconds == 0)
                    {
                        info.DurationMs = d * 1000;
                        info.DurationSeconds = (long)Math.Round(d);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Metadata parse failure is non-fatal; duration defaults to 0.
        }
        return info;
    }

    private static string Truncate(string s) =>
        string.IsNullOrWhiteSpace(s) ? "(no output)" : (s.Length > 500 ? s[..500] : s);
}
