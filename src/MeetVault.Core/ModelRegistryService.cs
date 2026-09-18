using System.Text.Json;

namespace MeetVault.Core;

/// <summary>
/// Loads the model registry (config/model-registry.json), validating entries.
/// The registry is data, not code: new packs can be added without changing the application.
/// </summary>
public sealed class ModelRegistryService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _registryPath;
    private readonly Func<string> _embeddedRegistryProvider;

    public ModelRegistryService(string registryPath, Func<string> embeddedRegistryProvider)
    {
        _registryPath = registryPath;
        _embeddedRegistryProvider = embeddedRegistryProvider;
    }

    /// <summary>
    /// Loads the registry from disk, falling back to the embedded copy (and re-persisting it).
    /// A stale disk copy (fewer packs than the embedded catalog shipped with this build) is
    /// replaced so newly added packs always appear.
    /// </summary>
    public ModelRegistry Load()
    {
        try
        {
            if (File.Exists(_registryPath))
            {
                var json = File.ReadAllText(_registryPath);
                var disk = JsonSerializer.Deserialize<ModelRegistry>(json, JsonOpts);
                if (disk is not null && Validate(disk).Count == 0)
                {
                    var embedded = JsonSerializer.Deserialize<ModelRegistry>(_embeddedRegistryProvider(), JsonOpts);
                    if (embedded is null || disk.Packs.Count >= embedded.Packs.Count)
                        return disk;
                }
            }
        }
        catch (Exception)
        {
            // fall through to embedded registry
        }

        var fallback = JsonSerializer.Deserialize<ModelRegistry>(_embeddedRegistryProvider(), JsonOpts)
            ?? new ModelRegistry { Packs = [] };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_registryPath)!);
            File.WriteAllText(_registryPath, JsonSerializer.Serialize(fallback, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
            // Registry stays in memory when disk write fails.
        }
        return fallback;
    }

    /// <summary>Registry validation problems; empty list means valid.</summary>
    public static List<string> Validate(ModelRegistry registry)
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in registry.Packs)
        {
            if (string.IsNullOrWhiteSpace(pack.Id)) problems.Add("A pack has no Id.");
            else if (!seen.Add(pack.Id)) problems.Add($"Duplicate pack id: {pack.Id}");
            if (string.IsNullOrWhiteSpace(pack.Sha256)) problems.Add($"Pack {pack.Id}: missing Sha256.");
            else if (pack.Sha256.Length != 64 || pack.Sha256.Any(c => !Uri.IsHexDigit(c)))
                problems.Add($"Pack {pack.Id}: Sha256 must be 64 hex characters.");
            if (pack.Urls.Count == 0) problems.Add($"Pack {pack.Id}: no download URL.");
            if (string.IsNullOrWhiteSpace(pack.FileName)) problems.Add($"Pack {pack.Id}: no FileName.");
        }
        return problems;
    }
}
