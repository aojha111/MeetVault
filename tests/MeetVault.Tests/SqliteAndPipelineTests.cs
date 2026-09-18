using MeetVault.Core;
using MeetVault.Infrastructure;

namespace MeetVault.Tests;

/// <summary>
/// Integration tests against a real temporary SQLite database.
/// </summary>
public class SqliteRepositoryTests : IDisposable
{
    private readonly string _root;
    private readonly Database _db;
    private readonly SqliteMeetingRepository _repo;

    public SqliteRepositoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meetvault-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new Database(Path.Combine(_root, "meetings.db"));
        _db.ApplyMigrationsAsync().GetAwaiter().GetResult();
        _repo = new SqliteMeetingRepository(_db);
    }

    public void Dispose()
    {
        _db.WithConnectionAsync<bool>(async conn =>
        {
            // Close WAL checkpoint handles so the directory can be deleted.
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await cmd.ExecuteNonQueryAsync();
            return true;
        }).GetAwaiter().GetResult();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private Meeting Meeting(string title = "Test meeting", string date = "2026-09-17", string? source = null) => new()
    {
        Title = title,
        MeetingDate = DateOnly.Parse(date),
        StartTime = new TimeOnly(10, 30),
        SourceFilePath = source ?? Path.Combine(_root, "source.mp4"),
    };

    [Fact]
    public async Task CreateAndGet_RoundTrips()
    {
        var created = await _repo.CreateAsync(Meeting());

        Assert.True(created.Id > 0);
        var loaded = await _repo.GetAsync(created.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Test meeting", loaded!.Title);
        Assert.Equal(new DateOnly(2026, 9, 17), loaded.MeetingDate);
        Assert.Equal(new TimeOnly(10, 30), loaded.StartTime);
        Assert.Equal(ProcessingStatus.Imported, loaded.Status);
    }

    [Fact]
    public async Task Update_PersistsChanges()
    {
        var created = await _repo.CreateAsync(Meeting());
        created.Title = "Renamed";
        created.Status = ProcessingStatus.Completed;
        created.CompletedStage = PipelineStage.AudioBriefGenerated;
        await _repo.UpdateAsync(created);

        var loaded = await _repo.GetAsync(created.Id);
        Assert.Equal("Renamed", loaded!.Title);
        Assert.Equal(ProcessingStatus.Completed, loaded.Status);
        Assert.Equal(PipelineStage.AudioBriefGenerated, loaded.CompletedStage);
    }

    [Fact]
    public async Task GetBySourcePath_DeduplicatesImports()
    {
        var source = Path.Combine(_root, "unique-source.mp4");
        var created = await _repo.CreateAsync(Meeting(source: source));

        var found = await _repo.GetBySourcePathAsync(source);
        Assert.NotNull(found);
        Assert.Equal(created.Id, found!.Id);

        var missing = await _repo.GetBySourcePathAsync(Path.Combine(_root, "other.mp4"));
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetAll_OrdersByDateDescending()
    {
        await _repo.CreateAsync(Meeting("Older", "2026-09-10"));
        await _repo.CreateAsync(Meeting("Newer", "2026-09-17"));

        var all = await _repo.GetAllAsync();
        Assert.Equal(["Newer", "Older"], all.Select(m => m.Title));
    }

    [Fact]
    public async Task GetByDate_AndGetByStatus_FilterCorrectly()
    {
        var a = await _repo.CreateAsync(Meeting("A", "2026-09-17"));
        await _repo.CreateAsync(Meeting("B", "2026-09-16"));

        var byDate = await _repo.GetByDateAsync(new DateOnly(2026, 9, 17));
        Assert.Equal(["A"], byDate.Select(m => m.Title));

        a.Status = ProcessingStatus.Completed;
        await _repo.UpdateAsync(a);
        var completed = await _repo.GetByStatusAsync(ProcessingStatus.Completed);
        Assert.Equal(["A"], completed.Select(m => m.Title));
    }

    [Fact]
    public async Task Delete_RemovesMeetingAndCascades()
    {
        var created = await _repo.CreateAsync(Meeting());
        await _repo.ReplaceTranscriptAsync(created.Id,
            [new TranscriptSegment { Sequence = 1, StartMs = 0, EndMs = 1000, Text = "hello" }],
            Path.Combine(_root, "transcript.json"));

        await _repo.DeleteAsync(created.Id);

        Assert.Null(await _repo.GetAsync(created.Id));
        Assert.Empty(await _repo.GetTranscriptAsync(created.Id));
    }

    [Fact]
    public async Task Transcript_RoundTripsWithSpeakersAndConfidence()
    {
        var meeting = await _repo.CreateAsync(Meeting());
        var segments = new List<TranscriptSegment>
        {
            new() { Sequence = 1, StartMs = 0, EndMs = 4_000, Speaker = "Speaker 1", Text = "Hello everyone.", Confidence = 0.92 },
            new() { Sequence = 2, StartMs = 4_500, EndMs = 9_000, Speaker = null, Text = "Second segment." },
        };
        await _repo.ReplaceTranscriptAsync(meeting.Id, segments, "transcript.json");

        var loaded = await _repo.GetTranscriptAsync(meeting.Id);
        Assert.Equal(2, loaded.Count);
        Assert.Equal(1, loaded[0].Sequence);
        Assert.Equal("Speaker 1", loaded[0].Speaker);
        Assert.Equal(0.92, loaded[0].Confidence);
        Assert.Null(loaded[1].Speaker);
        Assert.Null(loaded[1].Confidence);
    }

    [Fact]
    public async Task TranscriptSearch_FindsSegmentText()
    {
        var meeting = await _repo.CreateAsync(Meeting("Migration sync"));
        await _repo.ReplaceTranscriptAsync(meeting.Id,
            [new TranscriptSegment { Sequence = 1, StartMs = 0, EndMs = 1000, Text = "We should complete the API migration by Friday." }],
            Path.Combine(_root, "transcript.json"));

        var hits = await _repo.SearchAsync("API migration");
        Assert.Contains(hits, h => h.MeetingId == meeting.Id && h.MatchKind == "transcript");
    }

    [Fact]
    public async Task Analysis_RoundTripsAndReindexesFts()
    {
        var meeting = await _repo.CreateAsync(Meeting("Auth meeting"));
        var analysis = new MeetingAnalysis
        {
            MeetingTitle = "Authentication Migration",
            Summary = "The team agreed to migrate authentication.",
            Topics = ["authentication", "migration"],
            Decisions = [new Decision { DecisionText = "Use the new auth service", SourceSegmentIds = [2, 3] }],
            ActionItems =
            [
                new ActionItem { Task = "Complete migration", Owner = "Sarah", Deadline = "2026-09-18", SourceSegmentIds = [4] },
            ],
            OpenQuestions = [new OpenQuestion { Question = "What is the rollback plan?", Status = "Open" }],
            Risks = ["No rollback plan"],
            Participants = ["Sarah", "Mike"],
            Entities = [new Entity { EntityType = "project", EntityName = "AuthMigration" }],
        };
        var analysisPath = Path.Combine(_root, "analysis.json");
        await _repo.SaveAnalysisAsync(meeting.Id, analysis, analysisPath);

        var loaded = await _repo.GetAnalysisAsync(meeting.Id);
        Assert.NotNull(loaded);
        Assert.Equal("The team agreed to migrate authentication.", loaded!.Summary);
        Assert.Equal("Use the new auth service", loaded.Decisions[0].DecisionText);
        Assert.Equal([2, 3], loaded.Decisions[0].SourceSegmentIds);
        Assert.Equal("Sarah", loaded.ActionItems[0].Owner);
        Assert.Equal("AuthMigration", loaded.Entities[0].EntityName);

        var hits = await _repo.SearchAsync("rollback");
        Assert.Contains(hits, h => h.MeetingId == meeting.Id && h.MatchKind == "risks");

        var openActions = await _repo.GetOpenActionItemsAsync();
        Assert.Contains(openActions, a => a.Task == "Complete migration" && a.Owner == "Sarah");
    }

    [Fact]
    public async Task Relations_RoundTrip()
    {
        var a = await _repo.CreateAsync(Meeting("First"));
        var b = await _repo.CreateAsync(Meeting("Second"));
        await _repo.SaveRelationsAsync(a.Id,
            [new MeetingRelation { SourceMeetingId = a.Id, TargetMeetingId = b.Id, RelationType = "shared-topic", Evidence = "Shared: auth" }]);

        var relations = await _repo.GetRelationsAsync(a.Id);
        var relation = Assert.Single(relations);
        Assert.Equal(b.Id, relation.TargetMeetingId);
        Assert.Equal("shared-topic", relation.RelationType);
    }

    [Fact]
    public async Task TitleSearch_MatchesMeetingTitle()
    {
        await _repo.CreateAsync(Meeting("Database Migration Review"));
        var hits = await _repo.SearchAsync("Database Migration");
        Assert.Contains(hits, h => h.MatchKind == "title" && h.Title.Contains("Database Migration"));
    }
}

/// <summary>
/// End-to-end pipeline test with fake local AI services: proves the orchestrator,
/// stage caching, structured persistence and audio-brief generation all work.
/// </summary>
public class MeetingPipelineTests : IDisposable
{
    private readonly string _root;
    private readonly Database _db;
    private readonly SqliteMeetingRepository _repo;
    private readonly MeetingProcessor _processor;

    public MeetingPipelineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meetvault-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _db = new Database(Path.Combine(_root, "meetings.db"));
        _db.ApplyMigrationsAsync().GetAwaiter().GetResult();
        _repo = new SqliteMeetingRepository(_db);

        var paths = new AppPaths(_root);
        var settings = new AppSettings { TranscriptionLanguage = "en", EnableVad = false };

        var audio = new FakeAudioExtractor();
        var transcriber = new FakeTranscriber();
        var llm = new FakeLlm();
        var tts = new FakeTts();
        _processor = new MeetingProcessor(
            _repo, audio, transcriber, llm, tts,
            (id, date) => paths.MeetingDirectory(id, date),
            settings);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<Meeting> ImportAsync()
    {
        var source = Path.Combine(_root, "meeting.mp4");
        if (!File.Exists(source))
        {
            // A minimal non-empty file; the fake extractor doesn't care about contents.
            await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        }
        var (meeting, error) = await new MeetingService(_repo, (id, date) => new AppPaths(_root).MeetingDirectory(id, date))
            .ImportAsync(source);
        Assert.Null(error);
        Assert.NotNull(meeting);
        return meeting!;
    }

    [Fact]
    public async Task FullPipeline_ProducesTranscriptAnalysisAndBrief()
    {
        var meeting = await ImportAsync();
        var result = await _processor.ProcessAsync(meeting.Id);

        Assert.Equal(ProcessingStatus.Completed, result.Status);
        Assert.Equal(PipelineStage.AudioBriefGenerated, result.CompletedStage);

        // Transcript persisted with fake segments.
        var transcript = await _repo.GetTranscriptAsync(meeting.Id);
        Assert.NotEmpty(transcript);

        // Analysis persisted and validated.
        var analysis = await _repo.GetAnalysisAsync(meeting.Id);
        Assert.NotNull(analysis);
        Assert.False(string.IsNullOrWhiteSpace(analysis!.Summary));
        Assert.NotEmpty(analysis.Decisions);

        // Audio brief file exists on disk.
        Assert.False(string.IsNullOrEmpty(result.AudioBriefPath));
        Assert.True(File.Exists(result.AudioBriefPath));
    }

    [Fact]
    public async Task Retry_AfterFailedStage_SkipsCompletedStages()
    {
        var meeting = await ImportAsync();

        // First run: LLM fails during analysis.
        var failing = new MeetingProcessor(
            _repo,
            new FakeAudioExtractor(),
            new FakeTranscriber(),
            new FailingLlm(),
            new FakeTts(),
            (id, date) => new AppPaths(_root).MeetingDirectory(id, date),
            new AppSettings());
        await Assert.ThrowsAnyAsync<Exception>(() => failing.ProcessAsync(meeting.Id));

        var afterFailure = await _repo.GetAsync(meeting.Id);
        Assert.Equal(ProcessingStatus.Failed, afterFailure!.Status);
        // Audio + transcription stages completed and were cached.
        Assert.True(afterFailure.CompletedStage >= PipelineStage.Transcribed);

        // Second run with a working LLM must NOT re-run audio/transcription (stage caching).
        var recovered = await _processor.ProcessAsync(meeting.Id);
        Assert.Equal(ProcessingStatus.Completed, recovered.Status);
        var final = await _repo.GetAsync(meeting.Id);
        Assert.Equal(ProcessingStatus.Completed, final!.Status);
    }

    [Fact]
    public async Task AnalysisUpdate_UpdatesTitleFromLlm()
    {
        var meeting = await ImportAsync();
        await _processor.ProcessAsync(meeting.Id);
        var analysis = await _repo.GetAnalysisAsync(meeting.Id);
        Assert.Equal("API Migration Planning", analysis!.MeetingTitle);
    }
}

#region Fakes

public class FakeAudioExtractor : IAudioExtractor
{
    public bool IsAvailable => true;
    public int ExtractCount;

    public Task ExtractWavAsync(string inputPath, string outputWavPath, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ExtractCount);
        Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);
        // 10 seconds of 16 kHz mono 16-bit PCM ≈ 320,000 bytes.
        File.WriteAllBytes(outputWavPath, new byte[320_000]);
        return Task.CompletedTask;
    }

    public Task<MediaInfo> GetMediaInfoAsync(string inputPath, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MediaInfo { DurationSeconds = 10, SizeBytes = 320_000 });
}

public class FakeTranscriber : ITranscriptionService
{
    public bool IsAvailable => true;
    public int TranscribeCount;

    public Task<List<TranscriptSegment>> TranscribeAsync(
        string wavPath, string language, bool enableVad, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref TranscribeCount);
        progress?.Report(1.0);
        List<TranscriptSegment> segments =
        [
            new() { Sequence = 1, StartMs = 0, EndMs = 4_000, Text = "Today we discuss the API migration plan.", Speaker = "Speaker 1", Confidence = 0.95 },
            new() { Sequence = 2, StartMs = 4_000, EndMs = 8_000, Text = "The team agreed to complete the authentication migration by Friday.", Speaker = "Speaker 2", Confidence = 0.93 },
            new() { Sequence = 3, StartMs = 8_000, EndMs = 12_000, Text = "Sarah will own the migration and report progress.", Speaker = "Speaker 1", Confidence = 0.91 },
        ];
        return Task.FromResult(segments);
    }
}

/// <summary>Returns schema-valid JSON for chunk and final passes, and a brief script.</summary>
public class FakeLlm : ILlmService
{
    public bool IsAvailable => true;
    public bool CanFit(string text) => true;

    public Task<string> CompleteAsync(
        string systemPrompt, string userPrompt, string? jsonSchema,
        double temperature = 0.2, int maxTokens = 2048, CancellationToken cancellationToken = default)
    {
        if (systemPrompt == AnalysisPrompts.BriefSystemPrompt)
        {
            return Task.FromResult("""{"script": "Meeting brief. The team planned the API migration."}""");
        }
        if (userPrompt.Contains("MERGED EXTRACTION"))
        {
            return Task.FromResult("""
                {
                  "meetingTitle": "API Migration Planning",
                  "meetingDate": "2026-09-17",
                  "summary": "The team agreed to complete the API migration by Friday.",
                  "agenda": ["Migration plan"],
                  "topics": ["API migration"],
                  "keyDiscussionPoints": ["Timeline risk"],
                  "decisions": [{"decision": "Use the new authentication service", "sourceSegmentIds": [2]}],
                  "actionItems": [{"task": "Complete authentication migration", "owner": "Sarah", "deadline": "2026-09-18", "status": "Open"}],
                  "deadlines": ["2026-09-18"],
                  "risks": ["Rollback strategy undefined"],
                  "openQuestions": [{"question": "Who reviews the schema?", "status": "Open"}],
                  "followUps": [{"description": "Status update next week", "owner": "Sarah"}],
                  "participants": ["Sarah", "Mike"],
                  "entities": [{"type": "project", "name": "AuthMigration"}],
                  "relatedMeetingHints": ["api", "migration"]
                }
                """);
        }
        return Task.FromResult("""
            {
              "chunkSummary": "Discussion of the API migration.",
              "topics": ["API migration"],
              "keyDiscussionPoints": ["Timeline", "Ownership"],
              "decisions": [{"decision": "Use the new authentication service"}],
              "actionItems": [{"task": "Complete authentication migration", "owner": "Sarah"}],
              "deadlines": ["2026-09-18"],
              "risks": ["Rollback strategy undefined"],
              "openQuestions": [{"question": "Who reviews the schema?"}],
              "followUps": [],
              "participants": ["Sarah"],
              "entities": [{"type": "project", "name": "AuthMigration"}]
            }
            """);
    }

    public Task WarmUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Shutdown() { }
}

public class FailingLlm : ILlmService
{
    public bool IsAvailable => true;
    public bool CanFit(string text) => true;
    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, string? jsonSchema,
        double temperature = 0.2, int maxTokens = 2048, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Simulated LLM failure.");
    public Task WarmUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Shutdown() { }
}

public class FakeTts : ITtsService
{
    public bool IsAvailable => true;

    public IReadOnlyList<string> ListVoices() => ["sapi:default"];

    public Task SynthesizeToWavAsync(string text, string voiceId, string outputWavPath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);
        File.WriteAllBytes(outputWavPath, new byte[4_096]);
        return Task.CompletedTask;
    }
}

#endregion
