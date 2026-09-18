using System.Text.Json.Serialization;

namespace MeetVault.Core;

/// <summary>LLM brief-script envelope.</summary>
public sealed class BriefScript
{
    [JsonPropertyName("script")]
    public string? Script { get; set; }
}
