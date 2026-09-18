using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetVault.Core;

/// <summary>Loads and saves application settings as JSON, creating defaults on first run.</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _filePath;
    private readonly object _lock = new();

    public SettingsService(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        _filePath = Path.Combine(configDirectory, "appsettings.json");
    }

    public string FilePath => _filePath;

    public AppSettings Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                    if (settings is not null) return settings;
                }
            }
            catch (Exception)
            {
                // Corrupt settings fall back to defaults; the file will be rewritten on next save.
            }
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            File.WriteAllText(_filePath, json);
        }
    }
}
