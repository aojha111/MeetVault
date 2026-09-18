namespace MeetVault.Core;

/// <summary>Detects hardware capabilities at startup and recommends model profiles.</summary>
public sealed class HardwareInfo
{
    public string OsVersion { get; set; } = Environment.OSVersion.VersionString;
    public int CpuLogicalCores { get; set; } = Environment.ProcessorCount;
    public double RamGb { get; set; }
    public string GpuName { get; set; } = string.Empty;
    public double GpuVramGb { get; set; }

    /// <summary>Recommended LLM model id for this machine (registry ids). All are CPU-only tiers.</summary>
    public string RecommendedLlmPackId => RamGb >= 16
        ? "qwen2.5-3b-instruct-q4km"   // ~4 GB usage — best quality on 16 GB
        : RamGb >= 8
            ? "qwen2.5-1.5b-instruct-q4km" // ~2.5 GB usage
            : "qwen2.5-0.5b-instruct-q4km"; // ~1.2 GB usage, runs anywhere

    /// <summary>Recommended Whisper model pack for this machine.</summary>
    public string RecommendedWhisperPackId => CpuLogicalCores >= 4
        ? "whisper-base-multilingual"
        : "whisper-tiny-multilingual";

    /// <summary>True when the machine comfortably fits the recommended analysis model.</summary>
    public bool MeetsRecommendedSpec => RamGb >= 16 && CpuLogicalCores >= 4;

    public string Describe() =>
        $"{CpuLogicalCores} logical cores, {RamGb:0.#} GB RAM" + (string.IsNullOrEmpty(GpuName) ? "" : $", GPU: {GpuName}");
}

/// <summary>Provides hardware information.</summary>
public interface IHardwareInfoProvider
{
    HardwareInfo Detect();
}
