using MeetVault.Infrastructure;
using Xunit;

namespace MeetVault.Tests;

/// <summary>
/// Verifies that extracted runtime-pack archives are normalized so the declared
/// executable always lands at the pack root — regardless of upstream zip layout
/// (piper's wrapper folder, ffmpeg's bin/ subfolder, whisper's Release/ folder,
/// llama's flat layout).
/// </summary>
public class ArchiveLayoutTests : IDisposable
{
    private readonly string _dir;

    public ArchiveLayoutTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "meetvault-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Write(params string[] relativePath)
    {
        var path = Path.Combine([_dir, .. relativePath]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void SingleWrapperFolder_IsFlattened_LikePiper()
    {
        Write("pack", "piper", "piper.exe");
        Write("pack", "piper", "piper_phonemize.dll");
        Write("pack", "piper", "espeak-ng-data", "en_dict");

        ArchiveLayout.Normalize(Path.Combine(_dir, "pack"), "piper.exe");

        Assert.True(File.Exists(Path.Combine(_dir, "pack", "piper.exe")));
        Assert.True(File.Exists(Path.Combine(_dir, "pack", "piper_phonemize.dll")));
        Assert.True(File.Exists(Path.Combine(_dir, "pack", "espeak-ng-data", "en_dict")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "pack", "piper")));
    }

    [Fact]
    public void NestedBinFolder_LiftsExeAndSiblings_LikeFfmpeg()
    {
        Write("pack", "ffmpeg-9.0.1-essentials_build", "bin", "ffmpeg.exe");
        Write("pack", "ffmpeg-9.0.1-essentials_build", "bin", "ffprobe.exe");
        Write("pack", "ffmpeg-9.0.1-essentials_build", "doc", "faq.html");
        Write("pack", "ffmpeg-9.0.1-essentials_build", "LICENSE.txt");

        ArchiveLayout.Normalize(Path.Combine(_dir, "pack"), "ffmpeg.exe");

        Assert.True(File.Exists(Path.Combine(_dir, "pack", "ffmpeg.exe")));
        Assert.True(File.Exists(Path.Combine(_dir, "pack", "ffprobe.exe")));
        Assert.True(File.Exists(Path.Combine(_dir, "pack", "doc", "faq.html")));
        Assert.True(File.Exists(Path.Combine(_dir, "pack", "LICENSE.txt")));
    }

    [Fact]
    public void WrappedReleaseFolder_IsFlattened_LikeWhisperCpp()
    {
        Write("pack", "Release", "whisper-cli.exe");
        Write("pack", "Release", "ggml.dll");

        ArchiveLayout.Normalize(Path.Combine(_dir, "pack"), "whisper-cli.exe");

        Assert.True(File.Exists(Path.Combine(_dir, "pack", "whisper-cli.exe")));
        Assert.True(File.Exists(Path.Combine(_dir, "pack", "ggml.dll")));
    }

    [Fact]
    public void FlatArchive_IsLeftUnchanged_LikeLlamaCpp()
    {
        Write("pack", "llama-server.exe");
        Write("pack", "ggml.dll");

        ArchiveLayout.Normalize(Path.Combine(_dir, "pack"), "llama-server.exe");

        Assert.True(File.Exists(Path.Combine(_dir, "pack", "llama-server.exe")));
        Assert.True(File.Exists(Path.Combine(_dir, "pack", "ggml.dll")));
        Assert.DoesNotContain(Directory.GetDirectories(_dir, "*", SearchOption.AllDirectories),
            d => Path.GetFileName(d) is "bin" or "Release");
    }

    [Fact]
    public void DoubleWrapper_IsFlattenedInOnePass()
    {
        Write("pack", "outer", "inner", "tool.exe");

        ArchiveLayout.Normalize(Path.Combine(_dir, "pack"), "tool.exe");

        Assert.True(File.Exists(Path.Combine(_dir, "pack", "tool.exe")));
    }

    [Fact]
    public void MissingExecutable_IsLeftForValidationToReport()
    {
        Write("pack", "something-else.txt");

        ArchiveLayout.Normalize(Path.Combine(_dir, "pack"), "tool.exe");

        Assert.True(File.Exists(Path.Combine(_dir, "pack", "something-else.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "pack", "tool.exe")));
    }

    [Fact]
    public void NullExpectedExecutable_OnlyFlattensWrappers()
    {
        Write("pack", "wrapper", "file.txt");

        ArchiveLayout.Normalize(Path.Combine(_dir, "pack"), expectedExecutable: null);

        Assert.True(File.Exists(Path.Combine(_dir, "pack", "file.txt")));
    }
}
