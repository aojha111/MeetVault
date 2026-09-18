using System.Text.Json;
using MeetVault.Core;
using MeetVault.Infrastructure;
using Xunit;

namespace MeetVault.Tests;

public class EmbeddedCpuRegistryTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static ModelRegistry Embedded() =>
        JsonSerializer.Deserialize<ModelRegistry>(EmbeddedModelRegistry.Json, JsonOpts)!;

    [Fact]
    public void EmbeddedRegistry_IsValid()
    {
        var registry = Embedded();
        Assert.NotEmpty(registry.Packs);
        Assert.Empty(ModelRegistryService.Validate(registry));
    }

    [Fact]
    public void EmbeddedRegistry_ContainsAllCpuRuntimeAndModelPacks()
    {
        var registry = Embedded();
        Assert.Equal(12, registry.Packs.Count);
        Assert.NotNull(registry.Find("runtime-ffmpeg"));
        Assert.NotNull(registry.Find("runtime-whisper-cpu"));
        Assert.NotNull(registry.Find("runtime-llama-cpu"));
        Assert.NotNull(registry.Find("runtime-piper-tts"));
        Assert.NotNull(registry.Find("whisper-tiny-multilingual"));
        Assert.NotNull(registry.Find("whisper-base-multilingual"));
        Assert.NotNull(registry.Find("whisper-small-multilingual"));
        Assert.NotNull(registry.Find("whisper-vad-silero"));
        Assert.NotNull(registry.Find("qwen2.5-0.5b-instruct-q4km"));
        Assert.NotNull(registry.Find("qwen2.5-1.5b-instruct-q4km"));
        Assert.NotNull(registry.Find("qwen2.5-3b-instruct-q4km"));
        Assert.NotNull(registry.Find("piper-voice-en-lessac-medium"));
    }

    [Fact]
    public void EmbeddedRegistry_FitsCpuOnlyMachines_Within16GbRam()
    {
        foreach (var pack in Embedded().Packs)
        {
            Assert.True(pack.MinRamGb <= 16, $"{pack.Id} must run within 16 GB RAM");
            Assert.True(pack.SizeBytes > 0, $"{pack.Id} must declare a download size");
            Assert.All(pack.Urls, u => Assert.StartsWith("https://", u));
        }
    }

    [Theory]
    [InlineData("runtime-ffmpeg", "ffmpeg.exe")]
    [InlineData("runtime-whisper-cpu", "whisper-cli.exe")]
    [InlineData("runtime-llama-cpu", "llama-server.exe")]
    [InlineData("runtime-piper-tts", "piper.exe")]
    public void RuntimePacks_DeclareTheirExecutable(string packId, string exe)
    {
        var pack = Embedded().Find(packId)!;
        Assert.True(pack.ExtractZip);
        Assert.Equal(exe, pack.ExecutableRelativePath);
    }

    [Fact]
    public void PiperVoicePack_DeclaresJsonSidecar()
    {
        var pack = Embedded().Find("piper-voice-en-lessac-medium")!;
        var sidecar = Assert.Single(pack.ExtraFiles);
        Assert.EndsWith(".onnx.json", sidecar.RelativePath);
        Assert.NotNull(sidecar.Url);
        Assert.Equal(64, sidecar.Sha256?.Length);
    }
}

public class ModelRegistryFreshnessTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _dir;

    public ModelRegistryFreshnessTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "meetvault-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static ModelPack Pack(string id) => new()
    {
        Id = id,
        DisplayName = id,
        Kind = "runtime",
        Version = "1",
        Urls = ["https://example.invalid/" + id],
        Sha256 = new string('a', 64),
        FileName = id + ".zip",
    };

    [Fact]
    public void Load_ReplacesStaleDiskRegistryWithEmbeddedCatalog()
    {
        var path = Path.Combine(_dir, "model-registry.json");
        var stale = new ModelRegistry { Packs = [Pack("old-pack")] };
        File.WriteAllText(path, JsonSerializer.Serialize(stale));

        var loaded = new ModelRegistryService(path, () => EmbeddedModelRegistry.Json).Load();

        Assert.Contains(loaded.Packs, p => p.Id == "runtime-ffmpeg");
        Assert.DoesNotContain(loaded.Packs, p => p.Id == "old-pack");
    }

    [Fact]
    public void Load_KeepsCustomPacksWhenDiskRegistryIsSuperset()
    {
        var path = Path.Combine(_dir, "model-registry.json");
        var extended = JsonSerializer.Deserialize<ModelRegistry>(EmbeddedModelRegistry.Json, JsonOpts)!;
        extended.Packs.Add(Pack("custom-local-pack"));
        File.WriteAllText(path, JsonSerializer.Serialize(extended));

        var loaded = new ModelRegistryService(path, () => EmbeddedModelRegistry.Json).Load();

        Assert.Contains(loaded.Packs, p => p.Id == "custom-local-pack");
        Assert.Contains(loaded.Packs, p => p.Id == "runtime-ffmpeg");
    }
}

public class HardwareRecommendationTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    private static HardwareInfo Info(int cores, double ramGb) =>
        new() { CpuLogicalCores = cores, RamGb = ramGb };

    [Theory]
    [InlineData(16, "qwen2.5-3b-instruct-q4km")]
    [InlineData(8, "qwen2.5-1.5b-instruct-q4km")]
    [InlineData(4, "qwen2.5-0.5b-instruct-q4km")]
    public void RecommendedLlm_ScalesWithRam(double ramGb, string expected) =>
        Assert.Equal(expected, Info(4, ramGb).RecommendedLlmPackId);

    [Theory]
    [InlineData(4, "whisper-base-multilingual")]
    [InlineData(8, "whisper-base-multilingual")]
    [InlineData(2, "whisper-tiny-multilingual")]
    public void RecommendedWhisper_ScalesWithCores(int cores, string expected) =>
        Assert.Equal(expected, Info(cores, 16).RecommendedWhisperPackId);

    [Fact]
    public void RecommendedPacks_ExistInEmbeddedCpuRegistry()
    {
        var registry = JsonSerializer.Deserialize<ModelRegistry>(EmbeddedModelRegistry.Json, JsonOpts)!;
        var info = Info(8, 16);
        Assert.NotNull(registry.Find(info.RecommendedWhisperPackId));
        Assert.NotNull(registry.Find(info.RecommendedLlmPackId));
        Assert.True(info.MeetsRecommendedSpec);
    }
}
