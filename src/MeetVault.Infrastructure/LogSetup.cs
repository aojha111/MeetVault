using Serilog;
using Serilog.Events;

namespace MeetVault.Infrastructure;

/// <summary>Serilog setup: local rolling file logs; never logs transcript content.</summary>
public static class LogSetup
{
    /// <summary>Configures Serilog to write into <paramref name="logsDirectory"/>; returns the LoggerConfiguration.</summary>
    public static LoggerConfiguration Configure(string logsDirectory, LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        Directory.CreateDirectory(logsDirectory);
        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.File(
                Path.Combine(logsDirectory, "meetvault-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 20 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
    }
}
