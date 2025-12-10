using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using SystemProcesses.Desktop.Models;

namespace SystemProcesses.Desktop.Services
{
    public class ProcessService : IProcessService
    {
        private HashSet<string> _serviceProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        public ProcessService()
        {
            LoadServiceProcessNames();
        }

        private void LoadServiceProcessNames()
        {
            try
            {
                var services = ServiceController.GetServices();
                lock (_lock)
                {
                    _serviceProcessNames = new HashSet<string>(
                        services.Select(s => s.ServiceName),
                        StringComparer.OrdinalIgnoreCase
                    );
                }
            }
            catch
            {
                // If we can't load services, continue without service detection
            }
        }

        public async Task<List<ProcessInfo>> GetProcessTreeAsync()
        {
            return await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();

                var processInfos = new Dictionary<int, ProcessInfo>();

                // Batch load all WMI data upfront (MUCH faster than per-process queries)
                var wmiData = GetAllProcessWmiData();

                // Get all processes
                var processes = Process.GetProcesses();

                foreach (var process in processes)
                {
                    try
                    {
                        var pid = process.Id;

                        var info = new ProcessInfo
                        {
                            Pid = pid,
                            Name = process.ProcessName,
                            MemoryBytes = process.WorkingSet64,
                            VirtualMemoryBytes = process.VirtualMemorySize64,
                            ParentPid = wmiData.TryGetValue(pid, out var wmi) ? wmi.ParentPid : 0,
                            Parameters = wmiData.TryGetValue(pid, out var wmi2) ? wmi2.CommandLine : string.Empty,
                            IsService = IsServiceProcess(process.ProcessName)
                        };

                        // Get CPU percentage
                        info.CpuPercentage = GetCpuPercentage(process);

                        // Get icon (must freeze for cross-thread usage)
                        info.Icon = GetProcessIcon(process);

                        processInfos[pid] = info;
                    }
                    catch
                    {
                        // Skip processes we can't access
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                Debug.WriteLine($"ProcessService: Loaded {processInfos.Count} processes in {sw.Elapsed}.");

                // Build the tree structure
                var rootProcesses = new List<ProcessInfo>();

                foreach (var kvp in processInfos)
                {
                    var processInfo = kvp.Value;

                    if (processInfo.ParentPid != 0 && processInfos.ContainsKey(processInfo.ParentPid))
                    {
                        processInfos[processInfo.ParentPid].Children.Add(processInfo);
                    }
                    else
                    {
                        rootProcesses.Add(processInfo);
                    }
                }

                Debug.WriteLine($"ProcessService: Built process tree in {sw.Elapsed}.");

                // Sort all levels alphabetically by name
                SortProcessTreeByName(rootProcesses);

                Debug.WriteLine($"ProcessService: Sorted process tree in {sw.Elapsed}. Root processes count: {rootProcesses.Count}.");

                return rootProcesses;
            });
        }

        private record WmiProcessData(int ParentPid, string CommandLine);

        private Dictionary<int, WmiProcessData> GetAllProcessWmiData()
        {
            var result = new Dictionary<int, WmiProcessData>();

            try
            {
                // Single WMI query for ALL processes - much faster than per-process queries
                using var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process");
                using var results = searcher.Get();

                foreach (ManagementObject mo in results)
                {
                    try
                    {
                        var processId = Convert.ToInt32(mo["ProcessId"]);
                        var parentProcessId = Convert.ToInt32(mo["ParentProcessId"]);
                        var commandLine = mo["CommandLine"]?.ToString() ?? string.Empty;

                        result[processId] = new WmiProcessData(parentProcessId, commandLine);
                    }
                    catch
                    {
                        // Skip invalid entries
                    }
                }
            }
            catch
            {
                // If WMI fails, return empty dictionary
            }

            return result;
        }

        private void SortProcessTreeByName(List<ProcessInfo> processes)
        {
            processes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            foreach (var process in processes)
            {
                if (process.Children.Count > 0)
                {
                    SortProcessTreeByName(process.Children);
                }
            }
        }

        private bool IsServiceProcess(string processName)
        {
            lock (_lock)
            {
                return _serviceProcessNames.Contains(processName);
            }
        }

        private ImageSource? GetProcessIcon(Process process)
        {
            try
            {
                var fileName = process.MainModule?.FileName;
                if (string.IsNullOrEmpty(fileName))
                    return null;

                using var icon = Icon.ExtractAssociatedIcon(fileName);
                if (icon == null)
                    return null;

                var imageSource = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                // CRITICAL: Freeze to allow cross-thread access
                imageSource.Freeze();

                return imageSource;
            }
            catch
            {
                return null;
            }
        }

        private double GetCpuPercentage(Process process)
        {
            try
            {
                var totalProcessorTime = process.TotalProcessorTime.TotalMilliseconds;
                var processUptime = (DateTime.Now - process.StartTime).TotalMilliseconds;
                if (processUptime > 0)
                {
                    return (totalProcessorTime / processUptime / Environment.ProcessorCount) * 100;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Access denied for system processes - expected behavior
            }
            catch (InvalidOperationException)
            {
                // Process has exited
            }
            catch
            {
                // Other errors
            }

            return 0;
        }
    }
}
