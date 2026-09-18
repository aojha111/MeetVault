namespace MeetVault.Core;

/// <summary>Application settings persisted to config/appsettings.json.</summary>
public sealed class AppSettings
{
    public string LibraryRoot { get; set; } = string.Empty;

    public string ModelsRoot { get; set; } = string.Empty;

    /// <summary>ISO 639-1 language hint for transcription, "auto" for auto-detect.</summary>
    public string TranscriptionLanguage { get; set; } = "auto";

    /// <summary>Whisper model id used for transcription.</summary>
    public string WhisperModelId { get; set; } = "whisper-tiny-multilingual";

    /// <summary>LLM model id used for analysis and brief generation.</summary>
    public string LlmModelId { get; set; } = "qwen3-4b-instruct-q4km";

    /// <summary>TTS voice id: "sapi:<voice name>" for built-in Windows voices, or a model id for Piper.</summary>
    public string TtsVoice { get; set; } = "sapi:default";

    /// <summary>Target audio brief duration in seconds (30–300).</summary>
    public int BriefDurationSeconds { get; set; } = 60;

    public double PlaybackSpeed { get; set; } = 1.0;

    /// <summary>Whether a newly imported meeting is processed automatically.</summary>
    public bool AutoProcessOnImport { get; set; } = true;

    /// <summary>LLM context size in tokens.</summary>
    public int LlmContextSize { get; set; } = 8192;

    /// <summary>Enable VAD for transcription.</summary>
    public bool EnableVad { get; set; } = true;

    /// <summary>When true, all non-essential network access is disabled.</summary>
    public bool OfflineMode { get; set; } = false;

    /// <summary>UI theme: "system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>Days to keep intermediate artifacts (audio WAV); 0 keeps everything.</summary>
    public int CleanupDays { get; set; } = 0;
}
