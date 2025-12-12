using System.Collections.Generic;

namespace SystemProcesses.Desktop.Models;

public class ProcessInfo
{
    private int pid;

    public int Pid
    {
        get => pid;
        set
        {
            pid = value;
            PidText = pid.ToString();
        }
    }

    // Used for display purposes to avoid boxing in UI lists
    // Also for filtering as string
    public string PidText { get; private set; } = null!;

    public string Name { get; set; } = string.Empty;
    public double CpuPercentage { get; set; }
    public long MemoryBytes { get; set; }
    public long VirtualMemoryBytes { get; set; }
    public string Parameters { get; set; } = string.Empty;
    public bool IsService { get; set; }
    public int ParentPid { get; set; }
    public string? ProcessPath { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }

    // Unique timestamp (FileTime) from the OS kernel
    public long CreateTime { get; set; }

    // Initialized once, reused forever.
    public List<ProcessInfo> Children { get; } = [];

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

public struct DriveStats
{
    public char Letter;
    public long TotalSize;
    public long AvailableFreeSpace;
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

    public int DriveCount;

    public DriveStats[] Drives;

    // Fixed-size array for Top 5 to avoid List allocations
    public ProcessInfo?[] Top5Processes;
}