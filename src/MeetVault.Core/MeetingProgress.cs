namespace MeetVault.Core;

/// <summary>Progress information for one processing stage of a meeting.</summary>
public sealed class MeetingProgress
{
    public required long MeetingId { get; init; }
    public required string Stage { get; init; }
    /// <summary>0..1 progress; -1 for indeterminate.</summary>
    public double Percent { get; init; } = -1;
    public string Message { get; init; } = string.Empty;
    public required ProcessingStatus Status { get; init; }
    public string? Error { get; init; }
}

/// <summary>Thrown when a pipeline stage fails; distinguishes recoverable from fatal failures.</summary>
public sealed class PipelineStageException : Exception
{
    public PipelineStage Stage { get; }
    public bool Recoverable { get; }

    public PipelineStageException(PipelineStage stage, string message, bool recoverable = true, Exception? inner = null)
        : base(message, inner)
    {
        Stage = stage;
        Recoverable = recoverable;
    }
}
