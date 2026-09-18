namespace MeetVault.Core;

public sealed partial class MeetingProcessor
{
    /// <summary>Runs the full pipeline; part 1 (audio, transcription, analysis). Continues in ProcessPart2.</summary>
    public async Task<Meeting> ProcessAsync(long meetingId, bool force = false, CancellationToken ct = default)
    {
        var meeting = await _meetings.GetAsync(meetingId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Meeting {meetingId} not found.");

        var dir = _meetingDir(meeting.Id, meeting.MeetingDate);
        Directory.CreateDirectory(dir);

        await SetStatusAsync(meeting, ProcessingStatus.Processing, ct).ConfigureAwait(false);

        try
        {
            var wavPath = Path.Combine(dir, "audio.wav");
            await RunStageAsync(meeting, PipelineStage.AudioExtracted, force, ct,
                stageName: "Extracting audio",
                requiredToolAvailable: _audio.IsAvailable,
                missingToolMessage: "FFmpeg runtime is not installed. Install it from Model Manager → Runtimes.",
                artifactExists: () => File.Exists(wavPath) && new FileInfo(wavPath).Length > 44,
                runAsync: async token =>
                {
                    await _audio.ExtractWavAsync(meeting.SourceFilePath!, wavPath, token).ConfigureAwait(false);
                    meeting.AudioFilePath = wavPath;
                    meeting.DurationSeconds = new FileInfo(wavPath).Length / (16000 * 2); // 16 kHz mono s16
                }).ConfigureAwait(false);

            var transcriptJsonPath = Path.Combine(dir, "transcript.json");
            var transcriptTxtPath = Path.Combine(dir, "transcript.txt");
            List<TranscriptSegment> segments;
            var transcriptCached = !force && meeting.CompletedStage >= PipelineStage.Transcribed && File.Exists(transcriptJsonPath);
            if (transcriptCached)
            {
                segments = JsonUtil.ParseLenient<List<TranscriptSegment>>(
                    await File.ReadAllTextAsync(transcriptJsonPath, ct).ConfigureAwait(false)) ?? [];
                _log($"[{meeting.Id}] transcription cached ({segments.Count} segments).");
            }
            else
            {
                await RunStageAsync(meeting, PipelineStage.Transcribed, force, ct,
                    stageName: "Transcribing",
                    requiredToolAvailable: _transcriber.IsAvailable,
                    missingToolMessage: "No transcription model is installed. Install a Whisper model from Model Manager.",
                    artifactExists: () => File.Exists(transcriptJsonPath),
                    runAsync: async token =>
                    {
                        var progress = new Progress<double>(p =>
                            Report(meeting.Id, "Transcribing", p, "Transcribing…", ProcessingStatus.Processing));
                        var result = await _transcriber.TranscribeAsync(
                            wavPath, _settings.TranscriptionLanguage, _settings.EnableVad, progress, token).ConfigureAwait(false);
                        await PersistTranscriptAsync(meeting, result, transcriptJsonPath, transcriptTxtPath, token).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                segments = JsonUtil.ParseLenient<List<TranscriptSegment>>(
                    await File.ReadAllTextAsync(transcriptJsonPath, ct).ConfigureAwait(false)) ?? [];
            }

            await RunStageAsync(meeting, PipelineStage.Analyzed, force, ct,
                stageName: "Analyzing",
                requiredToolAvailable: _llm.IsAvailable,
                missingToolMessage: "No analysis LLM is installed. Install an LLM from Model Manager.",
                artifactExists: () => File.Exists(Path.Combine(dir, "analysis.json")),
                runAsync: token => AnalyzeAsync(meeting, segments, dir, token)).ConfigureAwait(false);

            var analysis = await _meetings.GetAnalysisAsync(meeting.Id, ct).ConfigureAwait(false)
                ?? throw new PipelineStageException(PipelineStage.Analyzed, "Analysis artifact missing after analysis stage.");

            return await ProcessPart2Async(meeting, analysis, dir, force, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            meeting.Status = ProcessingStatus.Canceled;
            meeting.UpdatedAt = DateTimeOffset.Now;
            await _meetings.UpdateAsync(meeting, ct).ConfigureAwait(false);
            Report(meeting.Id, "Canceled", -1, "Processing canceled.", ProcessingStatus.Canceled);
            throw;
        }
        catch (PipelineStageException ex)
        {
            await FailAsync(meeting, ex.Message).ConfigureAwait(false);
            Report(meeting.Id, ex.Stage.ToString(), -1, ex.Message, ProcessingStatus.Failed, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            await FailAsync(meeting, ex.Message).ConfigureAwait(false);
            Report(meeting.Id, "Processing", -1, ex.Message, ProcessingStatus.Failed, ex.Message);
            throw;
        }
    }

    private async Task FailAsync(Meeting meeting, string message)
    {
        meeting.Status = ProcessingStatus.Failed;
        meeting.LastError = message;
        meeting.UpdatedAt = DateTimeOffset.Now;
        await _meetings.UpdateAsync(meeting).ConfigureAwait(false);
    }
}
