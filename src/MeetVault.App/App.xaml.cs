using System.Windows;
using MeetVault.Core;
using MeetVault.Infrastructure;

namespace MeetVault.App;

/// <summary>Application entry point: configures local file logging before the UI loads.</summary>
public partial class App : Application
{
    public static AppBootstrapper? Bootstrapper { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Logs go to <root>/logs; the root is resolved lazily by the bootstrapper, so a
        // lightweight pre-pass uses the same default-location logic.
        var root = AppPaths.ResolveDefaultRoot();
        Serilog.Log.Logger = LogSetup.Configure(System.IO.Path.Combine(root, "logs"))
            .CreateLogger();

        // Apply the saved theme before any window renders, then follow the OS while in "system" mode.
        Bootstrapper = new AppBootstrapper();
        ThemeManager.Apply(Bootstrapper.Settings.Theme);
        ThemeManager.StartSystemWatcher(() => Bootstrapper.Settings.Theme);

        DispatcherUnhandledException += (_, args) =>
        {
            Serilog.Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show(
                args.Exception.Message,
                "MeetVault error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Bootstrapper?.Dispose();
        Serilog.Log.CloseAndFlush();
        base.OnExit(e);
    }
}
