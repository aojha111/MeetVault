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
        try
        {
            ZipFile.ExtractToDirectory(zipPath, installDir, overwriteFiles: true);

            // Flatten wrapper folders (piper/, Release/, ffmpeg-*-build/) and lift the
            // declared executable with its sibling DLLs to the pack root.
            ArchiveLayout.Normalize(installDir, pack.ExecutableRelativePath);

            if (!string.IsNullOrEmpty(pack.ExecutableRelativePath)
                && !File.Exists(Path.Combine(installDir, pack.ExecutableRelativePath)))
            {
                var listing = string.Join(", ", Directory.GetFileSystemEntries(installDir)
                    .Select(p => Path.GetFileName(p))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Take(12));
                throw new InvalidOperationException(
                    $"Archive for {pack.Id} extracted, but expected file '{pack.ExecutableRelativePath}' was not found. Extracted top-level entries: {listing}");
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException)
        {
            // Remove the half-installed directory so the pack stays cleanly "Not installed".
            try { Directory.Delete(installDir, recursive: true); } catch (IOException) { }
            throw new InvalidOperationException(
                $"Installation of {pack.DisplayName} failed: {ex.Message}", ex);
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
