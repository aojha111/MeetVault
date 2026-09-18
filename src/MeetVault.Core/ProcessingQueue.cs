using System.Collections.Concurrent;

namespace MeetVault.Core;

/// <summary>
/// Serial background processing queue. Jobs run one at a time; supports enqueue, cancel
/// and pause/resume, with per-meeting cancellation.
/// </summary>
public sealed class ProcessingQueue : IDisposable
{
    private readonly MeetingProcessor _processor;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ConcurrentQueue<QueueItem> _queue = new();
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _active = new();

    private volatile bool _paused;
    private Task? _worker;

    private sealed record QueueItem(long MeetingId, bool Force);

    public ProcessingQueue(MeetingProcessor processor, Action<string>? log = null)
    {
        _processor = processor;
        _log = log ?? (_ => { });
        _worker = Task.Run(WorkerLoopAsync);
    }

    public event EventHandler<MeetingProgress>? Progress;

    public int PendingCount => _queue.Count;
    public bool IsPaused => _paused;

    public void Enqueue(long meetingId, bool force = false)
    {
        _queue.Enqueue(new QueueItem(meetingId, force));
        _signal.Release();
    }

    public void Cancel(long meetingId)
    {
        var remaining = _queue.ToList().Where(i => i.MeetingId != meetingId).ToList();
        while (_queue.TryDequeue(out _)) { }
        foreach (var item in remaining) _queue.Enqueue(item);

        if (_active.TryGetValue(meetingId, out var cts))
            cts.Cancel();
    }

    public void Pause() => _paused = true;
    public void Resume() { _paused = false; _signal.Release(); }

    private async Task WorkerLoopAsync()
    {
        while (true)
        {
            await _signal.WaitAsync().ConfigureAwait(false);
            if (_paused)
            {
                await Task.Delay(200).ConfigureAwait(false);
                continue;
            }
            if (!_queue.TryDequeue(out var item)) continue;

            using var cts = new CancellationTokenSource();
            _active[item.MeetingId] = cts;
            try
            {
                _log($"Processing meeting {item.MeetingId} (force={item.Force}).");
                await _processor.ProcessAsync(item.MeetingId, item.Force, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _log($"Meeting {item.MeetingId} processing canceled.");
            }
            catch (Exception ex)
            {
                _log($"Meeting {item.MeetingId} processing failed: {ex.Message}");
            }
            finally
            {
                _active.TryRemove(item.MeetingId, out _);
            }
        }
    }

    public void Dispose()
    {
        foreach (var cts in _active.Values) cts.Cancel();
    }
}
