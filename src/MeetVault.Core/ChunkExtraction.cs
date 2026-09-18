using System.Text.Json.Serialization;

namespace MeetVault.Core;

/// <summary>Chunk-level extraction result returned by the LLM for one transcript slice.</summary>
public sealed class ChunkExtraction
{
    [JsonPropertyName("chunkSummary")]
    public string ChunkSummary { get; set; } = string.Empty;

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
}
