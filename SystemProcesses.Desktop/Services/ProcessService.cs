using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Services;

public class ProcessService : IProcessService
{
    private readonly Dictionary<int, ProcessInfo> _activeProcesses = new(1024);
    private readonly Dictionary<int, long> _prevCpuTimes = new(1024);
    private readonly List<ProcessInfo> _rootNodes = new(64);
    private readonly HashSet<int> _servicePids = new();

    // Reusable buffer for NtQuerySystemInformation
    private IntPtr _buffer = IntPtr.Zero;
    private int _bufferSize = 1024 * 1024;
    private long _prevTicks = 0;

    // Reusable comparison delegate to avoid allocation
    private static readonly Comparison<ProcessInfo> _nameComparer =
        (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    public ProcessService()
    {
        _buffer = Marshal.AllocHGlobal(_bufferSize);
    }

    ~ProcessService()
    {
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
        }
    }

    public async Task<List<ProcessInfo>> GetProcessTreeAsync()
    {
        return await Task.Run(() =>
        {
            lock (_activeProcesses)
            {
                RefreshServicePids();
                UpdateProcessSnapshot();
                RebuildTreeStructure();
                // Return a shallow copy of the roots to prevent iteration issues if the service updates immediately
                return new List<ProcessInfo>(_rootNodes);
            }
        });
    }

    private void RefreshServicePids()
    {
        _servicePids.Clear();
        IntPtr scmHandle = NativeMethods.OpenSCManager(null, null,
            NativeMethods.SC_MANAGER_CONNECT | NativeMethods.SC_MANAGER_ENUMERATE_SERVICE);

        if (scmHandle == IntPtr.Zero) return;

        IntPtr buf = IntPtr.Zero;
        try
        {
            int bytesNeeded = 0;
            int servicesReturned = 0;
            int resumeHandle = 0;

            // First call to get size
            NativeMethods.EnumServicesStatusEx(scmHandle, NativeMethods.SC_ENUM_PROCESS_INFO,
                NativeMethods.SERVICE_WIN32, NativeMethods.SERVICE_STATE_ALL,
                IntPtr.Zero, 0, out bytesNeeded, out servicesReturned, ref resumeHandle, null);

            if (bytesNeeded > 0)
            {
                buf = Marshal.AllocHGlobal(bytesNeeded);
                if (NativeMethods.EnumServicesStatusEx(scmHandle, NativeMethods.SC_ENUM_PROCESS_INFO,
                    NativeMethods.SERVICE_WIN32, NativeMethods.SERVICE_STATE_ALL,
                    buf, bytesNeeded, out bytesNeeded, out servicesReturned, ref resumeHandle, null))
                {
                    IntPtr ptr = buf;
                    for (int i = 0; i < servicesReturned; i++)
                    {
                        var service = Marshal.PtrToStructure<NativeMethods.ENUM_SERVICE_STATUS_PROCESS>(ptr);
                        if (service.ServiceStatusProcess.dwProcessId > 0)
                        {
                            _servicePids.Add(service.ServiceStatusProcess.dwProcessId);
                        }
                        ptr = IntPtr.Add(ptr, Marshal.SizeOf<NativeMethods.ENUM_SERVICE_STATUS_PROCESS>());
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

    private unsafe void UpdateProcessSnapshot()
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
            _bufferSize = requiredSize + (1024 * 1024);
            _buffer = Marshal.AllocHGlobal(_bufferSize);
            status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemProcessInformation,
                _buffer,
                _bufferSize,
                out requiredSize);
        }

        if (status != NativeMethods.STATUS_SUCCESS) return;

        long currentTicks = DateTime.UtcNow.Ticks;
        double deltaTime = (currentTicks - _prevTicks);
        _prevTicks = currentTicks;

        var currentPids = new HashSet<int>();
        long offset = 0;
        NativeMethods.SYSTEM_PROCESS_INFORMATION* ptr;

        do
        {
            ptr = (NativeMethods.SYSTEM_PROCESS_INFORMATION*)(_buffer + (int)offset);
            int pid = ptr->UniqueProcessId.ToInt32();
            currentPids.Add(pid);

            long totalCpuTime = ptr->KernelTime + ptr->UserTime;
            long memBytes = (long)ptr->WorkingSetSize;
            // FIX: Use PrivatePageCount (Commit Size) instead of VirtualSize (Address Space)
            // VirtualSize includes unallocated reserved space (TB range on x64).
            long virtualBytes = (long)ptr->PrivatePageCount;
            int parentPid = ptr->InheritedFromUniqueProcessId.ToInt32();

            // Calculate CPU Usage
            double cpuUsage = 0;
            if (_prevCpuTimes.TryGetValue(pid, out long prevTime) && deltaTime > 0)
            {
                long deltaCpu = totalCpuTime - prevTime;
                cpuUsage = (deltaCpu / (double)deltaTime) * 100.0;
                // Normalize by processor count if desired, but Task Manager usually shows sum > 100% for multi-core
                cpuUsage /= Environment.ProcessorCount;
            }
            _prevCpuTimes[pid] = totalCpuTime;

            // Check if service
            bool isService = _servicePids.Contains(pid);

            if (_activeProcesses.TryGetValue(pid, out var info))
            {
                // UPDATE existing (Zero Alloc)
                info.Update(cpuUsage, memBytes, virtualBytes);
                info.ParentPid = parentPid;
                info.IsService = isService; // Update service status (rarely changes, but possible)
            }
            else
            {
                string name;
                if (pid == 0) name = "System Idle Process";
                else if (pid == 4) name = "System";
                else name = Marshal.PtrToStringUni(ptr->ImageName.Buffer) ?? "Unknown";

                var newInfo = new ProcessInfo
                {
                    Pid = pid,
                    Name = name,
                    ParentPid = parentPid,
                    IsService = isService,
                    Icon = IconCache.GetIcon(GetProcessPath(pid)),
                    Parameters = GetCommandLine(pid) // Fetch once
                };
                newInfo.Update(cpuUsage, memBytes, virtualBytes);
                _activeProcesses.Add(pid, newInfo);
            }

            if (ptr->NextEntryOffset == 0) break;
            offset += ptr->NextEntryOffset;

        } while (true);

        // REMOVE stopped processes
        // To avoid allocating a list for removal, we can iterate a copy of keys or use a pooled list.
        // For simplicity/safety here, we use a standard list, but in extreme optimization, we'd pool this.
        var deadPids = _activeProcesses.Keys.Where(k => !currentPids.Contains(k)).ToList();
        foreach (var pid in deadPids)
        {
            _activeProcesses.Remove(pid);
            _prevCpuTimes.Remove(pid);
        }
    }

    private void RebuildTreeStructure()
    {
        _rootNodes.Clear();
        // Clear children lists (low cost, just resets count)
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
            // Ignore errors
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
}