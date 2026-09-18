using System.Security.Cryptography;

namespace MeetVault.Infrastructure;

/// <summary>SHA-256 verification helpers used by the model manager.</summary>
public static class HashUtil
{
    /// <summary>Computes the lowercase hex SHA-256 of a file.</summary>
    public static async Task<string> Sha256FileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Verifies a file's SHA-256 against the expected hex digest.</summary>
    public static async Task<bool> VerifyAsync(string path, string expectedSha256, CancellationToken ct = default)
    {
        var actual = await Sha256FileAsync(path, ct).ConfigureAwait(false);
        return actual.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
