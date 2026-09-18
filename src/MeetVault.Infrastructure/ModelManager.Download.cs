using System.IO.Compression;
using System.Net.Http.Headers;

namespace MeetVault.Infrastructure;

public sealed partial class ModelManager
{
    private string ModelsDirFor(Core.ModelPack pack) => pack.KindEnum switch
    {
        Core.ModelKind.WhisperModel => _paths.WhisperModelsDir,
        Core.ModelKind.LlmModel => _paths.LlmModelsDir,
        _ => _paths.TtsModelsDir,
    };

    private static void SanityCheck(Core.ModelPack pack)
    {
        if (pack.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException($"Pack {pack.Id} has an invalid file name.");
        foreach (var url in pack.Urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                throw new InvalidOperationException($"Pack {pack.Id} has an invalid download URL.");
        }
    }

    private static async Task DownloadAsync(Core.ModelPack pack, string downloadPath, IProgress<double>? progress, CancellationToken ct)
    {
        Exception? lastError = null;
        foreach (var url in pack.Urls)
        {
            try
            {
                await DownloadWithResumeAsync(url, downloadPath, pack.SizeBytes, progress, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        throw new InvalidOperationException($"Download failed for {pack.DisplayName}: {lastError?.Message}", lastError);
    }

    private static void InstallZip(Core.ModelPack pack, string zipPath, string installDir)
    {
        if (Directory.Exists(installDir))
        {
            try { Directory.Delete(installDir, recursive: true); } catch (IOException) { }
        }
        Directory.CreateDirectory(installDir);
        ZipFile.ExtractToDirectory(zipPath, installDir, overwriteFiles: true);

        // Some archives wrap everything in one folder; hoist a single child folder up if present.
        var dirs = Directory.GetDirectories(installDir);
        var files = Directory.GetFiles(installDir);
        if (files.Length == 0 && dirs.Length == 1)
        {
            var inner = dirs[0];
            foreach (var entry in Directory.GetFileSystemEntries(inner))
            {
                Directory.Move(entry, Path.Combine(installDir, Path.GetFileName(entry)));
            }
            Directory.Delete(inner);
        }

        if (!string.IsNullOrEmpty(pack.ExecutableRelativePath)
            && !File.Exists(Path.Combine(installDir, pack.ExecutableRelativePath)))
        {
            throw new InvalidOperationException(
                $"Archive for {pack.Id} extracted but expected file '{pack.ExecutableRelativePath}' was not found.");
        }
    }

    private async Task InstallExtraFilesAsync(Core.ModelPack pack, string installDir, IProgress<double>? progress, CancellationToken ct)
    {
        foreach (var extra in pack.ExtraFiles)
        {
            var target = Path.Combine(installDir, extra.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (extra.Url is null)
            {
                if (!File.Exists(target))
                    throw new InvalidOperationException($"Pack {pack.Id}: missing bundled file '{extra.RelativePath}' after extraction.");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var downloadPath = Path.Combine(_paths.DownloadsDir, extra.DownloadFileName ?? Path.GetFileName(extra.RelativePath));
            await DownloadWithResumeAsync(extra.Url, downloadPath, extra.SizeBytes, progress, ct).ConfigureAwait(false);
            if (extra.Sha256 is not null && !await HashUtil.VerifyAsync(downloadPath, extra.Sha256, ct).ConfigureAwait(false))
                throw new InvalidOperationException($"Checksum mismatch for {extra.RelativePath}.");
            File.Copy(downloadPath, target, overwrite: true);
            try { File.Delete(downloadPath); } catch (IOException) { }
        }
    }
}
