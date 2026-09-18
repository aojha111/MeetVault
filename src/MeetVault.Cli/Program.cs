using MeetVault.Core;
using MeetVault.Infrastructure;

namespace MeetVault.Cli;

/// <summary>
/// Headless operations for automation and power users. All operations run fully locally
/// (except pack downloads, which are explicit user actions).
/// </summary>
public static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        var root = GetOption(args, "--root");
        using var bootstrapper = new AppBootstrapper(root);
        var cmd = args[0].ToLowerInvariant();

        try
        {
            switch (cmd)
            {
                case "status": return Status(bootstrapper);
                case "packs": return Packs(bootstrapper);
                case "install": return await InstallAsync(bootstrapper, args).ConfigureAwait(false);
                case "remove": return Remove(bootstrapper, args);
                case "import": return await ImportAsync(bootstrapper, args).ConfigureAwait(false);
                case "process": return await ProcessAsync(bootstrapper, args).ConfigureAwait(false);
                case "list": return List(bootstrapper);
                case "search": return await SearchAsync(bootstrapper, args).ConfigureAwait(false);
                default:
                    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 2;
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        var idx = Array.IndexOf(args, name);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            MeetVault CLI — local meeting knowledge vault
            Usage:
              MeetVault.Cli status
              MeetVault.Cli packs
              MeetVault.Cli install <packId>
              MeetVault.Cli remove <packId>
              MeetVault.Cli import <file> [--title T] [--date yyyy-MM-dd] [--time HH:mm] [--no-process]
              MeetVault.Cli process <meetingId> [--force]
              MeetVault.Cli list
              MeetVault.Cli search <query>
            Options:
              --root <path>   Use an alternate vault root directory (portable data).
            """);
    }
}
