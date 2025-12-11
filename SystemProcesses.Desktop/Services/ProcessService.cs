using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Services;

public class ProcessService : IProcessService, IDisposable
{
    private struct ProcessHistory
    {
        public long TotalProcessorTime;
        public long TotalIoBytes;
    }

    private readonly Dictionary<int, ProcessInfo> _activeProcesses = new(1024);
    private readonly Dictionary<int, ProcessHistory> _prevProcessStats = new(1024);
    private readonly List<ProcessInfo> _rootNodes = new(64);
    private readonly HashSet<int> _servicePids = new();

    // Reusable buffers
    private readonly HashSet<int> _currentPidsBuffer = new(1024);
    private readonly List<int> _deadPidsBuffer = new(64);

    // Reusable buffer for NtQuerySystemInformation
    private IntPtr _buffer = IntPtr.Zero;
    private int _bufferSize = 1024 * 1024;
    private long _prevTicks = 0;
    // PDH Fields
    private IntPtr _pdhQuery = IntPtr.Zero;
    private IntPtr _pdhDiskIdleCounter = IntPtr.Zero;
    private bool _isPdhInitialized = false;

    // Reusable comparison delegate to avoid allocation
    private static readonly Comparison<ProcessInfo> _nameComparer =
        (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    public ProcessService()
    {
        _buffer = Marshal.AllocHGlobal(_bufferSize);
        InitializePdh();
    }

    private void InitializePdh()
    {
        Debug.WriteLine("--- PDH DIAGNOSIS START ---");

        try
        {
            // 1. Open Query
            int status = NativeMethods.PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _pdhQuery);
            Debug.WriteLine($"PdhOpenQuery Result: 0x{status:X8} (0 is Success)");

            if (status != 0) return;

            // 2. Try PhysicalDisk
            string physPath = "\\PhysicalDisk(_Total)\\% Idle Time";
            status = NativeMethods.PdhAddEnglishCounter(_pdhQuery, physPath, IntPtr.Zero, out _pdhDiskIdleCounter);
            Debug.WriteLine($"AddCounter '{physPath}' Result: 0x{status:X8}");

            // 3. Fallback to LogicalDisk
            if (status != 0)
            {
                string logPath = "\\LogicalDisk(_Total)\\% Idle Time";
                status = NativeMethods.PdhAddEnglishCounter(_pdhQuery, logPath, IntPtr.Zero, out _pdhDiskIdleCounter);
                Debug.WriteLine($"AddCounter '{logPath}' Result: 0x{status:X8}");
            }

            // 4. Initial Collect
            if (status == 0)
            {
                status = NativeMethods.PdhCollectQueryData(_pdhQuery);
                Debug.WriteLine($"Initial Collect Result: 0x{status:X8}");

                if (status == 0)
                {
                    _isPdhInitialized = true;
                    Debug.WriteLine("PDH Initialized SUCCESSFULLY.");
                }
            }
            else
            {
                Debug.WriteLine("PDH Initialization FAILED: Could not add any counters.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PDH CRITICAL EXCEPTION: {ex}");
        }
        finally
        {
            Debug.WriteLine("--- PDH DIAGNOSIS END ---");
        }
    }

    public async Task<(List<ProcessInfo> Roots, SystemStats Stats)> GetProcessTreeAsync()
    {
        // We return the raw list to avoid allocation. 
        // The consumer MUST NOT iterate this list concurrently with the next call to GetProcessTreeAsync.
        // Given the UI "pull" model, this is safe.
        return await Task.Run(() =>
        {
            lock (_activeProcesses)
            {
                RefreshServicePids();
                var stats = UpdateProcessSnapshot();
                RebuildTreeStructure();
                return (_rootNodes, stats);
            }
        });
    }

    private unsafe void RefreshServicePids()
    {
        _servicePids.Clear();
        IntPtr scmHandle = NativeMethods.OpenSCManagerW(null, null,
            NativeMethods.SC_MANAGER_CONNECT | NativeMethods.SC_MANAGER_ENUMERATE_SERVICE);

        if (scmHandle == IntPtr.Zero) return;

        IntPtr buf = IntPtr.Zero;
        try
        {
            int bytesNeeded = 0;
            int servicesReturned = 0;
            int resumeHandle = 0;

            // First call to get size
            NativeMethods.EnumServicesStatusExW(scmHandle, NativeMethods.SC_ENUM_PROCESS_INFO,
                NativeMethods.SERVICE_WIN32, NativeMethods.SERVICE_STATE_ALL,
                IntPtr.Zero, 0, out bytesNeeded, out servicesReturned, ref resumeHandle, null);

            if (bytesNeeded > 0)
            {
                buf = Marshal.AllocHGlobal(bytesNeeded);
                if (NativeMethods.EnumServicesStatusExW(scmHandle, NativeMethods.SC_ENUM_PROCESS_INFO,
                    NativeMethods.SERVICE_WIN32, NativeMethods.SERVICE_STATE_ALL,
                    buf, bytesNeeded, out bytesNeeded, out servicesReturned, ref resumeHandle, null))
                {
                    byte* ptr = (byte*)buf;
                    int structSize = Marshal.SizeOf<NativeMethods.ENUM_SERVICE_STATUS_PROCESS>();

                    for (int i = 0; i < servicesReturned; i++)
                    {
                        // Direct pointer access to avoid full struct marshalling
                        var serviceStruct = (NativeMethods.ENUM_SERVICE_STATUS_PROCESS*)ptr;
                        int pid = serviceStruct->ServiceStatusProcess.dwProcessId;

                        if (pid > 0)
                        {
                            _servicePids.Add(pid);
                        }
                        ptr += structSize;
                    }
                }
            }
        }
        finally
        {
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            NativeMethods.CloseServiceHandle(scmHandle);
        }
    }

    private unsafe SystemStats UpdateProcessSnapshot()
    {
        int requiredSize = 0;
        int status = NativeMethods.NtQuerySystemInformation(
            NativeMethods.SystemProcessInformation,
            _buffer,
            _bufferSize,
            out requiredSize);

        if (status == NativeMethods.STATUS_INFO_LENGTH_MISMATCH)
        {
            Marshal.FreeHGlobal(_buffer);
            _bufferSize = requiredSize + (1024 * 1024); // Add 1MB padding
            _buffer = Marshal.AllocHGlobal(_bufferSize);
            status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemProcessInformation,
                _buffer,
                _bufferSize,
                out requiredSize);
        }

        // Initialize Stats
        var stats = new SystemStats();

        if (status != NativeMethods.STATUS_SUCCESS) return stats;

        if (_isPdhInitialized)
        {
            int collectStatus = NativeMethods.PdhCollectQueryData(_pdhQuery);

            NativeMethods.PDH_FMT_COUNTERVALUE value;
            int readStatus = NativeMethods.PdhGetFormattedCounterValue(
                _pdhDiskIdleCounter,
                NativeMethods.PDH_FMT_DOUBLE,
                IntPtr.Zero,
                out value);

            if (collectStatus == 0 && readStatus == 0 && value.CStatus == 0) // CStatus 0 is Valid
            {
                // Clamp to 0-100 range
                double idle = value.doubleValue;

                // Debug.WriteLine($"Raw Idle: {idle:F2}%"); // Uncomment to see raw values

                if (idle > 100) idle = 100;
                if (idle < 0) idle = 0;

                stats.DiskActivePercent = 100.0 - idle;
            }
            else
            {
                // Log failures only (to avoid spamming success)
                Debug.WriteLine($"PDH Read Fail -> Collect: 0x{collectStatus:X8}, Read: 0x{readStatus:X8}, CStatus: 0x{value.CStatus:X8}");
            }
        }

        var memStatus = NativeMethods.MEMORYSTATUSEX.Default;
        if (NativeMethods.GlobalMemoryStatusEx(ref memStatus))
        {
            stats.TotalPhysicalMemory = (long)memStatus.ullTotalPhys;
            stats.AvailablePhysicalMemory = (long)memStatus.ullAvailPhys;
            stats.TotalCommitLimit = (long)memStatus.ullTotalPageFile;
            stats.AvailableCommitLimit = (long)memStatus.ullAvailPageFile;
        }

        long currentTicks = DateTime.UtcNow.Ticks;
        double deltaTime = (currentTicks - _prevTicks);
        double deltaTimeSec = deltaTime / 10_000_000.0; // Ticks are 100ns
        _prevTicks = currentTicks;

        _currentPidsBuffer.Clear();
        long offset = 0;
        NativeMethods.SYSTEM_PROCESS_INFORMATION* ptr;

        long globalIoDelta = 0;

        do
        {
            ptr = (NativeMethods.SYSTEM_PROCESS_INFORMATION*)((byte*)_buffer + offset);
            int pid = ptr->UniqueProcessId.ToInt32();
            _currentPidsBuffer.Add(pid);

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

            if (_prevProcessStats.TryGetValue(pid, out var history) && deltaTime > 0)
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
            _prevProcessStats[pid] = new ProcessHistory
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
            bool isService = _servicePids.Contains(pid);

            if (_activeProcesses.TryGetValue(pid, out var info))
            {
                // UPDATE existing (Zero Alloc)
                info.Update(cpuUsage, memBytes, virtualBytes, threads, handles);
                info.ParentPid = parentPid;
                info.IsService = isService; // Update service status (rarely changes, but possible)
            }
            else
            {
                string name;
                if (pid == 0) name = "System Idle Process";
                else if (pid == 4) name = "System";
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
                    Icon = IconCache.GetIcon(GetProcessPath(pid)),
                    Parameters = GetCommandLine(pid) // Fetch once
                };
                newInfo.Update(cpuUsage, memBytes, virtualBytes, threads, handles);
                _activeProcesses.Add(pid, newInfo);
            }

            if (ptr->NextEntryOffset == 0) break;
            offset += ptr->NextEntryOffset;

        } while (true);

        // Calculate IO Rate
        if (deltaTimeSec > 0)
        {
            stats.TotalIoBytesPerSec = (long)(globalIoDelta / deltaTimeSec);
        }

        // Remove stopped processes using pooled buffer
        _deadPidsBuffer.Clear();
        foreach (var pid in _activeProcesses.Keys)
        {
            if (!_currentPidsBuffer.Contains(pid))
            {
                _deadPidsBuffer.Add(pid);
            }
        }

        foreach (var pid in _deadPidsBuffer)
        {
            _activeProcesses.Remove(pid);
            _prevProcessStats.Remove(pid); // Remove history
        }

        return stats;
    }

    private void RebuildTreeStructure()
    {
        _rootNodes.Clear();

        // Reset children without reallocating lists if possible, 
        // but ProcessInfo.Children is a List<T>, so Clear() is O(N) but keeps capacity.
        foreach (var p in _activeProcesses.Values)
        {
            p.Children.Clear();
        }

        foreach (var p in _activeProcesses.Values)
        {
            if (p.ParentPid != 0 && _activeProcesses.TryGetValue(p.ParentPid, out var parent))
            {
                parent.Children.Add(p);
            }
            else
            {
                _rootNodes.Add(p);
            }
        }

        // Sort the tree
        SortTree(_rootNodes);
    }

    private void SortTree(List<ProcessInfo> nodes)
    {
        nodes.Sort(_nameComparer);
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
        if (pid <= 4) return string.Empty;

        IntPtr hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);

        if (hProcess == IntPtr.Zero) return string.Empty;

        try
        {
            int bufferSize = 0;
            // Get size first (usually returns STATUS_INFO_LENGTH_MISMATCH)
            NativeMethods.NtQueryInformationProcess(hProcess,
                NativeMethods.ProcessCommandLineInformation, IntPtr.Zero, 0, out bufferSize);

            if (bufferSize == 0) return string.Empty;

            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                int status = NativeMethods.NtQueryInformationProcess(hProcess,
                    NativeMethods.ProcessCommandLineInformation, buffer, bufferSize, out _);

                if (status == NativeMethods.STATUS_SUCCESS)
                {
                    // Read UNICODE_STRING
                    var unicodeString = Marshal.PtrToStructure<NativeMethods.UNICODE_STRING>(buffer);
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
        catch
        {
            // Ignore exceptions
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
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }

        if (_pdhQuery != IntPtr.Zero)
        {
            NativeMethods.PdhCloseQuery(_pdhQuery);
            _pdhQuery = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    ~ProcessService()
    {
        Dispose();
    }
}