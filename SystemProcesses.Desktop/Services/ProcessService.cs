using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                var processInfos = new Dictionary<int, ProcessInfo>();
                var parentChildMap = GetParentChildMap();

                // Get all processes
                var processes = Process.GetProcesses();

                foreach (var process in processes)
                {
                    try
                    {
                        var info = new ProcessInfo
                        {
                            Pid = process.Id,
                            Name = process.ProcessName,
                            MemoryBytes = process.WorkingSet64,
                            VirtualMemoryBytes = process.VirtualMemorySize64,
                            ParentPid = parentChildMap.ContainsKey(process.Id) ? parentChildMap[process.Id] : 0,
                            IsService = IsServiceProcess(process.ProcessName)
                        };

                        // Get command line parameters
                        try
                        {
                            info.Parameters = GetCommandLine(process.Id);
                        }
                        catch
                        {
                            info.Parameters = string.Empty;
                        }

                        // Get CPU percentage (simplified - would need timing for accurate measurement)
                        // Note: Many system processes throw Access Denied - this is expected behavior
                        info.CpuPercentage = GetCpuPercentage(process);

                        // Get icon
                        try
                        {
                            info.Icon = GetProcessIcon(process);
                        }
                        catch
                        {
                            info.Icon = null;
                        }

                        processInfos[process.Id] = info;
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

                // Build the tree structure
                var rootProcesses = new List<ProcessInfo>();

                foreach (var kvp in processInfos)
                {
                    var processInfo = kvp.Value;

                    if (processInfo.ParentPid != 0 && processInfos.ContainsKey(processInfo.ParentPid))
                    {
                        // Add to parent's children
                        processInfos[processInfo.ParentPid].Children.Add(processInfo);
                    }
                    else
                    {
                        // Root process (no parent or parent not found)
                        rootProcesses.Add(processInfo);
                    }
                }

                // Sort all levels alphabetically by name
                SortProcessTreeByName(rootProcesses);

                return rootProcesses;
            });
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

        private Dictionary<int, int> GetParentChildMap()
        {
            var map = new Dictionary<int, int>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId FROM Win32_Process");
                using var results = searcher.Get();

                foreach (ManagementObject mo in results)
                {
                    try
                    {
                        var processId = Convert.ToInt32(mo["ProcessId"]);
                        var parentProcessId = Convert.ToInt32(mo["ParentProcessId"]);
                        map[processId] = parentProcessId;
                    }
                    catch
                    {
                        // Skip invalid entries
                    }
                }
            }
            catch
            {
                // If WMI fails, return empty map
            }

            return map;
        }

        private string GetCommandLine(int processId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
                using var results = searcher.Get();

                foreach (ManagementObject mo in results)
                {
                    var commandLine = mo["CommandLine"]?.ToString() ?? string.Empty;
                    return commandLine;
                }
            }
            catch
            {
                // Access denied or other error
            }

            return string.Empty;
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

                var icon = System.Drawing.Icon.ExtractAssociatedIcon(fileName);
                if (icon == null)
                    return null;

                return Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
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
