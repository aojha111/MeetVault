using MeetVault.Core;
using MeetVault.Infrastructure;

namespace MeetVault.Cli;

public static partial class Program
{
    private static int Status(AppBootstrapper app)
    {
        Console.WriteLine($"Root:      {app.Paths.Root}");
        Console.WriteLine($"Database:  {app.Paths.DatabasePath}");
        Console.WriteLine($"FFmpeg:    {(app.Audio.IsAvailable ? "available" : "not installed")}");
        Console.WriteLine($"Whisper:   {(app.Transcriber.IsAvailable ? "available" : "not installed")}");
        Console.WriteLine($"LLM:       {(app.Llm.IsAvailable ? "available" : "not installed")}");
        Console.WriteLine($"TTS:       {(app.Tts.IsAvailable ? "available" : "not installed")}");
        Console.WriteLine($"Hardware:  {app.Hardware.Detect().Describe()}");
        var meetings = app.Repository.GetAllAsync().GetAwaiter().GetResult();
        Console.WriteLine($"Meetings:  {meetings.Count}");
        return 0;
    }

    private static int Packs(AppBootstrapper app)
    {
        foreach (var state in app.ModelManager.GetCatalog())
        {
            var status = state.IsInstalled ? $"installed v{state.InstalledVersion}" : "not installed";
            Console.WriteLine($"{state.Pack.Id,-38} {state.Pack.Kind,-14} {state.Pack.SizeBytes / (1024.0 * 1024),8:F1} MB  {status}");
        }
        return 0;
    }

    private static async Task<int> InstallAsync(AppBootstrapper app, string[] args)
    {
        var packId = args.Length > 1 ? args[1] : throw new ArgumentException("Missing pack id.");
        var progress = new Progress<double>(p => Console.Write($"\rDownloading {packId}: {p * 100:F1}%   "));
        await app.ModelManager.InstallAsync(packId, progress).ConfigureAwait(false);
        Console.WriteLine($"\rInstalled {packId}.                ");
        return 0;
    }

    private static int Remove(AppBootstrapper app, string[] args)
    {
        var packId = args.Length > 1 ? args[1] : throw new ArgumentException("Missing pack id.");
        app.ModelManager.Remove(packId);
        Console.WriteLine($"Removed {packId}.");
        return 0;
    }
}
