namespace MeetVault.Infrastructure;

/// <summary>
/// Normalizes extracted runtime-pack layouts so the declared executable always ends up at
/// the pack root, no matter how the upstream archive is structured. Known layouts:
/// <list type="bullet">
/// <item>Piper: single wrapper folder <c>piper/</c> (with espeak-ng-data beside the exe).</item>
/// <item>FFmpeg: wrapper folder plus <c>bin/ffmpeg.exe</c> and <c>doc/</c> pages.</item>
/// <item>whisper.cpp: single wrapper folder <c>Release/</c> with all DLLs beside the exe.</item>
/// <item>llama.cpp: flat archive, no wrapper.</item>
/// </list>
/// </summary>
public static class ArchiveLayout
{
    /// <summary>
    /// Flattens single-folder wrappers and lifts the declared executable (and its sibling
    /// files) to the pack root. Directory trees that ship beside the executable (e.g.
    /// piper's espeak-ng-data) are preserved relative to it.
    /// </summary>
    public static void Normalize(string installDir, string? expectedExecutable)
    {
        HoistSingleFolderWrappers(installDir);

        if (string.IsNullOrWhiteSpace(expectedExecutable))
            return;

        var exeName = Path.GetFileName(expectedExecutable);
        if (File.Exists(Path.Combine(installDir, exeName)))
            return;

        var nested = FindFile(installDir, exeName);
        if (nested is null)
            return; // Validation in InstallZip reports this with a diagnostic listing.

        var containingDir = Path.GetDirectoryName(nested);
        if (string.IsNullOrEmpty(containingDir) ||
            PathsEqual(containingDir, installDir))
            return;

        // Lift every loose file from the executable's directory to the pack root so
        // dependent native DLLs stay adjacent to the exe.
        foreach (var file in Directory.GetFiles(containingDir))
        {
            try { File.Move(file, Path.Combine(installDir, Path.GetFileName(file)), overwrite: true); }
            catch (IOException) { }
        }
        TryDeleteIfEmpty(containingDir);
    }

    /// <summary>
    /// Repeatedly collapses a lone child directory into its parent while the parent holds
    /// nothing else (piper/, Release/, ffmpeg-9.0.1-essentials_build/ wrappers).
    /// </summary>
    private static void HoistSingleFolderWrappers(string dir)
    {
        while (true)
        {
            FileSystemInfo[] entries;
            try { entries = new DirectoryInfo(dir).GetFileSystemInfos(); }
            catch (IOException) { return; }

            if (entries.Length != 1 || entries[0] is FileInfo)
                return; // Nothing to hoist (zero, multiple, or a lone loose file).

            var inner = entries[0].FullName;
            foreach (var child in Directory.GetFileSystemEntries(inner))
            {
                try { MoveEntry(child, Path.Combine(dir, Path.GetFileName(child))); }
                catch (IOException) { return; }
            }
            try { Directory.Delete(inner); }
            catch (IOException) { return; }
        }
    }

    private static void MoveEntry(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            if (!Directory.Exists(destination))
                Directory.Move(source, destination);
            return;
        }
        File.Move(source, destination, overwrite: true);
    }

    private static string? FindFile(string dir, string fileName)
    {
        try
        {
            return Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void TryDeleteIfEmpty(string dir)
    {
        try
        {
            if (Directory.GetFileSystemEntries(dir).Length == 0)
                Directory.Delete(dir);
        }
        catch (IOException) { }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
