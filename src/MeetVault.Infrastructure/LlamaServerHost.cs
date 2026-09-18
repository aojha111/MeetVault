using System.Diagnostics;
using System.Net.Sockets;
using MeetVault.Core;

namespace MeetVault.Infrastructure;

/// <summary>
/// Manages a llama.cpp llama-server process bound to 127.0.0.1 for one GGUF model.
/// The server exposes the OpenAI-compatible chat completions endpoint locally only;
/// no cloud service is involved at any point.
/// </summary>
public sealed partial class LlamaServerHost : IAsyncDisposable
{
    private readonly string _serverExePath;
    private readonly Action<string> _log;
    private Process? _process;
    private HttpClient? _client;

    public string ModelPath { get; }
    public int ContextSize { get; }
    public int Threads { get; }
    public int GpuLayers { get; }
    public int Port { get; private set; }
    public bool IsRunning => _process is { HasExited: false };

    public LlamaServerHost(
        string serverExePath,
        string modelPath,
        int contextSize,
        int threads,
        int gpuLayers,
        Action<string>? log = null)
    {
        _serverExePath = serverExePath;
        ModelPath = modelPath;
        ContextSize = Math.Max(2048, contextSize);
        Threads = Math.Clamp(threads, 1, Environment.ProcessorCount);
        GpuLayers = gpuLayers;
        _log = log ?? (_ => { });
    }

    /// <summary>Starts the server (or reuses it) and waits until /health reports ready.</summary>
    public async Task<HttpClient> StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning && _client is not null) return _client;
        await StopAsync().ConfigureAwait(false);

        if (!File.Exists(_serverExePath))
            throw new InvalidOperationException("llama.cpp runtime is not installed. Install it from Model Manager → Runtimes.");
        if (!File.Exists(ModelPath))
            throw new InvalidOperationException($"LLM model file not found: {ModelPath}");

        Port = await FindFreePortAsync().ConfigureAwait(false);

        var psi = new ProcessStartInfo
        {
            FileName = _serverExePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_serverExePath) ?? ".",
        };
        foreach (var a in new[]
                 {
                     "-m", ModelPath,
                     "--port", Port.ToString(),
                     "--host", "127.0.0.1",
                     "-c", ContextSize.ToString(),
                     "-t", Threads.ToString(),
                     "-ngl", GpuLayers.ToString(),
                     "--no-webui",
                 })
        {
            psi.ArgumentList.Add(a);
        }

        _log($"Starting llama-server (port {Port}, ctx {ContextSize}, threads {Threads}, ngl {GpuLayers}).");
        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start llama-server.");

        // Drain output so the process never blocks on full pipes.
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) _log("[llama] " + e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) _log("[llama] " + e.Data); };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Port}"), Timeout = TimeSpan.FromMinutes(30) };
        _client = client;

        var ready = await WaitForHealthAsync(client, cancellationToken).ConfigureAwait(false);
        if (!ready)
        {
            await StopAsync().ConfigureAwait(false);
            throw new InvalidOperationException("llama-server did not become healthy in time. Check the log for details.");
        }
        _log("llama-server is ready.");
        return client;
    }

    private static async Task<bool> WaitForHealthAsync(HttpClient client, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(5); // model load can be slow on HDDs
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var resp = await client.GetAsync("/health", ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    if (body.Contains("\"ok\"", StringComparison.OrdinalIgnoreCase) ||
                        body.Contains("ok", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception) when (ExceptionFilter())
            {
                // server not up yet
            }
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return false;
    }

    private static bool ExceptionFilter() => true;

    public async Task StopAsync()
    {
        _client?.Dispose();
        _client = null;
        if (_process is not null)
        {
            ProcessRunner.TryKill(_process);
            _process.Dispose();
            _process = null;
            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    private static async Task<int> FindFreePortAsync()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var port = Random.Shared.Next(21000, 45000);
            try
            {
                var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException)
            {
                // port in use, try another
            }
        }
        throw new InvalidOperationException("No free local port available for llama-server.");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
