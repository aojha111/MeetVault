using MeetVault.Core;

namespace MeetVault.Infrastructure;

public sealed partial class AppBootstrapper
{
    private string? ResolveWhisperModelPath()
    {
        var direct = ModelManager.GetInstalledModelFilePath(Settings.WhisperModelId);
        if (direct is not null) return direct;
        foreach (var pack in ModelManager.GetCatalog().Where(p => p.IsInstalled && p.Pack.KindEnum == ModelKind.WhisperModel))
        {
            var path = ModelManager.GetInstalledModelFilePath(pack.Pack.Id);
            if (path is not null) return path;
        }
        return null;
    }

    private string? ResolveVadModelPath() => ModelManager.GetInstalledModelFilePath("whisper-vad-silero");

    private string? ResolveLlmModelPath()
    {
        var direct = ModelManager.GetInstalledModelFilePath(Settings.LlmModelId);
        if (direct is not null) return direct;
        foreach (var pack in ModelManager.GetCatalog().Where(p => p.IsInstalled && p.Pack.KindEnum == ModelKind.LlmModel))
        {
            var path = ModelManager.GetInstalledModelFilePath(pack.Pack.Id);
            if (path is not null) return path;
        }
        return null;
    }

    /// <summary>Locates a runtime executable in an installed runtime pack, then falls back to a secondary pack or the fixed path.</summary>
    private string? ResolveRuntimeExe(string primaryPack, string? fallbackPack, string expectedPath)
    {
        foreach (var packId in new[] { primaryPack, fallbackPack })
        {
            if (packId is null) continue;
            var dir = ModelManager.GetInstalledRuntimeDirectory(packId);
            if (dir is null) continue;

            var exe = Path.Combine(dir, Path.GetFileName(expectedPath));
            if (File.Exists(exe)) return exe;

            var found = Directory.EnumerateFiles(dir, Path.GetFileName(expectedPath), SearchOption.AllDirectories).FirstOrDefault();
            if (found is not null) return found;
        }
        if (File.Exists(expectedPath)) return expectedPath;
        return FindOnPath(Path.GetFileName(expectedPath));
    }

    /// <summary>
    /// Searches the system PATH so machines with ffmpeg/whisper/llama already installed
    /// work even before (or without) downloading the matching runtime pack.
    /// </summary>
    private static string? FindOnPath(string exeName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) return null;
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, exeName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { /* malformed PATH entry */ }
        }
        return null;
    }

    private IReadOnlyList<(string VoiceId, string ModelPath)> ListPiperVoices()
    {
        var voices = new List<(string, string)>();
        try
        {
            if (Directory.Exists(Paths.TtsModelsDir))
            {
                foreach (var onnx in Directory.EnumerateFiles(Paths.TtsModelsDir, "*.onnx", SearchOption.AllDirectories))
                {
                    voices.Add(("piper:" + Path.GetFileNameWithoutExtension(onnx), onnx));
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to list Piper voices.");
        }
        return voices;
    }
}
