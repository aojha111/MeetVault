namespace MeetVault.Core;

/// <summary>Minimal description of an installed or available AI model/runtime pack.</summary>
public sealed class ModelInfo
{
    /// <summary>Stable registry id, e.g. "whisper-tiny-multilingual".</summary>
    public required string Id { get; set; }

    /// <summary>Human-facing display name.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Functional category of the model/pack.</summary>
    public required ModelKind Kind { get; set; }

    public required string Version { get; set; }

    /// <summary>Short description shown in the Model Manager.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Download size in bytes of the archive/file.</summary>
    public long DownloadSizeBytes { get; set; }

    /// <summary>Minimum RAM in GB recommended to run this model.</summary>
    public double MinRamGb { get; set; }

    public string License { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;
}

/// <summary>Functional category of a downloadable pack.</summary>
public enum ModelKind
{
    /// <summary>Speech-to-text model weights (whisper.cpp GGML).</summary>
    WhisperModel,

    /// <summary>Local LLM weights (GGUF).</summary>
    LlmModel,

    /// <summary>Neural TTS voice model (Piper ONNX).</summary>
    TtsVoice,

    /// <summary>Executable runtime pack (FFmpeg, whisper.cpp, llama.cpp, Piper).</summary>
    Runtime,
}
