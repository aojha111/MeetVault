namespace MeetVault.Core;

/// <summary>Extracts and normalizes 16 kHz mono 16-bit PCM WAV audio from any supported media file.</summary>
public interface IAudioExtractor
{
    /// <summary>Extracts normalized audio to <paramref name="outputWavPath"/>. Throws on failure.</summary>
    Task ExtractWavAsync(string inputPath, string outputWavPath, CancellationToken cancellationToken = default);

    /// <summary>Reads duration and basic metadata for a media file.</summary>
    Task<MediaInfo> GetMediaInfoAsync(string inputPath, CancellationToken cancellationToken = default);

    /// <summary>True when the underlying tool is available on this machine.</summary>
    bool IsAvailable { get; }
}

/// <summary>Local speech-to-text (whisper.cpp) producing timestamped segments.</summary>
public interface ITranscriptionService
{
    bool IsAvailable { get; }

    /// <summary>
    /// Transcribes a 16 kHz mono WAV file into timestamped segments.
    /// Progress values are in [0,1].
    /// </summary>
    Task<List<TranscriptSegment>> TranscribeAsync(
        string wavPath,
        string language,
        bool enableVad,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default);
}

/// <summary>Local LLM (llama.cpp / GGUF) used for structured analysis and brief scripts.</summary>
public interface ILlmService
{
    bool IsAvailable { get; }

    /// <summary>True when the LLM supports enough context to accept the given character count.</summary>
    bool CanFit(string text);

    /// <summary>One-shot chat completion. When <paramref name="jsonSchema"/> is given the model is
    /// constrained to emit JSON matching that schema (llama.cpp grammar support).</summary>
    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        string? jsonSchema,
        double temperature = 0.2,
        int maxTokens = 2048,
        CancellationToken cancellationToken = default);

    /// <summary>Starts the backing local server if it is not running (no-op for in-process runtimes).</summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the backing local server, if one was started.</summary>
    void Shutdown();
}

/// <summary>Local offline text-to-speech for meeting briefs.</summary>
public interface ITtsService
{
    bool IsAvailable { get; }

    /// <summary>Voice identifiers usable with <see cref="SynthesizeToWavAsync"/> (e.g. "sapi:Microsoft Zira", "piper:en_US-amy-medium").</summary>
    IReadOnlyList<string> ListVoices();

    /// <summary>Synthesizes text to a mono WAV file.</summary>
    Task SynthesizeToWavAsync(string text, string voiceId, string outputWavPath, CancellationToken cancellationToken = default);
}
