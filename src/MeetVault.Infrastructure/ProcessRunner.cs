using System.Diagnostics;
using System.Collections.Concurrent;

namespace MeetVault.Infrastructure;

/// <summary>
/// Safe process execution helper. Always uses ArgumentList (never shell string concatenation)
/// and only runs executables from the application's runtime directory.
/// </summary>
public static class ProcessRunner
{
    /// <summary>Runs a process, capturing stdout/stderr. Throws when the exit code is non-zero.</summary>
    public static async Task<ProcessResult> RunAsync(
        string exePath,
        IReadOnlyCollection<string> arguments,
        CancellationToken ct = default,
        string? workingDirectory = null,
        DataReceivedEventHandler? onStdoutLine = null,
        DataReceivedEventHandler? onStderrLine = null,
        int timeoutSeconds = 3600)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"Executable not found: {exePath}", exePath);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(exePath) ?? ".",
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var stdoutLines = new ConcurrentQueue<string>();
        var stderrLines = new ConcurrentQueue<string>();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdoutLines.Enqueue(e.Data);
            onStdoutLine?.Invoke(process, e);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderrLines.Enqueue(e.Data);
            onStderrLine?.Invoke(process, e);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        // WaitForExit flushes the asynchronous line readers. Do not call ReadToEnd here:
        // mixing synchronous reads with Begin*ReadLine causes InvalidOperationException.
        process.WaitForExit();
        var stdout = string.Join(Environment.NewLine, stdoutLines);
        var stderr = string.Join(Environment.NewLine, stderrLines);
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Process may have already exited.
        }
    }
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
}
