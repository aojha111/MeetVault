using System.Net.Http.Headers;

namespace MeetVault.Infrastructure;

public sealed partial class ModelManager
{
    /// <summary>Resumable HTTP download into the local cache.</summary>
    internal static async Task DownloadWithResumeAsync(string url, string targetPath, long expectedSize, IProgress<double>? progress, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MeetVault/1.0");

        long existing = File.Exists(targetPath) ? new FileInfo(targetPath).Length : 0;
        if (expectedSize > 0 && existing > 0 && existing >= expectedSize)
        {
            progress?.Report(0.84); // already fully cached from a previous attempt
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (existing > 0 && (int)response.StatusCode == 200)
        {
            existing = 0; // server ignored the range; restart
        }
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? -1;
        if (total > 0 && existing > 0) total += existing;

        await using var httpStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(targetPath, FileMode.Append, FileAccess.Write, FileShare.None, 512 * 1024);
        var buffer = new byte[512 * 1024];
        long written = existing;
        int read;
        while ((read = await httpStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            written += read;
            if (total > 0) progress?.Report(Math.Clamp(0.02 + 0.82 * written / total, 0.02, 0.84));
        }
    }
}
