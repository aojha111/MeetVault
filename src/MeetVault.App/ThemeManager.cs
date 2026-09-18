using System.Windows;
using Microsoft.Win32;

namespace MeetVault.App;

/// <summary>
/// Applies the dark/light palette to the running application ("system", "light" or "dark").
/// All control templates reference palette brushes via DynamicResource, so swapping the
/// palette dictionary re-styles every open window immediately.
/// </summary>
public static class ThemeManager
{
    private static readonly Uri DarkUri = new("pack://application:,,,/Themes/Dark.xaml");
    private static readonly Uri LightUri = new("pack://application:,,,/Themes/Light.xaml");

    /// <summary>Raised after the effective palette changed (dark → true, light → false).</summary>
    public static event Action<bool>? ThemeChanged;

    public static bool IsDarkNow { get; private set; } = true;

    /// <summary>Applies the configured theme ("system", "light" or "dark").</summary>
    public static void Apply(string theme)
    {
        var dark = theme.Equals("dark", StringComparison.OrdinalIgnoreCase) ||
                   (theme.Equals("system", StringComparison.OrdinalIgnoreCase) && SystemPrefersDark());
        SetDark(dark);
    }

    public static void SetDark(bool dark)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        var paletteUri = dark ? DarkUri : LightUri;

        // Replace the existing palette dictionary (the one whose source is a palette).
        for (int i = 0; i < dicts.Count; i++)
        {
            if (dicts[i].Source is { } src && src is { } && (src == DarkUri || src == LightUri))
            {
                dicts[i] = new ResourceDictionary { Source = paletteUri };
                IsDarkNow = dark;
                ThemeChanged?.Invoke(dark);
                return;
            }
        }

        // No palette loaded yet (first startup): append after the control styles.
        dicts.Add(new ResourceDictionary { Source = paletteUri });
        IsDarkNow = dark;
        ThemeChanged?.Invoke(dark);
    }

    /// <summary>Reads the Windows personalization setting (AppsUseLightTheme).</summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch (Exception)
        {
            return true; // registry unavailable → default to dark
        }
    }

    /// <summary>Watches Windows for light/dark changes while the mode is "system".</summary>
    public static void StartSystemWatcher(Func<string> getMode)
    {
        try
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category != UserPreferenceCategory.General) return;
                if (!getMode().Equals("system", StringComparison.OrdinalIgnoreCase)) return;
                var dispatcher = Application.Current?.Dispatcher;
                dispatcher?.BeginInvoke(() =>
                {
                    var dark = SystemPrefersDark();
                    if (dark != IsDarkNow) SetDark(dark);
                });
            };
        }
        catch (Exception)
        {
            // SystemEvents unavailable — theme stays static.
        }
    }
}
