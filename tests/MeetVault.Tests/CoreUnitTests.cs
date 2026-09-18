using MeetVault.Core;

namespace MeetVault.Tests;

public class TimecodeFormatterTests
{
    [Theory]
    [InlineData(0L, "00:00")]
    [InlineData(5_000L, "00:05")]
    [InlineData(65_000L, "01:05")]
    [InlineData(3_600_000L, "01:00:00")]
    [InlineData(3_725_000L, "01:02:05")]
    [InlineData(-1L, "00:00")]
    public void FormatMs_ProducesExpectedOutput(long ms, string expected) =>
        Assert.Equal(expected, TimecodeFormatter.FormatMs(ms));

    [Fact]
    public void FormatMsLong_AlwaysShowsHours() =>
        Assert.Equal("00:01:05", TimecodeFormatter.FormatMsLong(65_000));

    [Theory]
    [InlineData("01:02:05", 3_725_000L)]
    [InlineData("01:05", 65_000L)]
    [InlineData("30.5", 30_500L)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("not-a-time", null)]
    public void ParseToMs_RoundTrips(string? input, long? expected) =>
        Assert.Equal(expected, TimecodeFormatter.ParseToMs(input));
}

public class TranscriptChunkerTests
{
    private static TranscriptSegment Seg(long startMs, long endMs, string text) =>
        new() { Sequence = 1, StartMs = startMs, EndMs = endMs, Text = text };

    [Fact]
    public void Chunk_EmptyTranscript_ReturnsNoChunks()
    {
        var chunks = new TranscriptChunker().Chunk([]);
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_RespectsWordLimit()
    {
        // Many segments over the limit → multiple chunks, each within the word budget
        // (timecode prefixes count toward the chunk text).
        var segments = Enumerable.Range(0, 12)
            .Select(i => Seg(i * 1_000L, (i + 1) * 1_000L, string.Join(' ', Enumerable.Repeat($"word{i}", 10))))
            .ToList();
        var chunks = new TranscriptChunker(maxWordsPerChunk: 60).Chunk(segments);
        Assert.True(chunks.Count > 1, "expected the transcript to be split into multiple chunks");
        Assert.All(chunks, c => Assert.True(
            TranscriptChunker.CountWords(c.Text) <= 60 + c.SegmentCount,
            "chunk text (including per-segment timecode tokens) must stay within the word budget"));
        // Chunks cover the transcript in order without gaps.
        Assert.Equal(segments[0].StartMs, chunks[0].StartMs);
        Assert.Equal(segments[^1].EndMs, chunks[^1].EndMs);
    }

    [Fact]
    public void Chunk_FloorsWordLimitAt50()
    {
        // The chunker enforces a minimum chunk size to avoid degenerate LLM calls.
        var segments = Enumerable.Range(0, 5)
            .Select(i => Seg(i * 1_000L, (i + 1) * 1_000L, string.Join(' ', Enumerable.Repeat($"word{i}", 10))))
            .ToList();
        var chunks = new TranscriptChunker(maxWordsPerChunk: 10).Chunk(segments);
        Assert.Single(chunks); // 50 words fit in one chunk even with a requested limit of 10.
    }

    [Fact]
    public void Chunk_RespectsTimeSpanLimit()
    {
        // Segments 10 minutes apart each, 300s limit → one chunk per segment.
        var segments = Enumerable.Range(0, 3)
            .Select(i => Seg(i * 600_000L, i * 600_000L + 1_000L, "hello world"))
            .ToList();
        var chunks = new TranscriptChunker(maxSecondsPerChunk: 300).Chunk(segments);
        Assert.Equal(3, chunks.Count);
    }

    [Fact]
    public void Chunk_KeepsChunksInOrderWithTimecodes()
    {
        var segments = new List<TranscriptSegment>
        {
            Seg(0, 2_000, "first"),
            Seg(2_000, 4_000, "second"),
        };
        var chunks = new TranscriptChunker().Chunk(segments);
        var chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.StartMs);
        Assert.Equal(4_000, chunk.EndMs);
        Assert.Equal(2, chunk.SegmentCount);
        Assert.Contains("[00:00] first", chunk.Text);
        Assert.Contains("[00:02] second", chunk.Text);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("one two three", 3)]
    public void CountWords_CountsWords(string text, int expected) =>
        Assert.Equal(expected, TranscriptChunker.CountWords(text));
}

public class AnalysisMergerTests
{
    private static ChunkExtraction Extraction(
        List<string>? topics = null,
        List<Decision>? decisions = null,
        List<ActionItem>? actions = null) => new()
    {
        Topics = topics ?? [],
        Decisions = decisions ?? [],
        ActionItems = actions ?? [],
    };

    [Fact]
    public void Merge_CombinesAllChunks()
    {
        var merged = AnalysisMerger.Merge(
        [
            Extraction(topics: ["API migration"]),
            Extraction(topics: ["Database schema"]),
        ]);
        Assert.Equal(2, merged.Topics.Count);
    }

    [Fact]
    public void Merge_DeduplicatesNearIdenticalItems()
    {
        var merged = AnalysisMerger.Merge(
        [
            Extraction(decisions: [new Decision { DecisionText = "Complete the API migration by Friday." }]),
            Extraction(decisions: [new Decision { DecisionText = "Complete the API migration by Friday" }]),
            Extraction(decisions: [new Decision { DecisionText = "Completely different decision about servers" }]),
        ]);
        Assert.Equal(2, merged.Decisions.Count);
    }

    [Fact]
    public void Merge_DropsEmptyEntries()
    {
        var merged = AnalysisMerger.Merge(
        [
            new ChunkExtraction
            {
                Topics = ["", "  ", "Real Topic"],
                Decisions = [new Decision { DecisionText = " " }],
                ActionItems = [new ActionItem { Task = "" }],
            },
        ]);
        Assert.Equal(["Real Topic"], merged.Topics);
        Assert.Empty(merged.Decisions);
        Assert.Empty(merged.ActionItems);
    }
}

public class JsonUtilTests
{
    [Fact]
    public void ExtractJsonText_HandlesCodeFences()
    {
        var raw = "```json\n{\"script\": \"hello\"}\n```";
        Assert.Equal("{\"script\": \"hello\"}", JsonUtil.ExtractJsonText(raw));
    }

    [Fact]
    public void ExtractJsonText_HandlesSurroundingProse()
    {
        var raw = "Here is the analysis:\n{\"summary\": \"ok\"} hope this helps";
        Assert.Equal("{\"summary\": \"ok\"}", JsonUtil.ExtractJsonText(raw));
    }

    [Fact]
    public void ExtractJsonText_IgnoresBracesInsideStrings()
    {
        var raw = """{"text": "a { weird } string"}""";
        Assert.Equal(raw, JsonUtil.ExtractJsonText(raw));
    }

    [Fact]
    public void ParseLenient_RepairsTrailingCommas()
    {
        var result = JsonUtil.ParseLenient<MeetingAnalysis>("""{"summary": "s", "topics": ["a",],}""");
        Assert.NotNull(result);
        Assert.Equal("s", result!.Summary);
        Assert.Equal(["a"], result.Topics);
    }

    [Fact]
    public void ParseLenient_ParsesBriefEnvelope()
    {
        var brief = JsonUtil.ParseLenient<BriefScript>("Here you go: {\"script\": \"Meeting brief.\"}");
        Assert.Equal("Meeting brief.", brief!.Script);
    }

    [Fact]
    public void ValidateAnalysis_FlagsMissingSummary()
    {
        var problems = JsonUtil.ValidateAnalysis(new MeetingAnalysis { Summary = "" });
        Assert.Contains(problems, p => p.Contains("summary"));
    }

    [Fact]
    public void ValidateAnalysis_PassesValidAnalysis()
    {
        var analysis = new MeetingAnalysis
        {
            Summary = "ok",
            Decisions = [new Decision { DecisionText = "d" }],
            ActionItems = [new ActionItem { Task = "t" }],
        };
        Assert.Empty(JsonUtil.ValidateAnalysis(analysis));
    }
}

public class DateGroupingTests
{
    private static Meeting Meeting(DateOnly date, TimeOnly? start, string title) =>
        new() { Title = title, MeetingDate = date, StartTime = start };

    [Fact]
    public void BuildTree_GroupsByYearThenDay()
    {
        var tree = DateGroupNode.BuildTree(
        [
            Meeting(new DateOnly(2026, 9, 17), new TimeOnly(9, 0), "API Migration"),
            Meeting(new DateOnly(2026, 9, 17), new TimeOnly(14, 0), "Sprint Planning"),
            Meeting(new DateOnly(2026, 9, 16), null, "Architecture Review"),
            Meeting(new DateOnly(2025, 3, 2), null, "Older Meeting"),
        ]);

        Assert.Equal("All Meetings", tree.Label);
        Assert.Equal(["2026", "2025"], tree.Children.Select(c => c.Label));

        var year2026 = tree.Children[0];
        Assert.Equal(["September 17", "September 16"], year2026.Children.Select(c => c.Label));

        var sept17 = year2026.Children[0];
        // Later start time first.
        Assert.Equal(["Sprint Planning", "API Migration"], sept17.Children.Select(c => c.Label));
    }

    [Fact]
    public void RangeFilter_TodayAndYesterday()
    {
        var today = new DateOnly(2026, 9, 17);
        var meetings = new[]
        {
            Meeting(today, null, "Today"),
            Meeting(today.AddDays(-1), null, "Yesterday"),
            Meeting(today.AddDays(-2), null, "Older"),
        };

        Assert.Equal(["Today"], meetings.Where(DateGroupNode.RangeFilter("today", today)).Select(m => m.Title));
        Assert.Equal(["Yesterday"], meetings.Where(DateGroupNode.RangeFilter("yesterday", today)).Select(m => m.Title));
        Assert.Equal(3, meetings.Count(DateGroupNode.RangeFilter("all", today)));
    }

    [Fact]
    public void RangeFilter_ThisWeek_MondayThroughToday()
    {
        var wednesday = new DateOnly(2026, 9, 16); // Wednesday
        var meetings = new[]
        {
            Meeting(new DateOnly(2026, 9, 14), null, "Monday"),
            Meeting(new DateOnly(2026, 9, 13), null, "LastSunday"),
            Meeting(new DateOnly(2026, 9, 16), null, "Wednesday"),
        };
        Assert.Equal(["Monday", "Wednesday"], meetings.Where(DateGroupNode.RangeFilter("week", wednesday)).Select(m => m.Title));
    }

    [Fact]
    public void RangeFilter_ThisMonth()
    {
        var today = new DateOnly(2026, 9, 17);
        var meetings = new[]
        {
            Meeting(new DateOnly(2026, 9, 1), null, "InMonth"),
            Meeting(new DateOnly(2026, 8, 31), null, "PrevMonth"),
        };
        Assert.Equal(["InMonth"], meetings.Where(DateGroupNode.RangeFilter("month", today)).Select(m => m.Title));
    }
}

public class ImportValidatorTests : IDisposable
{
    private readonly string _dir;

    public ImportValidatorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "meetvault-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string CreateFile(string name, long sizeBytes = 100)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    [Fact]
    public void Validate_AcceptsSupportedMedia()
    {
        Assert.Null(ImportValidator.Validate(CreateFile("meeting.mp4")));
        Assert.Null(ImportValidator.Validate(CreateFile("meeting.MP4")));
        Assert.Null(ImportValidator.Validate(CreateFile("audio.mp3")));
    }

    [Fact]
    public void Validate_RejectsMissingFile() =>
        Assert.NotNull(ImportValidator.Validate(Path.Combine(_dir, "nope.mp4")));

    [Fact]
    public void Validate_RejectsUnsupportedExtension()
    {
        var error = ImportValidator.Validate(CreateFile("document.txt"));
        Assert.NotNull(error);
        Assert.Contains("Unsupported file type", error);
    }

    [Fact]
    public void Validate_RejectsEmptyFiles() =>
        Assert.Equal("File is empty.", ImportValidator.Validate(CreateFile("empty.mp4", 0)));

    [Fact]
    public void Validate_RejectsEmptyPath() =>
        Assert.NotNull(ImportValidator.Validate("   "));
}

public class MeetingMetadataDetectorTests : IDisposable
{
    private readonly string _dir;

    public MeetingMetadataDetectorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "meetvault-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Touch(string fileName)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    [Fact]
    public void Detect_ParsesDateAndTimeFromFileName()
    {
        var (date, time, _) = MeetingMetadataDetector.Detect(Touch("meet_abc123_2026-09-17_10-30.mp4"));
        Assert.Equal(new DateOnly(2026, 9, 17), date);
        Assert.Equal(new TimeOnly(10, 30), time);
    }

    [Fact]
    public void Detect_ParsesCompactTime()
    {
        var (date, time, _) = MeetingMetadataDetector.Detect(Touch("Recording 2026-09-17 1030.mp4"));
        Assert.Equal(new DateOnly(2026, 9, 17), date);
        Assert.Equal(new TimeOnly(10, 30), time);
    }

    [Fact]
    public void Detect_StripsPrefixesAndDatesFromTitle()
    {
        var (_, _, title) = MeetingMetadataDetector.Detect(Touch("Zoom Meeting 2026-09-17.mp4"));
        Assert.Equal("Meeting", title);
    }

    [Fact]
    public void Detect_KeepsDescriptiveTitle()
    {
        var (_, _, title) = MeetingMetadataDetector.Detect(Touch("Team Standup.mp4"));
        Assert.Equal("Team Standup", title);
    }

    [Fact]
    public void Detect_FallsBackToFileDate()
    {
        var (date, _, _) = MeetingMetadataDetector.Detect(Touch("untitled.mp4"));
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), date);
    }
}

public class BriefBuilderTests
{
    [Fact]
    public void BuildDeterministic_IncludesKeySections()
    {
        var analysis = new MeetingAnalysis
        {
            Summary = "The team planned the API migration.",
            Agenda = ["Migration plan", "Timeline"],
            Decisions = [new Decision { DecisionText = "Use the new authentication service" }],
            ActionItems =
            [
                new ActionItem { Task = "Complete authentication migration", Owner = "Sarah", Deadline = "2026-09-18" },
            ],
            Risks = ["Rollback strategy undefined"],
            OpenQuestions = [new OpenQuestion { Question = "Who reviews the schema?" }],
        };

        var script = BriefBuilder.BuildDeterministic(analysis, new DateOnly(2026, 9, 17));

        Assert.Contains("September 17, 2026", script);
        Assert.Contains("API migration", script);
        Assert.Contains("Use the new authentication service", script);
        Assert.Contains("owned by Sarah", script);
        Assert.Contains("due 2026-09-18", script);
        Assert.Contains("Rollback strategy", script);
        Assert.Contains("Who reviews the schema?", script);
    }

    [Fact]
    public void BuildDeterministic_HandlesEmptyAnalysis()
    {
        var script = BriefBuilder.BuildDeterministic(new MeetingAnalysis(), new DateOnly(2026, 9, 17));
        Assert.Contains("Meeting brief for September 17, 2026", script);
    }
}

public class SearchServiceTests
{
    [Theory]
    [InlineData("what was decided", true)]
    [InlineData("summarize the meeting", true)]
    [InlineData("authentication migration", false)]
    [InlineData("actions assigned to Sarah", false)]
    public void LooksLikeQuestion_DetectsQuestions(string query, bool expected) =>
        Assert.Equal(expected, SearchService.LooksLikeQuestion(query));
}

public class ModelRegistryValidationTests
{
    private static ModelPack Pack(string id, string sha = "") => new()
    {
        Id = id,
        DisplayName = id,
        Kind = "llm-model",
        Version = "1.0",
        Urls = ["https://example.invalid/" + id],
        Sha256 = sha,
        FileName = id + ".bin",
    };

    [Fact]
    public void Validate_AcceptsWellFormedRegistry()
    {
        var registry = new ModelRegistry { Packs = [Pack("a", new string('a', 64))] };
        Assert.Empty(ModelRegistryService.Validate(registry));
    }

    [Fact]
    public void Validate_FlagsDuplicateIds()
    {
        var registry = new ModelRegistry { Packs = [Pack("a", new string('a', 64)), Pack("a", new string('b', 64))] };
        Assert.Contains(ModelRegistryService.Validate(registry), p => p.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_FlagsBadChecksums()
    {
        var registry = new ModelRegistry { Packs = [Pack("a", "xyz")] };
        Assert.Contains(ModelRegistryService.Validate(registry), p => p.Contains("Sha256"));
    }

    [Fact]
    public void Validate_FlagsMissingUrlAndFileName()
    {
        var pack = Pack("a", new string('a', 64));
        pack.Urls = [];
        pack.FileName = "";
        var registry = new ModelRegistry { Packs = [pack] };
        var problems = ModelRegistryService.Validate(registry);
        Assert.Contains(problems, p => p.Contains("no download URL"));
        Assert.Contains(problems, p => p.Contains("no FileName"));
    }
}

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir;

    public SettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "meetvault-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Load_ReturnsDefaultsOnFirstRun()
    {
        var service = new SettingsService(_dir);
        var settings = service.Load();
        Assert.Equal("auto", settings.TranscriptionLanguage);
        Assert.Equal(60, settings.BriefDurationSeconds);
        Assert.True(settings.AutoProcessOnImport);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var service = new SettingsService(_dir);
        var settings = new AppSettings
        {
            TranscriptionLanguage = "en",
            BriefDurationSeconds = 90,
            Theme = "dark",
            OfflineMode = true,
        };
        service.Save(settings);

        var reloaded = new SettingsService(_dir).Load();
        Assert.Equal("en", reloaded.TranscriptionLanguage);
        Assert.Equal(90, reloaded.BriefDurationSeconds);
        Assert.Equal("dark", reloaded.Theme);
        Assert.True(reloaded.OfflineMode);
    }

    [Fact]
    public void Load_FallsBackToDefaultsOnCorruptFile()
    {
        var service = new SettingsService(_dir);
        File.WriteAllText(service.FilePath, "{ not valid json !!!");
        var settings = service.Load();
        Assert.Equal(60, settings.BriefDurationSeconds);
    }
}

public class AppPathsTests
{
    [Fact]
    public void MeetingDirectory_UsesPortableLayout()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "mv-paths-test"));
        var dir = paths.MeetingDirectory(42, new DateOnly(2026, 9, 17));
        Assert.EndsWith(Path.Combine("data", "meetings", "2026", "09", "17", "42"), dir);
    }

    [Fact]
    public void MarkerAndPackPaths_ArePredictable()
    {
        var paths = new AppPaths(Path.Combine("root"));
        Assert.EndsWith(Path.Combine("data", "installed", "whisper-tiny.installed.json"),
            paths.InstalledMarkerPath("whisper-tiny"));
        Assert.EndsWith(Path.Combine("data", "installed", "whisper-tiny"),
            paths.InstalledPackDirectory("whisper-tiny"));
        Assert.EndsWith(Path.Combine("models", "llm"), paths.LlmModelsDir);
    }

    [Fact]
    public void ResolveDefaultRoot_ReturnsWritableDirectory()
    {
        var root = AppPaths.ResolveDefaultRoot();
        Assert.False(string.IsNullOrWhiteSpace(root));
    }
}
