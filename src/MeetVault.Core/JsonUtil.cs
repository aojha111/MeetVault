using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeetVault.Core;

/// <summary>Robust helpers for parsing LLM output into JSON documents.</summary>
public static partial class JsonUtil
{
    [GeneratedRegex("```(?:json)?\\s*(.*?)\\s*```", RegexOptions.Singleline)]
    private static partial Regex CodeFencePattern();

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>Extracts the first JSON object/array from raw model output, tolerating prose and code fences.</summary>
    public static string ExtractJsonText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var fence = CodeFencePattern().Match(raw);
        if (fence.Success) raw = fence.Groups[1].Value;

        int objStart = raw.IndexOf('{');
        int arrStart = raw.IndexOf('[');
        if (objStart < 0 && arrStart < 0) return raw.Trim();

        bool isArray = arrStart >= 0 && (objStart < 0 || arrStart < objStart);
        char open = isArray ? '[' : '{';
        char close = isArray ? ']' : '}';
        int start = isArray ? arrStart : objStart;

        // Find the matching closing bracket, respecting strings and escapes.
        bool inString = false, escaped = false;
        int depth = 0;
        for (int i = start; i < raw.Length; i++)
        {
            char c = raw[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\') { escaped = true; continue; }
            if (c == '"') inString = !inString;
            if (inString) continue;
            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                    return raw[start..(i + 1)];
            }
        }
        return raw[start..].Trim();
    }

    /// <summary>Parses a JSON string into T, attempting to repair common LLM issues (fences, prose, trailing commas).</summary>
    public static T? ParseLenient<T>(string raw)
    {
        var json = ExtractJsonText(raw);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        try
        {
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch (JsonException)
        {
            // Repair: remove trailing commas before } or ]
            var repaired = Regex.Replace(json, @",\s*([}\]])", "$1");
            return JsonSerializer.Deserialize<T>(repaired, options);
        }
    }

    public static string ToPrettyJson<T>(T value) => JsonSerializer.Serialize(value, Pretty);

    /// <summary>Validates that required strings exist and returns a list of schema problems.</summary>
    public static List<string> ValidateAnalysis(MeetingAnalysis? analysis)
    {
        var problems = new List<string>();
        if (analysis is null)
        {
            problems.Add("Analysis payload is null.");
            return problems;
        }
        if (string.IsNullOrWhiteSpace(analysis.Summary))
            problems.Add("summary is empty.");
        if (analysis.Decisions.Any(d => string.IsNullOrWhiteSpace(d.DecisionText)))
            problems.Add("a decision has empty text.");
        if (analysis.ActionItems.Any(a => string.IsNullOrWhiteSpace(a.Task)))
            problems.Add("an action item has empty task.");
        return problems;
    }
}
