using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MeetVault.Infrastructure;

public sealed partial class LlamaServerHost
{
    /// <summary>Sends an OpenAI-compatible chat completion request with optional JSON-schema grammar constraint.</summary>
    public static async Task<string> ChatCompletionAsync(
        HttpClient client,
        string systemPrompt,
        string userPrompt,
        string? jsonSchema,
        double temperature,
        int maxTokens,
        CancellationToken ct)
    {
        var request = new Dictionary<string, object?>
        {
            ["messages"] = new object[]
            {
                new Dictionary<string, object> { ["role"] = "system", ["content"] = systemPrompt },
                new Dictionary<string, object> { ["role"] = "user", ["content"] = userPrompt },
            },
            ["temperature"] = temperature,
            ["max_tokens"] = maxTokens,
            ["stream"] = false,
            ["cache_prompt"] = true,
        };
        if (!string.IsNullOrWhiteSpace(jsonSchema))
        {
            request["response_format"] = new Dictionary<string, object>
            {
                ["type"] = "json_schema",
                ["json_schema"] = JsonDocument.Parse(jsonSchema).RootElement.Clone(),
            };
        }

        var json = JsonSerializer.Serialize(request);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync("/v1/chat/completions", content, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"llama-server request failed ({(int)resp.StatusCode}): {TruncateForLog(body)}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            throw new InvalidOperationException("llama-server returned no choices.");

        var message = choices[0].GetProperty("message");
        var text = message.TryGetProperty("content", out var contentEl) ? contentEl.GetString() : null;
        return text ?? string.Empty;
    }

    private static string TruncateForLog(string s) =>
        string.IsNullOrWhiteSpace(s) ? "(no output)" : (s.Length > 400 ? s[..400] : s);

    private static async Task<bool> UnusedPortProbeAsync(int port)
    {
        try
        {
            var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
