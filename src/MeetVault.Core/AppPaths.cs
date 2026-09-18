namespace MeetVault.Core;

/// <summary>
/// Resolves the portable data layout for MeetVault. All paths derive from a single root so the
/// application runs both installed and portable. Layout:
/// <code>
/// root/
/// ├── MeetVault.exe
/// ├── runtime/    (ffmpeg, whisper, llama, piper executables)
/// ├── models/     (whisper, llm, tts weights)
/// ├── data/       (meetings.db, meetings/YYYY/MM/DD/&lt;id&gt;/, cache/)
/// ├── config/     (appsettings.json, model-registry.json)
/// └── logs/
/// </code>
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string rootDirectory)
    {
        Root = Path.GetFullPath(rootDirectory);
        RuntimeDir = Path.Combine(Root, "runtime");
        ModelsDir = Path.Combine(Root, "models");
        DataDir = Path.Combine(Root, "data");
        ConfigDir = Path.Combine(Root, "config");
        LogsDir = Path.Combine(Root, "logs");
        DatabasePath = Path.Combine(DataDir, "meetings.db");
        MeetingsDir = Path.Combine(DataDir, "meetings");
        CacheDir = Path.Combine(DataDir, "cache");
        DownloadsDir = Path.Combine(CacheDir, "downloads");
        SettingsPath = Path.Combine(ConfigDir, "appsettings.json");
        ModelRegistryPath = Path.Combine(ConfigDir, "model-registry.json");
        FfmpegDir = Path.Combine(RuntimeDir, "ffmpeg");
        WhisperDir = Path.Combine(RuntimeDir, "whisper");
        LlamaDir = Path.Combine(RuntimeDir, "llama");
        PiperDir = Path.Combine(RuntimeDir, "piper");
        WhisperModelsDir = Path.Combine(ModelsDir, "whisper");
        LlmModelsDir = Path.Combine(ModelsDir, "llm");
        TtsModelsDir = Path.Combine(ModelsDir, "tts");
        InstalledMarkerDir = Path.Combine(DataDir, "installed");
    }

    public string Root { get; }
    public string RuntimeDir { get; }
    public string ModelsDir { get; }
    public string DataDir { get; }
    public string ConfigDir { get; }
    public string LogsDir { get; }
    public string DatabasePath { get; }
    public string MeetingsDir { get; }
    public string CacheDir { get; }
    public string DownloadsDir { get; }
    public string SettingsPath { get; }
    public string ModelRegistryPath { get; }
    public string FfmpegDir { get; }
    public string WhisperDir { get; }
    public string LlamaDir { get; }
    public string PiperDir { get; }
    public string WhisperModelsDir { get; }
    public string LlmModelsDir { get; }
    public string TtsModelsDir { get; }
    public string InstalledMarkerDir { get; }

    public string FfmpegExe => Path.Combine(FfmpegDir, "ffmpeg.exe");
    public string FfprobeExe => Path.Combine(FfmpegDir, "ffprobe.exe");
    public string WhisperCliExe => Path.Combine(WhisperDir, "whisper-cli.exe");
    public string LlamaServerExe => Path.Combine(LlamaDir, "llama-server.exe");
    public string PiperExe => Path.Combine(PiperDir, "piper.exe");

    /// <summary>Per-meeting working directory: data/meetings/YYYY/MM/DD/&lt;meeting-id&gt;/.</summary>
    public string MeetingDirectory(long meetingId, DateOnly date) =>
        Path.Combine(MeetingsDir, date.Year.ToString("0000"), date.Month.ToString("00"), date.Day.ToString("00"), meetingId.ToString());

    /// <summary>Directory where an installed pack's payload was extracted.</summary>
    public string InstalledPackDirectory(string packId) => Path.Combine(InstalledMarkerDir, packId);

    /// <summary>Marker file proving a pack finished download + verification + extraction.</summary>
    public string InstalledMarkerPath(string packId) => Path.Combine(InstalledMarkerDir, packId + ".installed.json");

    /// <summary>Pack-local extracted root used at runtime (layout depends on the archive).</summary>
    public string RuntimeBinDirectory(string packId) => Path.Combine(InstalledPackDirectory(packId), "bin");

    public void EnsureCoreDirectories()
    {
        Directory.CreateDirectory(RuntimeDir);
        Directory.CreateDirectory(ModelsDir);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(MeetingsDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(DownloadsDir);
        Directory.CreateDirectory(InstalledMarkerDir);
        Directory.CreateDirectory(WhisperModelsDir);
        Directory.CreateDirectory(LlmModelsDir);
        Directory.CreateDirectory(TtsModelsDir);
    }

    /// <summary>Picks the vault root: next to the exe when writable (portable), else %LOCALAPPDATA%\MeetVault.</summary>
    public static string ResolveDefaultRoot()
    {
        try
        {
            var exeDir = AppContext.BaseDirectory;
            var probe = Path.Combine(exeDir, ".portable-root");
            Directory.CreateDirectory(probe);
            Directory.Delete(probe);
            return exeDir;
        }
        catch (UnauthorizedAccessException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetVault");
        }
        catch (IOException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeetVault");
        }
    }
}
