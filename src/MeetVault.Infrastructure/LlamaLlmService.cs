using MeetVault.Core;

namespace MeetVault.Infrastructure;

/// <summary>
/// ILlmService implementation backed by a local llama.cpp llama-server process.
/// Conversation context never leaves the machine.
/// </summary>
public sealed class LlamaLlmService : ILlmService, IDisposable
{
    private readonly Func<string?> _serverExeResolver;
    private readonly Func<string?> _modelResolver;
    private readonly AppSettings _settings;
    private readonly Action<string> _log;

    private LlamaServerHost? _host;
    private HttpClient? _client;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LlamaLlmService(
        Func<string?> serverExeResolver,
        Func<string?> modelResolver,
        AppSettings settings,
        Action<string>? log = null)
    {
        _serverExeResolver = serverExeResolver;
        _modelResolver = modelResolver;
        _settings = settings;
        _log = log ?? (_ => { });
    }

    public bool IsAvailable => _serverExeResolver() is not null && _modelResolver() is not null;

    public bool CanFit(string text)
    {
        // Rough token estimate: ~4 chars/token for English. Leave headroom for prompts and output.
        var budget = (_settings.LlmContextSize - 1200) * 4;
        return text.Length <= budget;
    }

    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) return;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is null) await EnsureServerAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        string? jsonSchema,
        double temperature = 0.2,
        int maxTokens = 2048,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = await EnsureServerAsync(cancellationToken).ConfigureAwait(false);
            const int maxAttempts = 2;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await LlamaServerHost.ChatCompletionAsync(
                        client, systemPrompt, userPrompt, jsonSchema, temperature, maxTokens, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < maxAttempts && ex is not OperationCanceledException)
                {
                    _log($"LLM request failed ({ex.Message}); restarting server and retrying.");
                    StopServer();
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Shutdown() => StopServer();

    private async Task<HttpClient> EnsureServerAsync(CancellationToken ct)
    {
        if (_client is not null && _host is { IsRunning: true }) return _client;

        var exe = _serverExeResolver()
            ?? throw new InvalidOperationException("llama.cpp runtime is not installed. Install it from Model Manager → Runtimes.");
        var model = _modelResolver()
            ?? throw new InvalidOperationException("No LLM model is installed. Install one from Model Manager.");

        StopServer();
        _host = new LlamaServerHost(exe, model, _settings.LlmContextSize, Math.Max(4, Environment.ProcessorCount - 2), GpuLayers(), _log);
        _client = await _host.StartAsync(ct).ConfigureAwait(false);
        return _client;
    }

    /// <summary>GPU layers: 99 for the Vulkan runtime pack (all layers offloaded), 0 for CPU.</summary>
    private int GpuLayers() =>
        File.Exists(Path.Combine(Path.GetDirectoryName(_serverExeResolver() ?? string.Empty) ?? string.Empty, "ggml-vulkan.dll")) ? 99 : 0;

    private void StopServer()
    {
        try
        {
            _host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            _host = null;
            _client?.Dispose();
            _client = null;
        }
    }

    public void Dispose()
    {
        try { Shutdown(); } catch (Exception) { /* best effort */ }
        _gate.Dispose();
    }
}
