using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Serilog;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Services;

public class ProcessService : IProcessService, IDisposable
{
    private struct ProcessHistory
    {
        public long TotalProcessorTime;
        public long TotalIoBytes;
    }

    private readonly Dictionary<int, ProcessInfo> activeProcesses = new(1024);
    private readonly Dictionary<int, ProcessHistory> prevProcessStats = new(1024);
    private readonly List<ProcessInfo> rootNodes = new(64);
    private readonly HashSet<int> servicePids = [];

    // Reusable buffers
    private readonly HashSet<int> currentPidsBuffer = new(1024);

    private readonly List<int> stoppedPidsBuffer = new(64);
    private readonly ProcessInfo?[] top5Buffer = new ProcessInfo?[5];

    // Reusable buffer for NtQuerySystemInformation
    private IntPtr buffer = IntPtr.Zero;

    private int bufferSize = 1024 * 1024;
    private long prevTicks = 0;

    // PDH Fields
    private IntPtr pdhQuery = IntPtr.Zero;

    private IntPtr pdhDiskIdleCounter = IntPtr.Zero;
    private bool isPdhInitialized = false;

    // Reusable comparison delegate to avoid allocation
    private static readonly Comparison<ProcessInfo> nameComparer =
        (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    public ProcessService()
    {
        buffer = Marshal.AllocHGlobal(bufferSize);
        InitializePdh();
    }

    private void InitializePdh()
    {
        try
        {
            // 1. Open Query
            int status = NativeMethods.PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out pdhQuery);

            if (status != 0)
            {
                return;
            }

            // 2. Try PhysicalDisk
            const string physPath = "\\PhysicalDisk(_Total)\\% Idle Time";
            status = NativeMethods.PdhAddEnglishCounter(pdhQuery, physPath, IntPtr.Zero, out pdhDiskIdleCounter);

            // 3. Fallback to LogicalDisk
            if (status != 0)
            {
                const string logPath = "\\LogicalDisk(_Total)\\% Idle Time";
                status = NativeMethods.PdhAddEnglishCounter(pdhQuery, logPath, IntPtr.Zero, out pdhDiskIdleCounter);
            }

            // 4. Initial Collect
            if (status == 0)
            {
                status = NativeMethods.PdhCollectQueryData(pdhQuery);

                if (status == 0)
                {
                    isPdhInitialized = true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PDH CRITICAL EXCEPTION: {message}", ex.Message);
        }
    }

    public async Task<(List<ProcessInfo> Roots, SystemStats Stats)> GetProcessTreeAsync()
    {
        // We return the raw list to avoid allocation.
        // The consumer MUST NOT iterate this list concurrently with the next call to GetProcessTreeAsync.
        // Given the UI "pull" model, this is safe.
        return await Task.Run(() =>
        {
            lock (activeProcesses)
            {
                RefreshServicePids();
                var stats = UpdateProcessSnapshot();
                RebuildTreeStructure();
                return (rootNodes, stats);
            }
        });
    }

    private unsafe void RefreshServicePids()
    {
        servicePids.Clear();
        IntPtr scmHandle = NativeMethods.OpenSCManagerW(null, null,
            NativeMethods.ScManagerConnect | NativeMethods.ScManagerEnumerateService);

        if (scmHandle == IntPtr.Zero)
        {
            return;
        }

        IntPtr buf = IntPtr.Zero;
        try
        {
            int bytesNeeded = 0;
            int servicesReturned = 0;
            int resumeHandle = 0;

            // First call to get size
            NativeMethods.EnumServicesStatusExW(scmHandle, NativeMethods.ScEnumProcessInfo,
                NativeMethods.ServiceWIN32, NativeMethods.ServiceStateAll,
                IntPtr.Zero, 0, out bytesNeeded, out servicesReturned, ref resumeHandle, null);

            if (bytesNeeded > 0)
            {
                buf = Marshal.AllocHGlobal(bytesNeeded);
                if (NativeMethods.EnumServicesStatusExW(scmHandle, NativeMethods.ScEnumProcessInfo,
                    NativeMethods.ServiceWIN32, NativeMethods.ServiceStateAll,
                    buf, bytesNeeded, out bytesNeeded, out servicesReturned, ref resumeHandle, null))
                {
                    byte* ptr = (byte*)buf;
                    int structSize = Marshal.SizeOf<NativeMethods.EnumServiceStatusProcess>();

                    for (int i = 0; i < servicesReturned; i++)
                    {
                        // Direct pointer access to avoid full struct marshalling
                        var serviceStruct = (NativeMethods.EnumServiceStatusProcess*)ptr;
                        int pid = serviceStruct->ServiceStatusProcess.dwProcessId;

                        if (pid > 0)
                        {
                            servicePids.Add(pid);
                        }
                        ptr += structSize;
                    }
                }
            }
        }
        finally
        {
            if (buf != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buf);
            }

            NativeMethods.CloseServiceHandle(scmHandle);
        }
    }

    private unsafe SystemStats UpdateProcessSnapshot()
    {
        int requiredSize = 0;
        int status = NativeMethods.NtQuerySystemInformation(
            NativeMethods.SystemProcessInformationValue,
            buffer,
            bufferSize,
            out requiredSize);

        if (status == NativeMethods.StatusInfoLengthMismatch)
        {
            Marshal.FreeHGlobal(buffer);
            bufferSize = requiredSize + (1024 * 1024); // Add 1MB padding
            buffer = Marshal.AllocHGlobal(bufferSize);
            status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemProcessInformationValue,
                buffer,
                bufferSize,
                out requiredSize);
        }

        // Initialize Stats
        var stats = new SystemStats();

        if (status != NativeMethods.StatusSuccess)
        {
            return stats;
        }

        if (isPdhInitialized)
        {
            int collectStatus = NativeMethods.PdhCollectQueryData(pdhQuery);

            NativeMethods.PdhFmtCountervalue value;
            int readStatus = NativeMethods.PdhGetFormattedCounterValue(
                pdhDiskIdleCounter,
                NativeMethods.PdhFmtDouble,
                IntPtr.Zero,
                out value);

            if (collectStatus == 0 && readStatus == 0 && value.CStatus == 0) // CStatus 0 is Valid
            {
                // Clamp to 0-100 range
                double idle = value.doubleValue;

                // Debug.WriteLine($"Raw Idle: {idle:F2}%"); // Uncomment to see raw values

                if (idle > 100)
                {
                    idle = 100;
                }

                if (idle < 0)
                {
                    idle = 0;
                }

                stats.DiskActivePercent = 100.0 - idle;
            }
            else
            {
                // Log failures only (to avoid spamming success)
                Debug.WriteLine($"PDH Read Fail -> Collect: 0x{collectStatus:X8}, Read: 0x{readStatus:X8}, CStatus: 0x{value.CStatus:X8}");
            }
        }

        var memStatus = NativeMethods.MemoryStatusEx.Default;
        if (NativeMethods.GlobalMemoryStatusEx(ref memStatus))
        {
            stats.TotalPhysicalMemory = (long)memStatus.ullTotalPhys;
            stats.AvailablePhysicalMemory = (long)memStatus.ullAvailPhys;
            stats.TotalCommitLimit = (long)memStatus.ullTotalPageFile;
            stats.AvailableCommitLimit = (long)memStatus.ullAvailPageFile;
        }

        long currentTicks = DateTime.UtcNow.Ticks;
        double deltaTime = (currentTicks - prevTicks);
        double deltaTimeSec = deltaTime / 10_000_000.0; // Ticks are 100ns
        prevTicks = currentTicks;

        currentPidsBuffer.Clear();
        long offset = 0;
        NativeMethods.SystemProcessInformation* ptr;

        long globalIoDelta = 0;

        do
        {
            ptr = (NativeMethods.SystemProcessInformation*)((byte*)buffer + offset);
            int pid = ptr->UniqueProcessId.ToInt32();
            currentPidsBuffer.Add(pid);

            // Extract Data
            long totalCpuTime = ptr->KernelTime + ptr->UserTime;
            long currentIoBytes = ptr->ReadTransferCount + ptr->WriteTransferCount + ptr->OtherTransferCount;
            long memBytes = (long)ptr->WorkingSetSize;
            // FIX: Use PrivatePageCount (Commit Size) instead of VirtualSize (Address Space)
            // VirtualSize includes unallocated reserved space (TB range on x64).
            long virtualBytes = (long)ptr->PrivatePageCount;
            int parentPid = ptr->InheritedFromUniqueProcessId.ToInt32();
            int threads = (int)ptr->NumberOfThreads;
            int handles = (int)ptr->HandleCount;

            // Calculate CPU Usage
            double cpuUsage = 0;
            long ioDelta = 0;

            if (prevProcessStats.TryGetValue(pid, out var history) && deltaTime > 0)
            {
                // CPU
                long deltaCpu = totalCpuTime - history.TotalProcessorTime;
                cpuUsage = (deltaCpu / (double)deltaTime) * 100.0;
                cpuUsage /= Environment.ProcessorCount;

                // IO
                if (currentIoBytes >= history.TotalIoBytes) // Check for overflow/restart
                {
                    ioDelta = currentIoBytes - history.TotalIoBytes;
                }
            }

            // Update History
            prevProcessStats[pid] = new ProcessHistory
            {
                TotalProcessorTime = totalCpuTime,
                TotalIoBytes = currentIoBytes
            };

            // Aggregate Stats - EXCLUDE System Idle Process (PID 0)
            // PID 0 represents unused CPU/Resources. Including it skews "Total CPU" to ~100%.
            if (pid != 0)
            {
                stats.ProcessCount++;
                stats.ThreadCount += threads;
                stats.HandleCount += handles;
                stats.TotalMemory += memBytes;
                stats.TotalCpu += cpuUsage;
                globalIoDelta += ioDelta;
            }

            // Check if service
            // Update/Add ProcessInfo (Include PID 0 here so it shows in the Tree)
            bool isService = servicePids.Contains(pid);

            if (activeProcesses.TryGetValue(pid, out var info))
            {
                // UPDATE existing (Zero Alloc)
                info.Update(cpuUsage, memBytes, virtualBytes, threads, handles);
                info.ParentPid = parentPid;
                info.IsService = isService; // Update service status (rarely changes, but possible)
            }
            else
            {
                string name;
                if (pid == 0)
                {
                    name = "System Idle Process";
                }
                else if (pid == 4)
                {
                    name = "System";
                }
                else
                {
                    // Zero-alloc string creation if possible, but we need a string for WPF.
                    // Marshal.PtrToStringUni is optimized in modern .NET.
                    name = Marshal.PtrToStringUni(ptr->ImageName.Buffer) ?? "Unknown";
                }

                var newInfo = new ProcessInfo
                {
                    Pid = pid,
                    Name = name,
                    ParentPid = parentPid,
                    IsService = isService,
                    ProcessPath = GetProcessPath(pid),
                    Parameters = GetCommandLine(pid) // Fetch once
                };
                newInfo.Update(cpuUsage, memBytes, virtualBytes, threads, handles);
                activeProcesses.Add(pid, newInfo);
            }

            if (ptr->NextEntryOffset == 0)
            {
                break;
            }

            offset += ptr->NextEntryOffset;
        } while (true);

        // Calculate IO Rate
        if (deltaTimeSec > 0)
        {
            stats.TotalIoBytesPerSec = (long)(globalIoDelta / deltaTimeSec);
        }

        // Remove stopped processes using pooled buffer
        stoppedPidsBuffer.Clear();
        foreach (var pid in activeProcesses.Keys)
        {
            if (!currentPidsBuffer.Contains(pid))
            {
                stoppedPidsBuffer.Add(pid);
            }
        }

        foreach (var pid in stoppedPidsBuffer)
        {
            activeProcesses.Remove(pid);
            prevProcessStats.Remove(pid); // Remove history
        }

        // ADDED: Calculate Top 5 CPU Processes (O(N) - Single Pass)
        Array.Clear(top5Buffer); // Reset buffer

        foreach (var process in activeProcesses.Values)
        {
            // Skip Idle and System for "Top Apps" context if desired,
            // but usually users want to see what's eating CPU, including System.
            // We skip Idle (PID 0) as it's not a real process usage.
            if (process.Pid == 0)
            {
                continue;
            }

            InsertIntoTop5(process);
        }

        stats.Top5Processes = top5Buffer;

        return stats;
    }

    private void InsertIntoTop5(ProcessInfo candidate)
    {
        // Simple insertion sort into fixed size array
        // We want descending order (Highest CPU at index 0)

        for (int i = 0; i < 5; i++)
        {
            var current = top5Buffer[i];

            if (current == null || candidate.CpuPercentage > current.CpuPercentage)
            {
                // Shift remaining items down
                for (int j = 4; j > i; j--)
                {
                    top5Buffer[j] = top5Buffer[j - 1];
                }

                // Insert
                top5Buffer[i] = candidate;
                break;
            }
        }
    }

    private void RebuildTreeStructure()
    {
        rootNodes.Clear();

        // Reset children without reallocating lists if possible,
        // but ProcessInfo.Children is a List<T>, so Clear() is O(N) but keeps capacity.
        foreach (var p in activeProcesses.Values)
        {
            p.Children.Clear();
        }

        foreach (var p in activeProcesses.Values)
        {
            if (p.ParentPid != 0 && activeProcesses.TryGetValue(p.ParentPid, out var parent))
            {
                parent.Children.Add(p);
            }
            else
            {
                rootNodes.Add(p);
            }
        }

        // Sort the tree
        SortTree(rootNodes);
    }

    private void SortTree(List<ProcessInfo> nodes)
    {
        nodes.Sort(nameComparer);
        foreach (var node in nodes)
        {
            if (node.Children.Count > 0)
            {
                SortTree(node.Children);
            }
        }
    }

    private string GetCommandLine(int pid)
    {
        if (pid <= 4)
        {
            return string.Empty;
        }

        IntPtr hProcess = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation, false, pid);

        if (hProcess == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            int bufferSize = 0;
            // Get size first (usually returns STATUS_INFO_LENGTH_MISMATCH)
            NativeMethods.NtQueryInformationProcess(hProcess,
                NativeMethods.ProcessCommandLineInformation, IntPtr.Zero, 0, out bufferSize);

            if (bufferSize == 0)
            {
                return string.Empty;
            }

            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                int status = NativeMethods.NtQueryInformationProcess(hProcess,
                    NativeMethods.ProcessCommandLineInformation, buffer, bufferSize, out _);

                if (status == NativeMethods.StatusSuccess)
                {
                    // Read UNICODE_STRING
                    var unicodeString = Marshal.PtrToStructure<NativeMethods.UnicodeString>(buffer);
                    if (unicodeString.Buffer != IntPtr.Zero && unicodeString.Length > 0)
                    {
                        return Marshal.PtrToStringUni(unicodeString.Buffer) ?? string.Empty;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            // Ignore exceptions
            Log.Warning(ex, "Ignored");
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }

        return string.Empty;
    }

    private string? GetProcessPath(int pid)
    {
        // Fallback to .NET API for path retrieval as it's complex via P/Invoke
        // Only called once per process creation.
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.MainModule?.FileName;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Ignored");
            return null;
        }
    }

    public void Dispose()
    {
        if (buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
        }

        if (pdhQuery != IntPtr.Zero)
        {
            NativeMethods.PdhCloseQuery(pdhQuery);
            pdhQuery = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    ~ProcessService()
    {
        Dispose();
    }
}