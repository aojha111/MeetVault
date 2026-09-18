using System.Text;

namespace MeetVault.Core;

public sealed partial class MeetingProcessor
{
    private void Report(long meetingId, string stage, double percent, string message, ProcessingStatus status, string? error = null) =>
        Progress?.Invoke(this, new MeetingProgress
        {
            MeetingId = meetingId, Stage = stage, Percent = percent, Message = message, Status = status, Error = error,
        });

    private async Task SetStatusAsync(Meeting meeting, ProcessingStatus status, CancellationToken ct)
    {
        meeting.Status = status;
        meeting.UpdatedAt = DateTimeOffset.Now;
        await _meetings.UpdateAsync(meeting, ct).ConfigureAwait(false);
    }

    /// <summary>Runs one pipeline stage with caching, tool availability checks and error wrapping.</summary>
    private async Task RunStageAsync(
        Meeting meeting,
        PipelineStage stage,
        bool force,
        CancellationToken ct,
        string stageName,
        bool requiredToolAvailable,
        string missingToolMessage,
        Func<bool> artifactExists,
        Func<CancellationToken, Task> runAsync)
    {
        var done = meeting.CompletedStage >= stage;
        if (!force && done && artifactExists())
        {
            _log($"[{meeting.Id}] stage '{stageName}' cached, skipping.");
            return;
        }

        Report(meeting.Id, stageName, -1, $"{stageName}…", ProcessingStatus.Processing);
        if (!requiredToolAvailable)
            throw new PipelineStageException(stage, missingToolMessage, recoverable: true);

        try
        {
            await runAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PipelineStageException(stage, $"{stageName} failed: {ex.Message}", recoverable: true, inner: ex);
        }

        if (meeting.CompletedStage < stage)
        {
            meeting.CompletedStage = stage;
            meeting.UpdatedAt = DateTimeOffset.Now;
            await _meetings.UpdateAsync(meeting, ct).ConfigureAwait(false);
        }
    }

    private async Task PersistTranscriptAsync(
        Meeting meeting,
        IReadOnlyList<TranscriptSegment> segments,
        string jsonPath,
        string txtPath,
        CancellationToken ct)
    {
        meeting.TranscriptFilePath = jsonPath;
        await _meetings.ReplaceTranscriptAsync(meeting.Id, segments, jsonPath, ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            sb.Append('[').Append(TimecodeFormatter.FormatMs(seg.StartMs))
              .Append(" - ").Append(TimecodeFormatter.FormatMs(seg.EndMs)).Append(']');
            if (!string.IsNullOrEmpty(seg.Speaker))
                sb.Append(' ').Append(seg.Speaker).Append(':');
            sb.AppendLine();
            sb.AppendLine(seg.Text.Trim());
            sb.AppendLine();
        }
        await File.WriteAllTextAsync(txtPath, sb.ToString(), ct).ConfigureAwait(false);

        // Persist the machine-readable transcript too: it is the cached artifact for the
        // Transcribed stage and is re-read when a later stage is retried.
        var json = JsonUtil.ToPrettyJson(segments);
        await File.WriteAllTextAsync(jsonPath, json, ct).ConfigureAwait(false);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* best effort */ }
    }

    private string ResolveVoice()
    {
        if (string.IsNullOrWhiteSpace(_settings.TtsVoice) || _settings.TtsVoice == "sapi:default")
            return "sapi:default";
        if (_tts.ListVoices().Contains(_settings.TtsVoice))
            return _settings.TtsVoice;
        return "sapi:default";
    }
}
