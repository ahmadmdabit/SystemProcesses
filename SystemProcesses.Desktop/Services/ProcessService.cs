using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using Serilog;

using SystemProcesses.Desktop.Helpers;
using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Services;

public class ProcessService : IProcessService, IDisposable
{
    private struct ProcessHistory
    {
        public long TotalProcessorTime;
        public long TotalIoBytes;
    }

    private readonly Dictionary<int, ProcessInfo> activeProcesses = new(AppConstants.InitialActiveProcessesCapacity);
    private readonly Dictionary<int, ProcessHistory> prevProcessStats = new(AppConstants.InitialPrevStatsCapacity);
    private readonly List<ProcessInfo> rootNodes = new(AppConstants.InitialRootNodesCapacity);
    private readonly HashSet<int> servicePids = [];

    // Reusable buffers
    private readonly HashSet<int> currentPidsBuffer = new(AppConstants.InitialPidsBufferCapacity);

    private readonly List<int> stoppedPidsBuffer = new(AppConstants.InitialStoppedPidsCapacity);
    private readonly ProcessInfo?[] top5Buffer = new ProcessInfo?[AppConstants.TopProcessesCount];
    private readonly DriveStats[] driveBuffer = new DriveStats[AppConstants.MaxDriveLetters];

    // Reusable buffer for NtQuerySystemInformation
    private IntPtr buffer = IntPtr.Zero;
    private int bufferSize = AppConstants.InitialBufferSize;
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
        buffer = Marshal.AllocHGlobal(AppConstants.InitialBufferSize);
        bufferSize = AppConstants.InitialBufferSize;
        InitializePdh();
    }

    private void InitializePdh()
    {
        try
        {
            // 1. Open Query
            int status = SystemPrimitives.PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out pdhQuery);

            if (status != 0)
            {
                Log.Warning("PdhOpenQuery failed with status 0x{Status:X8}. Disk I/O monitoring disabled.", status);
                return;
            }

            // 2. Try PhysicalDisk
            const string physPath = "\\PhysicalDisk(_Total)\\% Idle Time";
            status = SystemPrimitives.PdhAddEnglishCounter(pdhQuery, physPath, IntPtr.Zero, out pdhDiskIdleCounter);

            // 3. Fallback to LogicalDisk
            if (status != 0)
            {
                Log.Warning("PdhAddEnglishCounter (PhysicalDisk) failed with status 0x{Status:X8}. Trying LogicalDisk.", status);
                const string logPath = "\\LogicalDisk(_Total)\\% Idle Time";
                status = SystemPrimitives.PdhAddEnglishCounter(pdhQuery, logPath, IntPtr.Zero, out pdhDiskIdleCounter);
            }

            // 4. Initial Collect
            if (status == 0)
            {
                status = SystemPrimitives.PdhCollectQueryData(pdhQuery);

                if (status == 0)
                {
                    isPdhInitialized = true;
                    Log.Information("PDH initialized successfully for disk I/O monitoring");
                }
                else
                {
                    Log.Warning("PdhCollectQueryData failed with status 0x{Status:X8}. Disk I/O monitoring disabled.", status);
                }
            }
            else
            {
                Log.Warning("PdhAddEnglishCounter (LogicalDisk) failed with status 0x{Status:X8}. Disk I/O monitoring disabled.", status);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PDH initialization exception: {Message}. Disk I/O monitoring disabled.", ex.Message);
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
        IntPtr scmHandle = SystemPrimitives.OpenSCManagerW(null, null,
            SystemPrimitives.ScManagerConnect | SystemPrimitives.ScManagerEnumerateService);

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
            SystemPrimitives.EnumServicesStatusExW(scmHandle, SystemPrimitives.ScEnumProcessInfo,
                SystemPrimitives.ServiceWIN32, SystemPrimitives.ServiceStateAll,
                IntPtr.Zero, 0, out bytesNeeded, out servicesReturned, ref resumeHandle, null);

            if (bytesNeeded > 0)
            {
                buf = Marshal.AllocHGlobal(bytesNeeded);
                if (SystemPrimitives.EnumServicesStatusExW(scmHandle, SystemPrimitives.ScEnumProcessInfo,
                    SystemPrimitives.ServiceWIN32, SystemPrimitives.ServiceStateAll,
                    buf, bytesNeeded, out bytesNeeded, out servicesReturned, ref resumeHandle, null))
                {
                    byte* ptr = (byte*)buf;
                    int structSize = Marshal.SizeOf<SystemPrimitives.EnumServiceStatusProcess>();

                    for (int i = 0; i < servicesReturned; i++)
                    {
                        // Direct pointer access to avoid full struct marshalling
                        var serviceStruct = (SystemPrimitives.EnumServiceStatusProcess*)ptr;
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

            SystemPrimitives.CloseServiceHandle(scmHandle);
        }
    }

    private unsafe SystemStats UpdateProcessSnapshot()
    {
        // CRITICAL: Validate buffer before use
        if (buffer == IntPtr.Zero || bufferSize <= 0)
        {
            Log.Error("Buffer not initialized: buffer={Buffer}, size={Size}", buffer, bufferSize);
            return new SystemStats();
        }

        int requiredSize = 0;
        int status = SystemPrimitives.NtQuerySystemInformation(
            SystemPrimitives.SystemProcessInformationValue,
            buffer,
            bufferSize,
            out requiredSize);

        if (status == SystemPrimitives.StatusInfoLengthMismatch)
        {
            // CRITICAL: Validate required size before allocation
            if (requiredSize <= 0 || requiredSize > AppConstants.MaxBufferSize) // Max 100MB
            {
                Log.Error("Invalid buffer size requested: {Size}", requiredSize);
                return new SystemStats();
            }

            Marshal.FreeHGlobal(buffer);
            bufferSize = requiredSize + AppConstants.BufferPaddingSize; // Add 1MB padding
            buffer = Marshal.AllocHGlobal(bufferSize);

            if (buffer == IntPtr.Zero)
            {
                Log.Error("Failed to allocate buffer of size {Size}", bufferSize);
                bufferSize = 0;
                return new SystemStats();
            }

            status = SystemPrimitives.NtQuerySystemInformation(
                SystemPrimitives.SystemProcessInformationValue,
                buffer,
                bufferSize,
                out requiredSize);
        }

        // Initialize Stats
        var stats = new SystemStats();

        if (status != SystemPrimitives.StatusSuccess)
        {
            Log.Warning("NtQuerySystemInformation failed with status 0x{Status:X8}", status);
            return stats;
        }

        if (isPdhInitialized)
        {
            int collectStatus = SystemPrimitives.PdhCollectQueryData(pdhQuery);

            SystemPrimitives.PdhFmtCountervalue value;
            int readStatus = SystemPrimitives.PdhGetFormattedCounterValue(
                pdhDiskIdleCounter,
                SystemPrimitives.PdhFmtDouble,
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

        var memStatus = SystemPrimitives.MemoryStatusEx.Default;
        if (SystemPrimitives.GlobalMemoryStatusEx(ref memStatus))
        {
            stats.TotalPhysicalMemory = (long)memStatus.ullTotalPhys;
            stats.AvailablePhysicalMemory = (long)memStatus.ullAvailPhys;
            stats.TotalCommitLimit = (long)memStatus.ullTotalPageFile;
            stats.AvailableCommitLimit = (long)memStatus.ullAvailPageFile;
        }

        // Storage Stats (Zero-Alloc, P/Invoke)
        int driveCount = 0;
        uint drivesBitMask = SystemPrimitives.GetLogicalDrives();

        // Stack allocate path buffer: "X:\\\0" (4 chars)
        char* rootPath = stackalloc char[4];
        rootPath[1] = ':';
        rootPath[2] = '\\';
        rootPath[3] = '\0';

        // Iterate bits 0-25 (A-Z)
        for (int i = 0; i < AppConstants.MaxDriveLetters; i++)
        {
            if ((drivesBitMask & (1 << i)) != 0)
            {
                rootPath[0] = (char)('A' + i);

                // Filter for Fixed drives only (HDD/SSD) to avoid latency/timeouts
                if (SystemPrimitives.GetDriveTypeW(rootPath) == SystemPrimitives.DriveFixed)
                {
                    ulong freeBytes, totalBytes, totalFree;
                    if (SystemPrimitives.GetDiskFreeSpaceExW(rootPath, out freeBytes, out totalBytes, out totalFree))
                    {
                        ref var d = ref driveBuffer[driveCount++];
                        d.Letter = rootPath[0];
                        d.TotalSize = (long)totalBytes;
                        d.AvailableFreeSpace = (long)freeBytes;
                    }
                }
            }
        }
        stats.DriveCount = driveCount;
        stats.Drives = driveBuffer;

        // Clear trailing slots so stale drive data is never referenced when driveCount shrinks
        // (e.g., a drive is unmounted). This prevents old DriveStats entries from persisting
        // in the buffer for the next cycle.
        if (driveCount < AppConstants.MaxDriveLetters)
        {
            Array.Clear(driveBuffer, driveCount, AppConstants.MaxDriveLetters - driveCount);
        }

        long currentTicks = DateTime.UtcNow.Ticks;
        double deltaTime = (currentTicks - prevTicks);
        double deltaTimeSec = deltaTime / (double)AppConstants.TicksPerSecond; // Ticks are 100ns
        prevTicks = currentTicks;

        currentPidsBuffer.Clear();
        long offset = 0;
        SystemPrimitives.SystemProcessInformation* ptr;

        long globalIoDelta = 0;

        do
        {
            // CRITICAL: Validate offset before pointer arithmetic
            if (offset < 0 || offset >= bufferSize)
            {
                Log.Error("Offset out of bounds: offset={Offset}, bufferSize={Size}", offset, bufferSize);
                break;
            }

            ptr = (SystemPrimitives.SystemProcessInformation*)((byte*)buffer + offset);

            // CRITICAL: Validate pointer is within buffer
            if ((byte*)ptr < (byte*)buffer || (byte*)ptr >= (byte*)buffer + bufferSize)
            {
                Log.Error("Pointer out of bounds during iteration");
                break;
            }

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
            if (pid != AppConstants.SystemIdleProcessPid)
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

            if (activeProcesses.TryGetValue(pid, out var info) && info.CreateTime == ptr->CreateTime)
            {
                // UPDATE existing (Zero Alloc) — CreateTime match confirms this is the same process instance,
                // not a recycled PID being reused by a different process.
                info.Update(cpuUsage, memBytes, virtualBytes, threads, handles);
                info.ParentPid = parentPid;
                info.IsService = isService; // Update service status (rarely changes, but possible)
            }
            else
            {
                string name;
                if (pid == AppConstants.SystemIdleProcessPid)
                {
                    name = "System Idle Process";
                }
                else if (pid == AppConstants.SystemProcessPid)
                {
                    name = "System";
                }
                else
                {
                    // CRITICAL: Validate string encoding
                    // UnicodeString.Length is reported in BYTES by the OS kernel.
                    // Marshal.PtrToStringUni expects a length in CHARACTERS.
                    // Since UTF-16 uses 2 bytes per character, we divide by 2 to get the correct count.
                    // VALIDATION: Ensure Length is even (valid UTF-16)
                    if (ptr->ImageName.Length % AppConstants.Utf16BytesPerChar != 0)
                    {
                        Log.Warning("Invalid UTF-16 string length for PID {Pid}: {Length}",
                            pid, ptr->ImageName.Length);
                        name = "Unknown";
                    }
                    else if (ptr->ImageName.Buffer == IntPtr.Zero)
                    {
                        Log.Warning("Null ImageName buffer for PID {Pid}", pid);
                        name = "Unknown";
                    }
                    else
                    {
                        try
                        {
                            name = Marshal.PtrToStringUni(ptr->ImageName.Buffer, ptr->ImageName.Length / 2) ?? "Unknown";
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to marshal process name for PID {Pid}", pid);
                            name = "Unknown";
                        }
                    }
                }

                var commandLineResult = GetCommandLine(pid);
                string commandLine = commandLineResult.GetValueOrDefault(string.Empty);

                var processPathResult = GetProcessPath(pid);
                string? processPath = processPathResult.GetValueOrDefault(null!);

                var newInfo = new ProcessInfo
                {
                    Pid = pid,
                    CreateTime = ptr->CreateTime,
                    Name = name,
                    ParentPid = parentPid,
                    IsService = isService,
                    ProcessPath = processPath,
                    Parameters = commandLine // Fetch once
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
            // Guard against self-parenting (ParentPid == Pid) and cyclic references.
            // If p's parent is itself or is an ancestor of p, treat p as a root node.
            if (p.ParentPid != 0 && p.ParentPid != p.Pid && activeProcesses.TryGetValue(p.ParentPid, out var parent))
            {
                if (!IsAncestor(p, parent))
                    parent.Children.Add(p);
                else
                    rootNodes.Add(p);
            }
            else
            {
                rootNodes.Add(p);
            }
        }

        // Sort the tree
        SortTree(rootNodes);
    }

    /// <summary>
    /// Detects if <paramref name="potentialAncestor"/> is an ancestor of <paramref name="candidate"/>
    /// by walking the parent chain. This prevents cyclic process hierarchies (A->B->A) from causing
    /// infinite recursion during tree sort/sync.
    /// </summary>
    private bool IsAncestor(ProcessInfo candidate, ProcessInfo potentialAncestor)
    {
        var current = potentialAncestor;
        while (current != null)
        {
            if (current.Pid == candidate.Pid) return true;
            if (current.ParentPid == 0 || current.ParentPid == current.Pid) break;
            if (!activeProcesses.TryGetValue(current.ParentPid, out current)) break;
        }
        return false;
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

    /// <summary>
    /// Retrieves the command line for a process.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    /// <returns>A Result containing the command line string on success, or a Failure with error details.</returns>
    private Result<string> GetCommandLine(int pid)
    {
        if (pid <= 4)
        {
            return new Result<string>.Failure(
                new InvalidOperationException("Cannot query command line for system processes"),
                $"PID {pid} is a system process (PID <= 4)");
        }

        IntPtr hProcess = SystemPrimitives.OpenProcess(
            SystemPrimitives.ProcessQueryLimitedInformation, false, pid);

        if (hProcess == IntPtr.Zero)
        {
            return new Result<string>.Failure(
                new UnauthorizedAccessException("OpenProcess failed"),
                $"Failed to open process handle for PID {pid} (access denied or process exited)");
        }

        try
        {
            int bufferSize = 0;
            // Get size first (usually returns StatusInfoLengthMismatch)
            SystemPrimitives.NtQueryInformationProcess(hProcess,
                SystemPrimitives.ProcessCommandLineInformation, IntPtr.Zero, 0, out bufferSize);

            if (bufferSize == 0)
            {
                return new Result<string>.Failure(
                    new InvalidOperationException("Buffer size query returned 0"),
                    $"NtQueryInformationProcess returned 0 buffer size for PID {pid}");
            }

            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                int status = SystemPrimitives.NtQueryInformationProcess(hProcess,
                    SystemPrimitives.ProcessCommandLineInformation, buffer, bufferSize, out _);

                if (status != SystemPrimitives.StatusSuccess)
                {
                    return new Result<string>.Failure(
                        new InvalidOperationException($"NtQueryInformationProcess failed with status 0x{status:X8}"),
                        $"Failed to query command line for PID {pid}: status 0x{status:X8}");
                }

                // Read UnicodeString
                var unicodeString = Marshal.PtrToStructure<SystemPrimitives.UnicodeString>(buffer);
                if (unicodeString.Buffer == IntPtr.Zero)
                {
                    return new Result<string>.Failure(
                        new InvalidOperationException("UnicodeString buffer is null"),
                        $"Command line buffer is null for PID {pid}");
                }

                if (unicodeString.Length == 0)
                {
                    return new Result<string>.Success(string.Empty);
                }

                // DX: The kernel buffer is not guaranteed to be null-exitd.
                // We must explicitly tell .NET how many characters to read.
                // Calculation: [OS Byte Count] / 2 = [.NET Char Count]
                string? commandLine = Marshal.PtrToStringUni(unicodeString.Buffer, unicodeString.Length / 2);
                return new Result<string>.Success(commandLine ?? string.Empty);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Exception while querying command line for PID {Pid}", pid);
            return new Result<string>.Failure(ex, $"Exception occurred while querying command line for PID {pid}");
        }
        finally
        {
            SystemPrimitives.CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Retrieves the file path of a process executable.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    /// <returns>A Result containing the process path on success, or a Failure with error details.</returns>
    private Result<string> GetProcessPath(int pid)
    {
        // Fallback to .NET API for path retrieval as it's complex via P/Invoke
        // Only called once per process creation.
        try
        {
            using var p = Process.GetProcessById(pid);
            string? path = p.MainModule?.FileName;

            if (string.IsNullOrEmpty(path))
            {
                return new Result<string>.Failure(
                    new InvalidOperationException("MainModule.FileName is null or empty"),
                    $"Process.GetProcessById({pid}).MainModule?.FileName returned null or empty");
            }

            return new Result<string>.Success(path);
        }
        catch (ArgumentException ex)
        {
            return new Result<string>.Failure(ex, $"Process with PID {pid} not found (may have exited)");
        }
        catch (InvalidOperationException ex)
        {
            return new Result<string>.Failure(ex, $"Cannot access process path for PID {pid} (access denied or system process)");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Exception while querying process path for PID {Pid}", pid);
            return new Result<string>.Failure(ex, $"Exception occurred while querying process path for PID {pid}");
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
            SystemPrimitives.PdhCloseQuery(pdhQuery);
            pdhQuery = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    ~ProcessService()
    {
        Dispose();
    }
}