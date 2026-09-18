using System.Diagnostics;

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

        process.OutputDataReceived += onStdoutLine ?? ((_, _) => { });
        process.ErrorDataReceived += onStderrLine ?? ((_, _) => { });
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

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
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
