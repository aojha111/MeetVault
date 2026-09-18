using System.Collections.Concurrent;
using MeetVault.Core;
using MeetVault.Infrastructure;

namespace MeetVault.Tests;

/// <summary>
/// Regression coverage for <see cref="ProcessingQueue"/>: progress raised by the processor
/// must be forwarded through the queue's own <see cref="ProcessingQueue.Progress"/> event.
/// That is the channel the WPF UI and CLI subscribe to — if it is never raised, background
/// work is completely invisible and the in-app retry/reprocess buttons can never surface.
/// </summary>
public class QueueTests : IDisposable
{
    private readonly string _root;
    private readonly Database _db;
    private readonly SqliteMeetingRepository _repo;
    private readonly AppPaths _paths;

    public QueueTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meetvault-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new Database(Path.Combine(_root, "meetings.db"));
        _db.ApplyMigrationsAsync().GetAwaiter().GetResult();
        _repo = new SqliteMeetingRepository(_db);
        _paths = new AppPaths(_root);
    }

    private Meeting CreateImportedMeeting()
    {
        // A minimal non-empty source; the fake extractor doesn't care about contents.
        var source = Path.Combine(_root, "meeting.mp4");
        if (!File.Exists(source)) File.WriteAllBytes(source, [1, 2, 3, 4]);
        var meeting = new Meeting
        {
            Title = "Queue test",
            MeetingDate = DateOnly.FromDateTime(DateTime.Today),
            StartTime = new TimeOnly(10, 0),
            SourceFilePath = source,
        };
        return _repo.CreateAsync(meeting).GetAwaiter().GetResult();
    }

    private MeetingProcessor NewProcessor()
    {
        var settings = new AppSettings { TranscriptionLanguage = "en", EnableVad = false };
        return new MeetingProcessor(
            _repo, new FakeAudioExtractor(), new FakeTranscriber(), new FakeLlm(), new FakeTts(),
                        (id, date) => _paths.MeetingDirectory(id, date), settings);
    }

    [Fact]
    public async Task Queue_ForwardsProcessorProgress_ToSubscribers()
    {
        var meeting = CreateImportedMeeting();
        var processor = NewProcessor();

        var collected = new ConcurrentBag<MeetingProgress>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var queue = new ProcessingQueue(processor);
        var completed = new TaskCompletionSource<bool>();
        queue.Progress += (s, p) =>
        {
            if (p.MeetingId != meeting.Id) return;
            collected.Add(p);
            if (p.Status == ProcessingStatus.Completed) completed.TrySetResult(true);
        };

                queue.Enqueue(meeting.Id);
        await completed.Task.WaitAsync(cts.Token);

        // Bookends: Queued raised on enqueue, Completed forwarded from the processor.
        Assert.Contains(collected, p => p.Status == ProcessingStatus.Queued);
        Assert.Contains(collected, p => p.Status == ProcessingStatus.Completed);
        // The processor's per-stage reports must be forwarded, not swallowed.
        Assert.Contains(collected, p => p.Status == ProcessingStatus.Processing);
        Assert.True(collected.Count > 3, "stage-by-stage progress should be forwarded, not just bookends");
    }

    [Fact]
    public async Task Retry_AfterFailure_ForwardsFailureThenCompletion()
    {
        var meeting = CreateImportedMeeting();

        // First processor fails at the LLM stage; the failure must be visible to subscribers.
        var failingProcessor = new MeetingProcessor(
            _repo, new FakeAudioExtractor(), new FakeTranscriber(), new FailingLlm(), new FakeTts(),
            (id, date) => _paths.MeetingDirectory(id, date),
            new AppSettings { TranscriptionLanguage = "en", EnableVad = false });

        var collected = new ConcurrentBag<MeetingProgress>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using (var queue = new ProcessingQueue(failingProcessor))
        {
            queue.Progress += (_, p) => { if (p.MeetingId == meeting.Id) collected.Add(p); };
            var failed = new TaskCompletionSource<bool>();
            queue.Progress += (s, p) =>
            {
                if (p.MeetingId == meeting.Id && p.Status == ProcessingStatus.Failed)
                    failed.TrySetResult(true);
            };
                        queue.Enqueue(meeting.Id, force: true);
            await failed.Task.WaitAsync(cts.Token);
        }

        Assert.Contains(collected, p => p.Status == ProcessingStatus.Queued);
        Assert.Contains(collected, p => p.Status == ProcessingStatus.Failed && p.Error is not null);

        // Retry with a working LLM: must complete successfully.
        collected.Clear();
        using var okQueue = new ProcessingQueue(NewProcessor());
        var completed = new TaskCompletionSource<bool>();
        okQueue.Progress += (s, p) =>
        {
            if (p.MeetingId != meeting.Id) return;
            collected.Add(p);
            if (p.Status == ProcessingStatus.Completed) completed.TrySetResult(true);
        };
                okQueue.Enqueue(meeting.Id, force: false);
        await completed.Task.WaitAsync(cts.Token);

        Assert.Contains(collected, p => p.Status == ProcessingStatus.Completed);
        var final = await _repo.GetAsync(meeting.Id);
        Assert.Equal(ProcessingStatus.Completed, final!.Status);
    }

    public void Dispose()
    {
        try
        {
            _db.WithConnectionAsync<bool>(async conn =>
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                await cmd.ExecuteNonQueryAsync();
                return true;
            }).GetAwaiter().GetResult();
        }
        catch { /* best-effort */ }
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
