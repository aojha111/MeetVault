using MeetVault.Core;
using MeetVault.Infrastructure;

namespace MeetVault.Cli;

public static partial class Program
{
    private static async Task<int> ImportAsync(AppBootstrapper app, string[] args)
    {
        var file = args.Length > 1 ? args[1] : throw new ArgumentException("Missing file path.");
        var title = GetOption(args, "--title");
        var dateText = GetOption(args, "--date");
        var timeText = GetOption(args, "--time");
        var noProcess = args.Contains("--no-process");

        DateOnly? date = string.IsNullOrEmpty(dateText) ? null : DateOnly.Parse(dateText);
        TimeOnly? time = string.IsNullOrEmpty(timeText) ? null : TimeOnly.Parse(timeText);

        var (meeting, error) = await app.Meetings.ImportAsync(file, title, date, time).ConfigureAwait(false);
        if (meeting is null)
        {
            Console.Error.WriteLine("Import failed: " + error);
            return 1;
        }
        Console.WriteLine($"Imported meeting {meeting.Id}: \"{meeting.Title}\" ({meeting.MeetingDate:yyyy-MM-dd}).");

        if (!noProcess)
        {
            AttachProgress(app);
            await app.Processor.ProcessAsync(meeting.Id).ConfigureAwait(false);
            Console.WriteLine("\rProcessing complete.                      ");
        }
        return 0;
    }

    private static async Task<int> ProcessAsync(AppBootstrapper app, string[] args)
    {
        var idText = args.Length > 1 ? args[1] : throw new ArgumentException("Missing meeting id.");
        var id = long.Parse(idText);
        var force = args.Contains("--force");
        AttachProgress(app);
        await app.Processor.ProcessAsync(id, force).ConfigureAwait(false);
        Console.WriteLine("\rProcessing complete.                      ");
        return 0;
    }

        private static void AttachProgress(AppBootstrapper app)
    {
        // The CLI drives the processor directly (not via the queue), so subscribe to the
        // processor's per-stage progress, which is the event actually raised here.
        app.Processor.Progress += (_, p) =>
        {
            if (p.Percent >= 0)
                Console.Write($"\r{p.Stage}: {p.Percent * 100:F1}%   ");
            else
                Console.Write($"\r{p.Stage}: {p.Message}   ");
        };
    }

    private static int List(AppBootstrapper app)
    {
        var meetings = app.Repository.GetAllAsync().GetAwaiter().GetResult();
        foreach (var m in meetings)
        {
            Console.WriteLine($"#{m.Id,-4} {m.MeetingDate:yyyy-MM-dd}  [{m.Status}]  {m.Title}");
        }
        Console.WriteLine($"{meetings.Count} meeting(s).");
        return 0;
    }

    private static async Task<int> SearchAsync(AppBootstrapper app, string[] args)
    {
        var query = args.Length > 1 ? args[1] : throw new ArgumentException("Missing query.");
        var response = await app.Search.SearchAsync(query).ConfigureAwait(false);
        foreach (var hit in response.Hits)
        {
            Console.WriteLine($"#{hit.MeetingId} [{hit.MatchKind}] {hit.Title} ({hit.MeetingDate:yyyy-MM-dd})");
            Console.WriteLine($"    {hit.Snippet.Replace('\n', ' ')}");
        }
        Console.WriteLine($"{response.Hits.Count} hit(s).");
        return 0;
    }
}
