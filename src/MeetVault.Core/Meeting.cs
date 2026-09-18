namespace MeetVault.Core;

/// <summary>A meeting stored in the vault: source recording, derived artifacts and processing state.</summary>
public sealed class Meeting
{
    public long Id { get; set; }
    public string Title { get; set; } = "Untitled meeting";

    /// <summary>Local calendar date the meeting took place (not a UTC instant).</summary>
    public DateOnly MeetingDate { get; set; }

    /// <summary>Local wall-clock start time, when known. Null when only the date is known.</summary>
    public TimeOnly? StartTime { get; set; }

    public long DurationSeconds { get; set; }
    public string? SourceFilePath { get; set; }
    public string? AudioFilePath { get; set; }
    public string? TranscriptFilePath { get; set; }
    public string? AnalysisFilePath { get; set; }
    public string? AudioBriefPath { get; set; }

    public ProcessingStatus Status { get; set; } = ProcessingStatus.Imported;

    /// <summary>The furthest successfully completed pipeline stage (drives stage caching and retry).</summary>
    public PipelineStage CompletedStage { get; set; } = PipelineStage.None;

    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public string MeetingDateIso => MeetingDate.ToString("yyyy-MM-dd");
}

/// <summary>High-level processing status surfaced in the UI.</summary>
public enum ProcessingStatus
{
    Imported = 0,
    Queued = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Canceled = 5,
}

/// <summary>Pipeline stages, in execution order. Persisted per meeting to enable stage caching.</summary>
public enum PipelineStage
{
    None = 0,
    AudioExtracted = 1,
    Transcribed = 2,
    Analyzed = 3,
    BriefScriptGenerated = 4,
    AudioBriefGenerated = 5,
}
