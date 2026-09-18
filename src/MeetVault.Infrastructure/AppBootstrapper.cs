using MeetVault.Core;

namespace MeetVault.Infrastructure;

/// <summary>
/// Composition root: wires up the full application object graph.
/// Used by the WPF app and the CLI so both use identical behavior.
/// </summary>
public sealed partial class AppBootstrapper : IDisposable
{

    public AppPaths Paths { get; }
    public AppSettings Settings { get; private set; }
    public SettingsService SettingsService { get; }
    public Database Database { get; }
    public SqliteMeetingRepository Repository { get; }
    public ModelRegistryService ModelRegistryService { get; }
    public ModelManager ModelManager { get; }
    public HardwareInfoProvider Hardware { get; }
    public FfmpegAudioExtractor Audio { get; }
    public WhisperTranscriber Transcriber { get; }
    public LlamaLlmService Llm { get; }
    public TtsService Tts { get; }
    public MeetingProcessor Processor { get; }
    public ProcessingQueue Queue { get; }
    public MeetingService Meetings { get; }
    public SearchService Search { get; }

    public AppBootstrapper(string? rootDirectory = null)
    {
        Paths = new AppPaths(rootDirectory ?? AppPaths.ResolveDefaultRoot());
        Paths.EnsureCoreDirectories();

        SettingsService = new SettingsService(Paths.ConfigDir);
        Settings = SettingsService.Load();

        if (string.IsNullOrWhiteSpace(Settings.LibraryRoot))
            Settings.LibraryRoot = Paths.MeetingsDir;
        if (string.IsNullOrWhiteSpace(Settings.ModelsRoot))
            Settings.ModelsRoot = Paths.ModelsDir;

        Database = new Database(Paths.DatabasePath);
        Database.ApplyMigrationsAsync().GetAwaiter().GetResult();
        Repository = new SqliteMeetingRepository(Database);

        ModelRegistryService = new ModelRegistryService(Paths.ModelRegistryPath, () => EmbeddedModelRegistry.Json);
        ModelManager = new ModelManager(Paths, ModelRegistryService, () => Settings.OfflineMode, msg => Serilog.Log.Information("models: {Message}", msg));

        Hardware = new HardwareInfoProvider();

        // First-run (or after a settings reset): default to models that fit this machine.
        // All registry tiers are CPU-only; no GPU is required for any pack.
        var registry = ModelRegistryService.Load();
        if (registry.Find(Settings.WhisperModelId) is not { KindEnum: ModelKind.WhisperModel })
            Settings.WhisperModelId = Hardware.Detect().RecommendedWhisperPackId;
        if (registry.Find(Settings.LlmModelId) is not { KindEnum: ModelKind.LlmModel })
            Settings.LlmModelId = Hardware.Detect().RecommendedLlmPackId;
        SaveSettings();

        var log = new Action<string>(msg => Serilog.Log.Information("pipeline: {Message}", msg));
        // Runtime executables are resolved lazily from installed registry packs, so a pack
        // installed via Model Manager is picked up without restarting the app.
        Audio = new FfmpegAudioExtractor(
            () => ResolveRuntimeExe("runtime-ffmpeg", null, Paths.FfmpegExe),
            () => ResolveRuntimeExe("runtime-ffmpeg", null, Paths.FfprobeExe),
            log);
        Transcriber = new WhisperTranscriber(
            () => ResolveRuntimeExe("runtime-whisper-cpu", null, Paths.WhisperCliExe),
            ResolveWhisperModelPath,
            ResolveVadModelPath,
            log);
        Llm = new LlamaLlmService(
            () => ResolveRuntimeExe("runtime-llama-cpu", null, Paths.LlamaServerExe),
            ResolveLlmModelPath,
            Settings,
            log);
        Tts = new TtsService(
            new WindowsTtsService(),
            new PiperTtsService(() => ResolveRuntimeExe("runtime-piper-tts", null, Paths.PiperExe), ListPiperVoices, log),
            () => Settings.TtsVoice.StartsWith("piper:", StringComparison.OrdinalIgnoreCase));

        Processor = new MeetingProcessor(Repository, Audio, Transcriber, Llm, Tts, Paths.MeetingDirectory, Settings, log);
        Queue = new ProcessingQueue(Processor, log);
        Meetings = new MeetingService(Repository, Paths.MeetingDirectory);
        Search = new SearchService(Repository, Repository);

        Serilog.Log.Information("MeetVault bootstrapped at {Root}", Paths.Root);
    }

    public void SaveSettings() => SettingsService.Save(Settings);

    public void Dispose()
    {
        Queue.Dispose();
        Llm.Dispose();
    }
}
