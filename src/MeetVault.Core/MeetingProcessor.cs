namespace MeetVault.Core;

/// <summary>
/// Orchestrates the local meeting processing pipeline:
/// extract audio → transcribe → segment → analyze → brief script → TTS audio.
/// Each stage result is cached on disk and marked in the database so a failed later stage
/// can be retried without repeating successful earlier stages.
/// </summary>
public sealed partial class MeetingProcessor
{
    private readonly IMeetingRepository _meetings;
    private readonly IAudioExtractor _audio;
    private readonly ITranscriptionService _transcriber;
    private readonly ILlmService _llm;
    private readonly ITtsService _tts;
    private readonly Func<long, DateOnly, string> _meetingDir;
    private readonly AppSettings _settings;
    private readonly Action<string> _log;

    public MeetingProcessor(
        IMeetingRepository meetings,
        IAudioExtractor audio,
        ITranscriptionService transcriber,
        ILlmService llm,
        ITtsService tts,
        Func<long, DateOnly, string> meetingDir,
        AppSettings settings,
        Action<string>? log = null)
    {
        _meetings = meetings;
        _audio = audio;
        _transcriber = transcriber;
        _llm = llm;
        _tts = tts;
        _meetingDir = meetingDir;
        _settings = settings;
        _log = log ?? (_ => { });
    }

    public event EventHandler<MeetingProgress>? Progress;
}
