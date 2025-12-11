using System.Collections.Generic;
using System.Windows.Media;

namespace SystemProcesses.Desktop.Models;

public class ProcessInfo
{
    public int Pid { get; set; }
    public string Name { get; set; } = string.Empty;
    public double CpuPercentage { get; set; }
    public long MemoryBytes { get; set; }
    public long VirtualMemoryBytes { get; set; }
    public string Parameters { get; set; } = string.Empty;
    public bool IsService { get; set; }
    public int ParentPid { get; set; }
    public ImageSource? Icon { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }

    // Initialized once, reused forever.
    public List<ProcessInfo> Children { get; } = new List<ProcessInfo>();

    // Helper for differential updates
    public void Update(double cpu, long mem, long virtualMem, int threads, int handles)
    {
        CpuPercentage = cpu;
        MemoryBytes = mem;
        VirtualMemoryBytes = virtualMem;
        ThreadCount = threads;
        HandleCount = handles;
    }
}

public struct SystemStats
{
    public int ProcessCount;

    public int ThreadCount;

    public int HandleCount;

    public double TotalCpu;

    // Working Set (Physical used by processes)
    public long TotalMemory;
    // Installed RAM
    public long TotalPhysicalMemory;
    // Free RAM
    public long AvailablePhysicalMemory;
    // RAM + PageFile
    public long TotalCommitLimit;
    // Free Commit
    public long AvailableCommitLimit;
    // System-wide IO Throughput
    public long TotalIoBytesPerSec;
    public double DiskActivePercent;
}