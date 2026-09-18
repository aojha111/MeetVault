namespace MeetVault.Core;

public sealed partial class MeetingProcessor
{
    /// <summary>Chunked map → deterministic merge → final synthesis analysis pass.</summary>
    private async Task AnalyzeAsync(Meeting meeting, List<TranscriptSegment> segments, string dir, CancellationToken ct)
    {
        Report(meeting.Id, "Analyzing", 0, "Analyzing transcript with local LLM…", ProcessingStatus.Processing);
        var chunks = new TranscriptChunker().Chunk(segments);
        var extractions = new List<ChunkExtraction>();

        for (int i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = chunks[i];
            Report(meeting.Id, "Analyzing", (i + 0.5) / chunks.Count,
                $"Analyzing chunk {i + 1}/{chunks.Count}…", ProcessingStatus.Processing);

            var raw = await _llm.CompleteAsync(
                AnalysisPrompts.ChunkSystemPrompt,
                AnalysisPrompts.ChunkUserPrompt(chunk, meeting.MeetingDate),
                AnalysisSchemas.Chunk,
                temperature: 0.1,
                maxTokens: 2048,
                cancellationToken: ct).ConfigureAwait(false);

            var extraction = JsonUtil.ParseLenient<ChunkExtraction>(raw)
                ?? throw new PipelineStageException(PipelineStage.Analyzed, $"LLM returned invalid JSON for chunk {chunk.Index}.");
            extractions.Add(extraction);
        }

        Report(meeting.Id, "Analyzing", 0.9, "Merging chunk results…", ProcessingStatus.Processing);
        var merged = AnalysisMerger.Merge(extractions);

        var finalRaw = await _llm.CompleteAsync(
            AnalysisPrompts.FinalSystemPrompt,
            AnalysisPrompts.FinalUserPrompt(merged, meeting.MeetingDate, meeting.Title),
            AnalysisSchemas.Final,
            temperature: 0.2,
            maxTokens: 3000,
            cancellationToken: ct).ConfigureAwait(false);

        var final = JsonUtil.ParseLenient<MeetingAnalysis>(finalRaw)
            ?? throw new PipelineStageException(PipelineStage.Analyzed, "LLM returned invalid JSON for the final synthesis.");

        // Deterministic backfill for anything the synthesis dropped.
        if (final.Topics.Count == 0) final.Topics = merged.Topics;
        if (final.KeyDiscussionPoints.Count == 0) final.KeyDiscussionPoints = merged.KeyDiscussionPoints;
        if (final.Deadlines.Count == 0) final.Deadlines = merged.Deadlines;
        if (final.Risks.Count == 0) final.Risks = merged.Risks;
        if (final.Participants.Count == 0) final.Participants = merged.Participants;
        if (final.Entities.Count == 0) final.Entities = merged.Entities;
        if (final.RelatedMeetingHints.Count == 0) final.RelatedMeetingHints = merged.Topics.Take(5).ToList();
        if (!string.IsNullOrWhiteSpace(final.MeetingTitle))
            meeting.Title = final.MeetingTitle;

        var problems = JsonUtil.ValidateAnalysis(final);
        if (problems.Count > 0)
            throw new PipelineStageException(PipelineStage.Analyzed, "Analysis failed validation: " + string.Join("; ", problems));

        var analysisPath = Path.Combine(dir, "analysis.json");
        await _meetings.SaveAnalysisAsync(meeting.Id, final, analysisPath, ct).ConfigureAwait(false);
        // Keep the in-memory record in sync: the final UpdateAsync persists the whole
        // meeting object and must not wipe the analysis_file_path column.
        meeting.AnalysisFilePath = analysisPath;
        Report(meeting.Id, "Analyzing", 1, "Analysis complete.", ProcessingStatus.Processing);

        var relations = await BuildRelationsAsync(meeting, final, ct).ConfigureAwait(false);
        await _meetings.SaveRelationsAsync(meeting.Id, relations, ct).ConfigureAwait(false);
    }

    /// <summary>Deterministic cross-meeting relations from shared entities/topics.</summary>
    private async Task<List<MeetingRelation>> BuildRelationsAsync(Meeting meeting, MeetingAnalysis analysis, CancellationToken ct)
    {
        var relations = new List<MeetingRelation>();
        var all = await _meetings.GetAllAsync(ct).ConfigureAwait(false);
        var myKeys = KeysOf(analysis);
        foreach (var other in all)
        {
            if (other.Id == meeting.Id) continue;
            var otherAnalysis = await _meetings.GetAnalysisAsync(other.Id, ct).ConfigureAwait(false);
            if (otherAnalysis is null) continue;
            var shared = myKeys.Intersect(KeysOf(otherAnalysis), StringComparer.OrdinalIgnoreCase).Take(3).ToList();
            if (shared.Count > 0)
            {
                relations.Add(new MeetingRelation
                {
                    SourceMeetingId = meeting.Id,
                    TargetMeetingId = other.Id,
                    RelationType = "shared-topic",
                    Evidence = "Shared: " + string.Join(", ", shared),
                });
            }
        }
        return relations;
    }

    private static HashSet<string> KeysOf(MeetingAnalysis a) =>
        a.Entities.Select(e => e.EntityName.Trim())
            .Concat(a.Topics)
            .Where(s => s.Length > 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Encodes a WAV to MP3 via ffmpeg; false when encoding fails.</summary>
    private async Task<bool> TryEncodeMp3Async(string wavPath, string mp3Path, CancellationToken ct)
    {
        try
        {
            var tmp = mp3Path + ".tmp.mp3";
            await _audio.ExtractWavAsync(wavPath, tmp, ct).ConfigureAwait(false);
            if (File.Exists(tmp) && new FileInfo(tmp).Length > 0)
            {
                if (File.Exists(mp3Path)) File.Delete(mp3Path);
                File.Move(tmp, mp3Path);
                return true;
            }
        }
        catch (Exception ex)
        {
            _log($"MP3 encode failed, keeping WAV: {ex.Message}");
        }
        return false;
    }
}
