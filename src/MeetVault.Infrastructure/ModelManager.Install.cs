namespace MeetVault.Infrastructure;

public sealed partial class ModelManager
{
    private void RemoveCore(string packId)
    {
        var installDir = _paths.InstalledPackDirectory(packId);
        if (Directory.Exists(installDir))
        {
            try { Directory.Delete(installDir, recursive: true); } catch (IOException) { }
        }

        var pack = _registryService.Load().Find(packId);
        if (pack is not null && !pack.ExtractZip && pack.KindEnum is not Core.ModelKind.Runtime)
        {
            try { File.Delete(Path.Combine(ModelsDirFor(pack), pack.FileName)); } catch (IOException) { }
            foreach (var extra in pack.ExtraFiles)
            {
                var extraName = Path.GetFileName(extra.RelativePath);
                try { File.Delete(Path.Combine(ModelsDirFor(pack), extraName)); } catch (IOException) { }
            }
        }

        var marker = _paths.InstalledMarkerPath(packId);
        try { if (File.Exists(marker)) File.Delete(marker); } catch (IOException) { }
        _log($"Removed pack {packId}.");
    }

    private string? ResolveModelFile(string packId)
    {
        var pack = _registryService.Load().Find(packId);
        if (pack is null || ReadMarker(packId) is null) return null;

        if (!pack.ExtractZip)
        {
            var copy = Path.Combine(ModelsDirFor(pack), pack.FileName);
            if (File.Exists(copy)) return copy;
            var original = Path.Combine(_paths.InstalledPackDirectory(packId), pack.FileName);
            return File.Exists(original) ? original : null;
        }

        foreach (var extra in pack.ExtraFiles)
        {
            var candidate = Path.Combine(_paths.InstalledPackDirectory(packId), extra.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private void WriteMarker(string packId, InstalledMarker marker)
    {
        Directory.CreateDirectory(_paths.InstalledMarkerDir);
        File.WriteAllText(_paths.InstalledMarkerPath(packId), System.Text.Json.JsonSerializer.Serialize(marker, MarkerJsonOptions));
    }

    private InstalledMarker? ReadMarker(string packId)
    {
        var path = _paths.InstalledMarkerPath(packId);
        try
        {
            if (!File.Exists(path)) return null;
            return System.Text.Json.JsonSerializer.Deserialize<InstalledMarker>(File.ReadAllText(path), MarkerJsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions MarkerJsonOptions = new() { WriteIndented = true };
}

/// <summary>Persisted proof that a pack was downloaded, verified and extracted.</summary>
public sealed class InstalledMarker
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("installedAt")]
    public DateTimeOffset InstalledAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("sideLoaded")]
    public bool SideLoaded { get; set; }
}
