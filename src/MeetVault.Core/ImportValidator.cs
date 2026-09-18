namespace MeetVault.Core;

/// <summary>Validates an imported recording path before the pipeline touches it.</summary>
public static class ImportValidator
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".m4v", ".mpg", ".mpeg", ".ts", ".flv",
        ".mp3", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".flac", ".wma",
    };

    /// <summary>Returns an error message when the file must not be imported, otherwise null.</summary>
    public static string? Validate(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "File path is empty.";
        if (filePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return "File path contains invalid characters.";
        try
        {
            if (!File.Exists(filePath))
                return $"File not found: {filePath}";
            var ext = Path.GetExtension(filePath);
            if (!AllowedExtensions.Contains(ext))
                return $"Unsupported file type '{ext}'. Supported: video (mp4, mkv, mov, avi, webm...) and audio (mp3, wav, m4a...).";
            var fi = new FileInfo(filePath);
            if (fi.Length == 0)
                return "File is empty.";
            if (fi.Length > 20L * 1024 * 1024 * 1024)
                return "File exceeds the 20 GB import limit.";
        }
        catch (Exception ex)
        {
            return $"Cannot access file: {ex.Message}";
        }
        return null;
    }
}
