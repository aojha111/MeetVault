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
    private readonly CancellationTokenSource _stop = new();

    private volatile bool _paused;
    private Task? _worker;

    private sealed record QueueItem(long MeetingId, bool Force);

    public ProcessingQueue(MeetingProcessor processor, Action<string>? log = null)
    {
        _processor = processor;
        _log = log ?? (_ => { });
        // The processor raises per-stage progress on its own Progress event; forward those
        // (plus the queued/completed transitions below) to subscribers of this queue, which
        // is the channel the WPF UI and CLI listen on.
        _processor.Progress += OnProcessorProgress;
        _worker = Task.Run(WorkerLoopAsync);
    }

    /// <summary>Per-stage progress reported by the processor, forwarded to subscribers.</summary>
    public event EventHandler<MeetingProgress>? Progress;

    private void OnProcessorProgress(object? sender, MeetingProgress p) => Progress?.Invoke(this, p);

    public int PendingCount => _queue.Count;
    public bool IsPaused => _paused;

        public void Enqueue(long meetingId, bool force = false)
    {
        _queue.Enqueue(new QueueItem(meetingId, force));
        // Surface the queued state immediately so the UI reflects pending work without
        // having to wait for the background worker to pick the job up.
        Progress?.Invoke(this, new MeetingProgress
        {
            MeetingId = meetingId,
            Stage = "Queued",
            Percent = -1,
            Message = force ? "Reprocessing queued." : "Queued for processing.",
            Status = ProcessingStatus.Queued,
        });
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
            try
            {
                await _signal.WaitAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.Token.IsCancellationRequested)
            {
                return; // shutdown
            }
            if (_stop.Token.IsCancellationRequested) return;

            if (_paused)
            {
                try { await Task.Delay(250, _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_stop.Token.IsCancellationRequested) { return; }
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
        _stop.Cancel();
        foreach (var cts in _active.Values) cts.Cancel();
        _processor.Progress -= OnProcessorProgress;
    }
}
