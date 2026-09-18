using System.Text.Json.Serialization;

namespace MeetVault.Core;

/// <summary>Structured meeting intelligence extracted by the local LLM from the transcript.</summary>
public sealed class MeetingAnalysis
{
    [JsonPropertyName("meetingTitle")]
    public string MeetingTitle { get; set; } = string.Empty;

    /// <summary>ISO yyyy-MM-dd meeting date as stated/discerned from the meeting itself; may be empty.</summary>
    [JsonPropertyName("meetingDate")]
    public string MeetingDate { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("agenda")]
    public List<string> Agenda { get; set; } = [];

    [JsonPropertyName("topics")]
    public List<string> Topics { get; set; } = [];

    [JsonPropertyName("keyDiscussionPoints")]
    public List<string> KeyDiscussionPoints { get; set; } = [];

    [JsonPropertyName("decisions")]
    public List<Decision> Decisions { get; set; } = [];

    [JsonPropertyName("actionItems")]
    public List<ActionItem> ActionItems { get; set; } = [];

    [JsonPropertyName("deadlines")]
    public List<string> Deadlines { get; set; } = [];

    [JsonPropertyName("risks")]
    public List<string> Risks { get; set; } = [];

    [JsonPropertyName("openQuestions")]
    public List<OpenQuestion> OpenQuestions { get; set; } = [];

    [JsonPropertyName("followUps")]
    public List<FollowUp> FollowUps { get; set; } = [];

    [JsonPropertyName("participants")]
    public List<string> Participants { get; set; } = [];

    [JsonPropertyName("entities")]
    public List<Entity> Entities { get; set; } = [];

    /// <summary>Free-text hints used for cross-meeting linking (topic keywords, project names...).</summary>
    [JsonPropertyName("relatedMeetingHints")]
    public List<string> RelatedMeetingHints { get; set; } = [];
}

/// <summary>A decision actually agreed during the meeting (never speculation or proposals).</summary>
public sealed class Decision
{
    [JsonPropertyName("decision")]
    public string DecisionText { get; set; } = string.Empty;

    /// <summary>1-based transcript segment sequence numbers that support this decision.</summary>
    [JsonPropertyName("sourceSegmentIds")]
    public List<long> SourceSegmentIds { get; set; } = [];
}

/// <summary>An action item assigned during the meeting.</summary>
public sealed class ActionItem
{
    [JsonPropertyName("task")]
    public string Task { get; set; } = string.Empty;

    /// <summary>Owner exactly as named by the speakers; null when no owner was stated. Never inferred.</summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    /// <summary>ISO yyyy-MM-dd deadline when stated, otherwise free text such as "next Friday".</summary>
    [JsonPropertyName("deadline")]
    public string? Deadline { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Open";

    [JsonPropertyName("sourceSegmentIds")]
    public List<long> SourceSegmentIds { get; set; } = [];
}

public sealed class OpenQuestion
{
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Open";
}

public sealed class FollowUp
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }
}

/// <summary>A named entity (person, project, system, organization) mentioned in the meeting.</summary>
public sealed class Entity
{
    [JsonPropertyName("type")]
    public string EntityType { get; set; } = "other";

    [JsonPropertyName("name")]
    public string EntityName { get; set; } = string.Empty;
}
