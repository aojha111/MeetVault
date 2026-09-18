using System.Management;
using MeetVault.Core;

namespace MeetVault.Infrastructure;

/// <summary>Reads CPU/RAM/GPU information via WMI; degrades gracefully on failure.</summary>
public sealed class HardwareInfoProvider : IHardwareInfoProvider
{
    public HardwareInfo Detect()
    {
        var info = new HardwareInfo
        {
            CpuLogicalCores = Environment.ProcessorCount,
        };

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var o in searcher.Get())
            {
                info.RamGb = Convert.ToDouble(o["TotalPhysicalMemory"]) / (1024 * 1024 * 1024);
                break;
            }
        }
        catch (Exception) { /* WMI unavailable */ }

        if (info.RamGb <= 0)
        {
            info.RamGb = GetRamGbViaGlobalMemoryStatus();
        }

        try
        {
            using var gpuSearcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            double bestVram = 0;
            string bestName = string.Empty;
            foreach (var o in gpuSearcher.Get())
            {
                var name = o["Name"]?.ToString() ?? string.Empty;
                var vram = o["AdapterRAM"] is null ? 0 : Convert.ToDouble(o["AdapterRAM"]);
                // AdapterRAM overflows for >4GB cards (uint32); treat negative as large.
                if (vram < 0) vram = 8 * 1024.0 * 1024 * 1024;
                if (vram > bestVram)
                {
                    bestVram = vram;
                    bestName = name;
                }
            }
            info.GpuName = bestName;
            info.GpuVramGb = bestVram / (1024 * 1024 * 1024);
        }
        catch (Exception) { /* WMI unavailable */ }

        return info;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private static double GetRamGbViaGlobalMemoryStatus()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? status.ullTotalPhys / (1024.0 * 1024 * 1024) : 8;
    }
}
