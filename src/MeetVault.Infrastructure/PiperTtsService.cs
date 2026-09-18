using System.Diagnostics;
using MeetVault.Core;

namespace MeetVault.Infrastructure;

/// <summary>
/// Offline neural TTS using the Piper runtime pack (piper.exe + espeak-ng data) and a
/// downloadable ONNX voice model. Produces mono WAV at the voice's native sample rate.
/// </summary>
public sealed class PiperTtsService : ITtsService
{
    private readonly Func<string?> _piperExePath;
    private readonly Func<IReadOnlyList<(string VoiceId, string ModelPath)>> _voiceResolver;
    private readonly Action<string> _log;

    public PiperTtsService(
        Func<string?> piperExePath,
        Func<IReadOnlyList<(string VoiceId, string ModelPath)>> voiceResolver,
        Action<string>? log = null)
    {
        _piperExePath = piperExePath;
        _voiceResolver = voiceResolver;
        _log = log ?? (_ => { });
    }

    public bool IsAvailable => _piperExePath() is { } piper && File.Exists(piper) && _voiceResolver().Count > 0;

    public IReadOnlyList<string> ListVoices() =>
        _voiceResolver().Select(v => v.VoiceId).ToList();

    public async Task SynthesizeToWavAsync(string text, string voiceId, string outputWavPath, CancellationToken cancellationToken = default)
    {
        var piperExe = _piperExePath()
            ?? throw new InvalidOperationException("Piper TTS runtime is not installed. Install it from Model Manager → Runtimes.");
        var voice = _voiceResolver().FirstOrDefault(v => v.VoiceId.Equals(voiceId, StringComparison.OrdinalIgnoreCase));
        if (voice.VoiceId is null) voice = _voiceResolver().FirstOrDefault();
        if (voice.VoiceId is null)
            throw new InvalidOperationException("No Piper voice model is installed.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);

        var args = new List<string> { "--model", voice.ModelPath, "--output_file", outputWavPath };
        var jsonSidecar = voice.ModelPath + ".json";
        if (File.Exists(jsonSidecar)) args.AddRange(new[] { "--json_config", jsonSidecar });

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = piperExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(piperExe) ?? ".",
            },
        };
        foreach (var a in args) process.StartInfo.ArgumentList.Add(a);
        process.Start();

        await process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ProcessRunner.TryKill(process);
            throw;
        }

        if (process.ExitCode != 0 || !File.Exists(outputWavPath) || new FileInfo(outputWavPath).Length <= 44)
            throw new InvalidOperationException($"Piper TTS failed (exit {process.ExitCode}).");
        _log($"Piper synthesized {new FileInfo(outputWavPath).Length} bytes.");
    }
}
