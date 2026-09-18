namespace MeetVault.Core;

/// <summary>A downloadable pack entry from the model registry (config/model-registry.json).</summary>
public sealed class ModelPack
{
    /// <summary>Stable id, e.g. "whisper-base-multilingual".</summary>
    public required string Id { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>Runtime | whisper-model | llm-model | tts-voice.</summary>
    public required string Kind { get; set; }

    public required string Version { get; set; }

    public string Description { get; set; } = string.Empty;

    public string License { get; set; } = string.Empty;

    /// <summary>Direct download URLs; tried in order.</summary>
    public required List<string> Urls { get; set; }

    /// <summary>Expected total download size in bytes (for progress and sanity checks).</summary>
    public long SizeBytes { get; set; }

    /// <summary>Expected SHA-256 of the downloaded file (hex, lowercase). Required.</summary>
    public required string Sha256 { get; set; }

    /// <summary>File name the download is stored under.</summary>
    public required string FileName { get; set; }

    /// <summary>When true the download is a zip archive extracted to the pack directory.</summary>
    public bool ExtractZip { get; set; }

    /// <summary>Subdirectory of the pack install dir where the zip is extracted ("" = pack dir).</summary>
    public string ExtractSubDirectory { get; set; } = string.Empty;

    /// <summary>After extraction, the binary the app must execute (relative to install dir), when known.</summary>
    public string? ExecutableRelativePath { get; set; }

    /// <summary>For model files (not archives): target subfolder under models/.</summary>
    public string TargetModelsSubDirectory { get; set; } = string.Empty;

    /// <summary>Minimum RAM (GB) recommended to run this model.</summary>
    public double MinRamGb { get; set; }

    /// <summary>Optional minimum free disk (GB) to install.</summary>
    public double MinFreeDiskGb { get; set; }

    /// <summary>Group label in the Model Manager UI (e.g. "Transcription", "Analysis LLM", "TTS", "Runtimes").</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Extra files (name + sha256) that must exist after install, e.g. Piper voice JSON.</summary>
    public List<ModelPackFile> ExtraFiles { get; set; } = [];

    public ModelKind KindEnum => Kind switch
    {
        "whisper-model" => ModelKind.WhisperModel,
        "llm-model" => ModelKind.LlmModel,
        "tts-voice" => ModelKind.TtsVoice,
        _ => ModelKind.Runtime,
    };
}

/// <summary>An additional file bundled with a pack (checked after extraction).</summary>
public sealed class ModelPackFile
{
    /// <summary>Path relative to the pack install directory.</summary>
    public required string RelativePath { get; set; }

    /// <summary>Where to obtain this file if it is a separate download; empty when contained in the archive.</summary>
    public string? Url { get; set; }

    /// <summary>Expected SHA-256 when separately downloaded.</summary>
    public string? Sha256 { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>File name used for the download when <see cref="Url"/> is set.</summary>
    public string? DownloadFileName { get; set; }
}

/// <summary>Structured, validated view of the model registry file.</summary>
public sealed class ModelRegistry
{
    public required List<ModelPack> Packs { get; set; }

    public ModelPack? Find(string id) => Packs.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public List<ModelPack> ByKind(string kind) => Packs.Where(p => p.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)).ToList();
}
