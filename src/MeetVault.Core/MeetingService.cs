namespace MeetVault.Core;

/// <summary>Import workflow: validate, detect metadata, create the meeting record.</summary>
public sealed class MeetingService
{
    private readonly IMeetingRepository _meetings;
    private readonly Func<long, DateOnly, string> _meetingDir;

    public MeetingService(IMeetingRepository meetings, Func<long, DateOnly, string> meetingDir)
    {
        _meetings = meetings;
        _meetingDir = meetingDir;
    }

    /// <summary>Imports a recording: validates it and creates the meeting record. Returns the meeting and an error (null on success).</summary>
    public async Task<(Meeting? Meeting, string? Error)> ImportAsync(
        string filePath,
        string? titleOverride = null,
        DateOnly? dateOverride = null,
        TimeOnly? timeOverride = null,
        CancellationToken ct = default)
    {
        var validationError = ImportValidator.Validate(filePath);
        if (validationError is not null)
            return (null, validationError);

        var (date, time, detectedTitle) = MeetingMetadataDetector.Detect(filePath);
        var meeting = new Meeting
        {
            Title = titleOverride is { Length: > 0 } ? titleOverride : detectedTitle,
            MeetingDate = dateOverride ?? date,
            StartTime = timeOverride ?? time,
            SourceFilePath = Path.GetFullPath(filePath),
            Status = ProcessingStatus.Imported,
        };

        var existing = await _meetings.GetBySourcePathAsync(meeting.SourceFilePath, ct).ConfigureAwait(false);
        if (existing is not null)
            return (existing, $"This recording is already in the library (\"{existing.Title}\").");

        var created = await _meetings.CreateAsync(meeting, ct).ConfigureAwait(false);
        Directory.CreateDirectory(_meetingDir(created.Id, created.MeetingDate));
        return (created, null);
    }

    public Task<Meeting?> GetAsync(long id, CancellationToken ct = default) => _meetings.GetAsync(id, ct);
    public Task<IReadOnlyList<Meeting>> GetAllAsync(CancellationToken ct = default) => _meetings.GetAllAsync(ct);

    public async Task UpdateDetailsAsync(long id, string title, DateOnly date, TimeOnly? start, CancellationToken ct = default)
    {
        var meeting = await _meetings.GetAsync(id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Meeting {id} not found.");
        meeting.Title = title;
        meeting.MeetingDate = date;
        meeting.StartTime = start;
        meeting.UpdatedAt = DateTimeOffset.Now;
        await _meetings.UpdateAsync(meeting, ct).ConfigureAwait(false);
    }

    public async Task UpdateSpeakerAsync(long meetingId, long segmentId, string? speaker, CancellationToken ct = default)
    {
        var segments = await _meetings.GetTranscriptAsync(meetingId, ct).ConfigureAwait(false);
        foreach (var seg in segments.Where(s => s.Id == segmentId))
        {
            seg.Speaker = speaker;
        }
        var meeting = await _meetings.GetAsync(meetingId, ct).ConfigureAwait(false);
        if (meeting is null) return;
        await _meetings.ReplaceTranscriptAsync(meetingId, segments, meeting.TranscriptFilePath ?? string.Empty, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var meeting = await _meetings.GetAsync(id, ct).ConfigureAwait(false);
        if (meeting is null) return;
        await _meetings.DeleteAsync(id, ct).ConfigureAwait(false);
        var dir = _meetingDir(id, meeting.MeetingDate);
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* in use */ }
        }
    }
}
