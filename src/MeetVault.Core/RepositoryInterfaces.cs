namespace MeetVault.Core;

/// <summary>Persistence for meetings, transcripts, analyses, relations.</summary>
public interface IMeetingRepository
{
    Task<Meeting> CreateAsync(Meeting meeting, CancellationToken ct = default);
    Task<Meeting?> GetAsync(long id, CancellationToken ct = default);
    Task<Meeting?> GetBySourcePathAsync(string sourceFilePath, CancellationToken ct = default);
    Task UpdateAsync(Meeting meeting, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<Meeting>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Meeting>> GetByDateAsync(DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<Meeting>> GetByStatusAsync(ProcessingStatus status, CancellationToken ct = default);

    Task ReplaceTranscriptAsync(long meetingId, IReadOnlyList<TranscriptSegment> segments, string transcriptPath, CancellationToken ct = default);
    Task<IReadOnlyList<TranscriptSegment>> GetTranscriptAsync(long meetingId, CancellationToken ct = default);

    Task SaveAnalysisAsync(long meetingId, MeetingAnalysis analysis, string analysisPath, CancellationToken ct = default);
    Task<MeetingAnalysis?> GetAnalysisAsync(long meetingId, CancellationToken ct = default);

    Task SaveRelationsAsync(long meetingId, IReadOnlyList<MeetingRelation> relations, CancellationToken ct = default);
    Task<IReadOnlyList<MeetingRelation>> GetRelationsAsync(long meetingId, CancellationToken ct = default);
}

/// <summary>Full-text search across stored meeting knowledge.</summary>
public interface ISearchRepository
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default);
    Task<IReadOnlyList<ActionItem>> GetOpenActionItemsAsync(CancellationToken ct = default);
}

/// <summary>Search result row.</summary>
public sealed class SearchHit
{
    public long MeetingId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateOnly MeetingDate { get; set; }
    /// <summary>Which part of the knowledge matched: title, transcript, summary, decision, action, question, topic, person.</summary>
    public string MatchKind { get; set; } = string.Empty;
    /// <summary>Snipped match context for display.</summary>
    public string Snippet { get; set; } = string.Empty;
}

/// <summary>Relationship between two meetings.</summary>
public sealed class MeetingRelation
{
    public long SourceMeetingId { get; set; }
    public long TargetMeetingId { get; set; }
    /// <summary>"same-entity", "same-topic", "follow-up"...</summary>
    public string RelationType { get; set; } = string.Empty;
    /// <summary>Human-readable evidence, e.g. shared entity or topic.</summary>
    public string Evidence { get; set; } = string.Empty;
}

/// <summary>Model manager: download, verify (SHA-256), install, remove and locate packs.</summary>
public interface IModelManager
{
    /// <summary>All packs listed in the registry with their installation state.</summary>
    IReadOnlyList<ModelPackState> GetCatalog();

    ModelPackState? GetPack(string packId);

    /// <summary>Downloads (resumable), verifies SHA-256 and installs a pack. Progress in [0,1].</summary>
    Task InstallAsync(string packId, IProgress<double>? progress, CancellationToken cancellationToken = default);

    /// <summary>Removes an installed pack and its marker.</summary>
    void Remove(string packId);

    /// <summary>Path to the primary model file for model-kind packs, when installed.</summary>
    string? GetInstalledModelFilePath(string packId);

    /// <summary>Directory containing executables for an installed runtime pack.</summary>
    string? GetInstalledRuntimeDirectory(string packId);

    /// <summary>Registers an already-present model file (side-loaded) so the app can use it without downloading.</summary>
    void MarkInstalledFromLocalFile(string packId, string filePath);
}

/// <summary>Install state of one registry pack.</summary>
public sealed class ModelPackState
{
    public required ModelPack Pack { get; set; }
    public bool IsInstalled { get; set; }
    public string? InstalledVersion { get; set; }
    public DateTimeOffset? InstalledAt { get; set; }
    public long SizeOnDiskBytes { get; set; }
}
