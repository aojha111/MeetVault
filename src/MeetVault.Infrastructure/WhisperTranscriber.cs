using System.Text.RegularExpressions;
using MeetVault.Core;

namespace MeetVault.Infrastructure;

/// <summary>
/// whisper.cpp transcription via the whisper-cli executable from the whisper runtime pack.
/// Produces timestamped segments by parsing whisper-cli's JSON output (-oj).
/// </summary>
public sealed partial class WhisperTranscriber : ITranscriptionService
{
    private readonly Func<string?> _cliPath;
    private readonly Func<string?> _modelPathResolver;
    private readonly Func<string?> _vadModelPathResolver;
    private readonly Action<string> _log;

    public WhisperTranscriber(
        Func<string?> cliPath,
        Func<string?> modelPathResolver,
        Func<string?> vadModelPathResolver,
        Action<string>? log = null)
    {
        _cliPath = cliPath;
        _modelPathResolver = modelPathResolver;
        _vadModelPathResolver = vadModelPathResolver;
        _log = log ?? (_ => { });
    }

    public bool IsAvailable => _cliPath() is { } cli && File.Exists(cli) && _modelPathResolver() is not null;

    public async Task<List<TranscriptSegment>> TranscribeAsync(
        string wavPath,
        string language,
        bool enableVad,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        var cliPath = _cliPath()
            ?? throw new InvalidOperationException("whisper.cpp runtime is not installed. Install it from Model Manager → Runtimes.");
        var modelPath = _modelPathResolver()
            ?? throw new InvalidOperationException("No Whisper model is installed. Install one from Model Manager.");

        var outputBase = Path.Combine(Path.GetDirectoryName(wavPath)!, Path.GetFileNameWithoutExtension(wavPath) + ".whisper");
        var jsonPath = outputBase + ".json";

        var args = new List<string>
        {
            "-m", modelPath,
            "-f", wavPath,
            "-oj",             // JSON output
            "-of", outputBase, // output file base
            "-np",             // no prints
            "-t", Math.Max(1, Environment.ProcessorCount - 2).ToString(),
        };

        if (!string.IsNullOrEmpty(language) && !language.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-l");
            args.Add(language);
        }
        var vadPath = enableVad ? _vadModelPathResolver() : null;
        if (vadPath is not null)
        {
            args.Add("--vad");
            args.Add("--vad-model");
            args.Add(vadPath);
        }

        progress?.Report(0.02);
        _log($"whisper-cli: model={Path.GetFileName(modelPath)} vad={vadPath is not null}");
        var result = await ProcessRunner.RunAsync(cliPath, args, cancellationToken,
            onStderrLine: (_, e) =>
            {
                if (e.Data is null) return;
                var m = ProgressLine().Match(e.Data);
                if (m.Success && double.TryParse(m.Groups["p"].Value, out var pct))
                    progress?.Report(Math.Clamp(pct / 100.0, 0.0, 0.99));
            },
            timeoutSeconds: 6 * 3600).ConfigureAwait(false);

        if (!File.Exists(jsonPath))
            throw new InvalidOperationException($"whisper-cli did not produce JSON output. Exit {result.ExitCode}. {Truncate(result.StandardError)}");

        var segments = ParseWhisperJson(await File.ReadAllTextAsync(jsonPath, cancellationToken).ConfigureAwait(false));
        try { File.Delete(jsonPath); } catch (IOException) { }
        progress?.Report(1.0);
        return segments;
    }

    private static string Truncate(string s) =>
        string.IsNullOrWhiteSpace(s) ? "(no output)" : (s.Length > 500 ? s[..500] : s);

    [GeneratedRegex(@"progress\s*=\s*(?<p>\d+(?:\.\d+)?)%")]
    private static partial Regex ProgressLine();
}
