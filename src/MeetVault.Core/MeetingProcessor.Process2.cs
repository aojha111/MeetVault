using System.Text;

namespace MeetVault.Core;

public sealed partial class MeetingProcessor
{
    /// <summary>Pipeline part 2: brief script + TTS audio brief, then completion.</summary>
    private async Task<Meeting> ProcessPart2Async(Meeting meeting, MeetingAnalysis analysis, string dir, bool force, CancellationToken ct)
    {
        var scriptPath = Path.Combine(dir, "brief-script.txt");
        await RunStageAsync(meeting, PipelineStage.BriefScriptGenerated, force, ct,
            stageName: "Generating meeting brief",
            requiredToolAvailable: true,
            missingToolMessage: string.Empty,
            artifactExists: () => File.Exists(scriptPath),
            runAsync: async token =>
            {
                var script = await GenerateBriefScriptAsync(meeting, analysis, token).ConfigureAwait(false);
                await File.WriteAllTextAsync(scriptPath, script, token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        await RunStageAsync(meeting, PipelineStage.AudioBriefGenerated, force, ct,
            stageName: "Generating audio",
            requiredToolAvailable: _tts.IsAvailable,
            missingToolMessage: "No TTS voice is available. Windows built-in voices are used automatically.",
            artifactExists: () => !string.IsNullOrEmpty(meeting.AudioBriefPath) && File.Exists(meeting.AudioBriefPath),
            runAsync: async token =>
            {
                var script = await File.ReadAllTextAsync(scriptPath, token).ConfigureAwait(false);
                var briefWav = Path.Combine(dir, "brief.wav");
                await _tts.SynthesizeToWavAsync(script, ResolveVoice(), briefWav, token).ConfigureAwait(false);

                var briefMp3 = Path.Combine(dir, "brief.mp3");
                string finalPath;
                if (_audio.IsAvailable && await TryEncodeMp3Async(briefWav, briefMp3, token).ConfigureAwait(false))
                {
                    finalPath = briefMp3;
                    TryDelete(briefWav);
                }
                else
                {
                    finalPath = briefWav;
                }
                meeting.AudioBriefPath = finalPath;
            }).ConfigureAwait(false);

        meeting.Status = ProcessingStatus.Completed;
        meeting.LastError = null;
        meeting.UpdatedAt = DateTimeOffset.Now;
        await _meetings.UpdateAsync(meeting, ct).ConfigureAwait(false);
        Report(meeting.Id, "Completed", 1, "Meeting processed.", ProcessingStatus.Completed);
        return meeting;
    }
}
