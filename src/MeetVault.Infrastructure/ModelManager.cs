using System.Text.Json.Serialization;
using MeetVault.Core;

namespace MeetVault.Infrastructure;

/// <summary>
/// Downloads, verifies (SHA-256), installs, removes and locates model/runtime packs from
/// the registry. Downloads are resumable and verified before anything is installed.
/// Only registry-declared executables are ever executed; downloaded scripts are never run.
/// </summary>
public sealed partial class ModelManager : IModelManager
{
    private readonly AppPaths _paths;
    private readonly ModelRegistryService _registryService;
    private readonly Action<string> _log;
    private readonly Func<bool> _offlineMode;

    public ModelManager(AppPaths paths, ModelRegistryService registryService, Func<bool> offlineMode, Action<string>? log = null)
    {
        _paths = paths;
        _registryService = registryService;
        _offlineMode = offlineMode;
        _log = log ?? (_ => { });
    }

    public IReadOnlyList<ModelPackState> GetCatalog()
    {
        var registry = _registryService.Load();
        var list = new List<ModelPackState>();
        foreach (var pack in registry.Packs)
        {
            var marker = ReadMarker(pack.Id);
            list.Add(new ModelPackState
            {
                Pack = pack,
                IsInstalled = marker is not null,
                InstalledVersion = marker?.Version,
                InstalledAt = marker?.InstalledAt,
                SizeOnDiskBytes = marker?.SizeBytes ?? 0,
            });
        }
        return list;
    }

    public ModelPackState? GetPack(string packId) =>
        GetCatalog().FirstOrDefault(p => p.Pack.Id.Equals(packId, StringComparison.OrdinalIgnoreCase));

    public async Task InstallAsync(string packId, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        if (_offlineMode())
            throw new InvalidOperationException("Offline Mode is enabled. Disable it in Settings to download packs.");

        var pack = _registryService.Load().Find(packId)
            ?? throw new InvalidOperationException($"Unknown pack '{packId}'.");
        SanityCheck(pack);

        var downloadPath = Path.Combine(_paths.DownloadsDir, pack.FileName);
        Directory.CreateDirectory(_paths.DownloadsDir);

        progress?.Report(0.01);
        await DownloadAsync(pack, downloadPath, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(0.85);
        var sha = await HashUtil.Sha256FileAsync(downloadPath, cancellationToken).ConfigureAwait(false);
        if (!sha.Equals(pack.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(downloadPath); } catch (IOException) { }
            throw new InvalidOperationException(
                $"Checksum mismatch for {pack.DisplayName}. Expected {pack.Sha256[..12]}…, got {sha[..12]}…. The download was deleted. Please retry.");
        }
        _log($"Checksum OK for {pack.FileName}.");

        progress?.Report(0.9);
        var installDir = _paths.InstalledPackDirectory(pack.Id);
        if (pack.ExtractZip)
        {
            InstallZip(pack, downloadPath, installDir);
        }
        else
        {
            Directory.CreateDirectory(installDir);
            File.Copy(downloadPath, Path.Combine(installDir, pack.FileName), overwrite: true);
        }

        await InstallExtraFilesAsync(pack, installDir, progress, cancellationToken).ConfigureAwait(false);

        // Keep a copy of single-file model weights in the models/ tree (predictable layout).
        if (!pack.ExtractZip && pack.KindEnum is ModelKind.WhisperModel or ModelKind.LlmModel or ModelKind.TtsVoice)
        {
            Directory.CreateDirectory(ModelsDirFor(pack));
            File.Copy(downloadPath, Path.Combine(ModelsDirFor(pack), pack.FileName), overwrite: true);
            // Sidecar files (e.g. Piper voice .onnx.json) live next to the model copy.
            foreach (var extra in pack.ExtraFiles)
            {
                var installedExtra = Path.Combine(installDir, extra.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(installedExtra))
                    File.Copy(installedExtra, Path.Combine(ModelsDirFor(pack), Path.GetFileName(extra.RelativePath)), overwrite: true);
            }
        }

        WriteMarker(pack.Id, new InstalledMarker
        {
            Id = pack.Id,
            Version = pack.Version,
            InstalledAt = DateTimeOffset.Now,
            SizeBytes = new FileInfo(downloadPath).Length,
        });

        try { File.Delete(downloadPath); } catch (IOException) { /* cache cleanup is best-effort */ }
        progress?.Report(1.0);
        _log($"Installed {pack.DisplayName} v{pack.Version}.");
    }

    public void MarkInstalledFromLocalFile(string packId, string filePath)
    {
        var pack = _registryService.Load().Find(packId)
            ?? throw new InvalidOperationException($"Unknown pack '{packId}'.");
        var installDir = _paths.InstalledPackDirectory(packId);
        Directory.CreateDirectory(installDir);
        File.Copy(filePath, Path.Combine(installDir, pack.FileName), overwrite: true);
        WriteMarker(pack.Id, new InstalledMarker
        {
            Id = pack.Id,
            Version = pack.Version,
            InstalledAt = DateTimeOffset.Now,
            SizeBytes = new FileInfo(filePath).Length,
            SideLoaded = true,
        });
    }

    public void Remove(string packId) => RemoveCore(packId);

    public string? GetInstalledModelFilePath(string packId) => ResolveModelFile(packId);

    public string? GetInstalledRuntimeDirectory(string packId)
    {
        if (ReadMarker(packId) is null) return null;
        var dir = _paths.InstalledPackDirectory(packId);
        return Directory.Exists(dir) ? dir : null;
    }
}
