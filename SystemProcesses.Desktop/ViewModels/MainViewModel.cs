using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using SystemProcesses.Desktop.Models;
using SystemProcesses.Desktop.Services;
using SystemProcesses.Desktop.Utils;

namespace SystemProcesses.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IProcessService _processService;
    private readonly DispatcherTimer _refreshTimer;

    // Cache ViewModels to preserve state (Expansion, Selection)
    // Key: PID
    private readonly Dictionary<int, ProcessItemViewModel> _viewModelCache = new();

    // Reusable collections for SyncProcessCollection to ensure Zero-Allocation
    private readonly HashSet<int> _reusablePidSet = new();
    private readonly Stack<ProcessItemViewModel> _reusableStack = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    private int? _isolationTargetPid;

    // Manual Property Implementation intenionally (Replaces [ObservableProperty])
    private bool _isTreeIsolated;
    public bool IsTreeIsolated
    {
        get => _isTreeIsolated;
        set
        {
            if (_isTreeIsolated == value) return;

            if (value)
            {
                // ACTIVATE: Capture the current selection as the fixed root
                if (SelectedProcess != null)
                {
                    _isolationTargetPid = SelectedProcess.Pid;
                    _isTreeIsolated = true;
                }
                else
                {
                    // Cannot isolate if nothing is selected; ignore the toggle
                    OnPropertyChanged(); // Notify to revert UI checkmark if bound
                    return;
                }
            }
            else
            {
                // DEACTIVATE: Release the lock
                _isolationTargetPid = null;
                _isTreeIsolated = false;
            }

            OnPropertyChanged();
            Task.Run(RefreshProcessesAsync);
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GracefulEndProcessCommand))]
    [NotifyCanExecuteChangedFor(nameof(GracefulEndProcessTreeCommand))]
    [NotifyCanExecuteChangedFor(nameof(EndProcessCommand))]
    [NotifyCanExecuteChangedFor(nameof(EndProcessTreeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowProcessDetailsCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenProcessDirectoryCommand))]
    private ProcessItemViewModel? _selectedProcess;

    [ObservableProperty] private int _totalProcessCount;
    [ObservableProperty] private int _totalThreadCount;
    [ObservableProperty] private int _totalHandleCount;
    [ObservableProperty] private long _totalMemoryBytes;
    [ObservableProperty] private double _totalCpuUsage;
    [ObservableProperty] private long _totalPhysicalMemory;
    [ObservableProperty] private long _availablePhysicalMemory;
    [ObservableProperty] private long _totalCommitLimit;
    [ObservableProperty] private long _availableCommitLimit;
    [ObservableProperty] private long _totalIoBytesPerSec;
    [ObservableProperty] private double _diskActivePercent;

    [ObservableProperty]
    private string _trayToolTipTextHeader = "System Processes\nInitializing...";

    [ObservableProperty]
    private string _trayToolTipTextBody = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseResumeText))]
    private bool _isPaused;

    private int _refreshInterval = 1000;

    // Concurrency control
    private readonly System.Threading.SemaphoreSlim _refreshLock = new(1, 1);
    private bool _isRefreshPending;

    public ObservableCollection<ProcessItemViewModel> Processes { get; } = new();
    public ObservableCollection<string> RefreshIntervals { get; }

    public string PauseResumeText => IsPaused ? "Resume" : "Pause";

    public string SelectedRefreshInterval
    {
        get => $"{_refreshInterval / 1000}";
        set
        {
            if (!int.TryParse(value, out int seconds))
            {
                IsPaused = true;
                return;
            }
            else
            {
                IsPaused = false;
            }
            var newInterval = seconds * 1000;
            if (_refreshInterval != newInterval)
            {
                _refreshInterval = newInterval;
                OnPropertyChanged();
                _refreshTimer.Interval = TimeSpan.FromMilliseconds(_refreshInterval);
            }
        }
    }

    public MainViewModel() : this(new ProcessService()) { }

    public MainViewModel(IProcessService processService)
    {
        _processService = processService;
        RefreshIntervals = new ObservableCollection<string> { "1", "2", "5", "10", "20", "Disabled" };

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_refreshInterval) };
        _refreshTimer.Tick += async (s, e) =>
        {
            // Don't stop timer, just skip if busy
            if (_refreshLock.CurrentCount > 0) await RefreshProcessesAsync();
        };
        _refreshTimer.Start();

        Task.Run(RefreshProcessesAsync);
    }

    partial void OnSearchTextChanged(string value) => Task.Run(RefreshProcessesAsync);
    partial void OnIsPausedChanged(bool value)
    {
        if (_isPaused) _refreshTimer.Stop();
        else _refreshTimer.Start();
    }

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    [RelayCommand]
    private async Task RefreshProcessesAsync()
    {
        // FIX: Concurrency Loop
        // If a refresh is already running, mark pending so it runs again immediately after.
        // This ensures rapid updates (like typing) are not dropped.
        // Non-blocking check to coalesce rapid updates (like typing)
        if (_refreshLock.CurrentCount == 0)
        {
            _isRefreshPending = true;
            return;
        }

        await _refreshLock.WaitAsync();

        try
        {
            do
            {
                _isRefreshPending = false;
                var (rootInfos, stats) = await _processService.GetProcessTreeAsync();
                var filteredRoots = ApplyFilters(rootInfos);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SyncProcessCollection(Processes, filteredRoots);

                    // Update Stats
                    TotalProcessCount = stats.ProcessCount;
                    TotalThreadCount = stats.ThreadCount;
                    TotalHandleCount = stats.HandleCount;
                    TotalMemoryBytes = stats.TotalMemory;
                    TotalCpuUsage = stats.TotalCpu;
                    TotalPhysicalMemory = stats.TotalPhysicalMemory;
                    AvailablePhysicalMemory = stats.AvailablePhysicalMemory;
                    TotalCommitLimit = stats.TotalCommitLimit;
                    AvailableCommitLimit = stats.AvailableCommitLimit;
                    TotalIoBytesPerSec = stats.TotalIoBytesPerSec;
                    DiskActivePercent = stats.DiskActivePercent;

                    // ADDED: Update Tray Tooltip
                    UpdateTrayToolTip(stats);
                });

            } while (_isRefreshPending);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private List<ProcessInfo> ApplyFilters(List<ProcessInfo> roots)
    {
        // Check if we have an active isolation target
        if (IsTreeIsolated && _isolationTargetPid.HasValue)
        {
            // Use the CAPTURED Pid, not the current selection
            var target = FindProcessInGraph(roots, _isolationTargetPid.Value);

            // If the isolated process still exists, show it. 
            // If it died, show empty list (or could fallback to full list, but empty is safer for "Isolation")
            return target != null ? new List<ProcessInfo> { target } : new List<ProcessInfo>();
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return roots;
        }

        return FilterGraphBySearch(roots, SearchText);
    }

    private ProcessInfo? FindProcessInGraph(List<ProcessInfo> nodes, int pid)
    {
        foreach (var node in nodes)
        {
            if (node.Pid == pid) return node;
            var found = FindProcessInGraph(node.Children, pid);
            if (found != null) return found;
        }
        return null;
    }

    private List<ProcessInfo> FilterGraphBySearch(List<ProcessInfo> nodes, string text)
    {
        var result = new List<ProcessInfo>();
        foreach (var node in nodes)
        {
            bool match = node.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || node.PidText.Contains(text, StringComparison.OrdinalIgnoreCase);
            var filteredChildren = FilterGraphBySearch(node.Children, text);

            if (match || filteredChildren.Count > 0)
            {
                // FIX: Clone the node to attach filtered children.
                // If we don't clone, we attach the original node which has ALL children,
                // defeating the purpose of the filter for the view.
                var clone = new ProcessInfo
                {
                    Pid = node.Pid,
                    Name = node.Name,
                    CpuPercentage = node.CpuPercentage,
                    MemoryBytes = node.MemoryBytes,
                    VirtualMemoryBytes = node.VirtualMemoryBytes,
                    Parameters = node.Parameters,
                    IsService = node.IsService,
                    ParentPid = node.ParentPid,
                    ProcessPath = node.ProcessPath
                };
                // ProcessInfo.Children is a get-only List, so we use AddRange
                clone.Children.AddRange(filteredChildren);
                result.Add(clone);
            }
        }
        return result;
    }

    private void SyncProcessCollection(ObservableCollection<ProcessItemViewModel> collection, List<ProcessInfo> sourceInfos, int depth = 0)
    {
        // Zero-Allocation: Reuse the HashSet
        _reusablePidSet.Clear();
        foreach (var info in sourceInfos)
        {
            _reusablePidSet.Add(info.Pid);
        }

        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (!_reusablePidSet.Contains(collection[i].Pid))
            {
                RemoveFromCache(collection[i].Pid, true);
                collection.RemoveAt(i);
            }
        }

        for (int i = 0; i < sourceInfos.Count; i++)
        {
            var info = sourceInfos[i];
            ProcessItemViewModel vm;

            if (!_viewModelCache.TryGetValue(info.Pid, out vm))
            {
                vm = new ProcessItemViewModel(info);
                BuildCache(vm);
                collection.Insert(i, vm);
            }
            else
            {
                if (!collection.Contains(vm))
                {
                    collection.Insert(i, vm);
                }
                else
                {
                    int oldIndex = collection.IndexOf(vm);
                    if (oldIndex != i)
                    {
                        collection.Move(oldIndex, i);
                    }
                }
            }

            vm.Depth = depth;
            vm.Refresh();
            SyncProcessCollection(vm.Children, info.Children, depth + 1);
        }
    }

    public void BuildCache(ProcessItemViewModel root)
    {
        // Iterative stack-based traversal avoids recursion allocations
        _reusableStack.Clear();
        _reusableStack.Push(root);

        while (_reusableStack.Count > 0)
        {
            var node = _reusableStack.Pop();
            _viewModelCache[node.Pid] = node;

            // Push children without LINQ/foreach to avoid allocations
            var children = node.Children;
            for (int i = 0; i < children.Count; i++)
            {
                _reusableStack.Push(children[i]);
            }
        }
    }

    public bool RemoveFromCache(int id, bool includeChildren)
    {
        if (!_viewModelCache.TryGetValue(id, out var node))
            return false;

        // Remove recursively from dictionary
        _reusableStack.Clear();
        _reusableStack.Push(node);

        while (_reusableStack.Count > 0)
        {
            var current = _reusableStack.Pop();
            _viewModelCache.Remove(current.Pid);

            if (includeChildren)
            {
                var children = current.Children;
                for (int i = 0; i < children.Count; i++)
                {
                    _reusableStack.Push(children[i]);
                }
            }
        }
        return true;
    }

    private void UpdateTrayToolTip(SystemStats stats)
    {
        // Format:
        // CPU: 12.5%  RAM: 45%  Disk: 5%
        // 1. ProcessA (10.2%)
        // 2. ProcessB (1.1%)
        // ...

        double ramPercent = 0;
        if (stats.TotalPhysicalMemory > 0)
            ramPercent = ((double)(stats.TotalPhysicalMemory - stats.AvailablePhysicalMemory) / stats.TotalPhysicalMemory) * 100;

        TrayToolTipTextHeader = $"CPU: {stats.TotalCpu:F0}%  RAM: {ramPercent:F0}%  Disk: {stats.DiskActivePercent:F0}%";

        using var sb = StringBuilderPool.Rent();

        if (stats.Top5Processes != null)
        {
            int count = 0;
            foreach (var p in stats.Top5Processes)
            {
                if (p == null) break;
                sb.Builder.AppendLine($"{++count}. {p.Name} ({p.CpuPercentage:F1}%)");
            }
        }

        Debug.WriteLine($"Length: {sb.Builder.Length}");
        TrayToolTipTextBody = sb.Build();
    }

    [RelayCommand]
    private void ShowApplication()
    {
        var window = Application.Current.MainWindow;
        if (window != null)
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Show();
            window.Activate();
        }
    }

    [RelayCommand]
    private void ExitApplication()
    {
        Application.Current.Shutdown();
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private void CopyProcessPath()
    {
        if (SelectedProcess == null) return;

        // ProcessPath is already cached in ProcessInfo
        var path = SelectedProcess.ProcessInfo.ProcessPath;

        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                Clipboard.SetText(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("Path is unavailable for this process.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private async Task GracefulEndProcessAsync()
    {
        if (SelectedProcess == null) return;

        // Capture state to prevent race conditions
        int pid = SelectedProcess.Pid;
        string name = SelectedProcess.Name;

        if (MessageBox.Show($"Send close request to '{name}' (PID: {pid})?",
            "Graceful End", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await Task.Run(() =>
        {
            try
            {
                using var process = Process.GetProcessById(pid);

                // PID Reuse Check: Verify StartTime if possible (requires capturing it earlier, 
                // but here we assume short duration between selection and action).
                // Ideally, ProcessInfo should carry StartTime.

                process.Refresh();
                if (process.CloseMainWindow())
                {
                    if (!process.WaitForExit(3000))
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (MessageBox.Show($"Process '{name}' did not close within 3 seconds.\nForce kill it?",
                                "Process Unresponsive", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                            {
                                process.Kill();
                            }
                        });
                    }
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (MessageBox.Show($"Could not send close request (No Window or Unresponsive).\nForce kill '{name}'?",
                            "No Window Found", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        {
                            process.Kill();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private async Task GracefulEndProcessTreeAsync()
    {

        var selected = SelectedProcess;
        if (selected == null) return;

        int rootPid = selected.Pid;
        string rootName = selected.Name;

        if (MessageBox.Show($"Send close request to '{rootName}' (PID: {rootPid}) and all children?",
            "Graceful End Tree", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        // 1. Collect all PIDs in the tree (Bottom-Up approach preferred for closing)
        var pidsToClose = new List<int>();
        void CollectPids(ProcessItemViewModel vm)
        {
            foreach (var child in vm.Children) CollectPids(child);
            pidsToClose.Add(vm.Pid);
        }

        // Use cache to find the current tree structure
        if (_viewModelCache.TryGetValue(rootPid, out var rootVm))
        {
            CollectPids(rootVm);
        }
        else
        {
            pidsToClose.Add(rootPid);
        }

        // 2. Send Close Requests asynchronously
        await Task.Run(() =>
        {
            foreach (var pid in pidsToClose)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    p.Refresh();
                    p.CloseMainWindow();
                }
                catch { /* Ignore access denied or already exited */ }
            }
        });

        var remaining = new HashSet<int>(pidsToClose);
        var remainingTries = 3;
        var delay = 1000;
        var tryNumber = 0;
        while (true)
        {
            ++tryNumber;

            // 3. Verify
            var closedPids = new HashSet<int>();
            foreach (var pid in remaining)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (p == null)
                    {
                        closedPids.Add(pid);
                    }
                    else
                    {
                        p.Refresh();
                        if (p.HasExited)
                        {
                            closedPids.Add(pid);
                        }
                    }
                }
                catch { /* Assume exited if cannot access */ }
            }

            foreach (var pid in closedPids)
            {
                remaining.Remove(pid);
            }

            if (remaining.Count == 0)
            {
                break;
            }
            else
            {
                if (--remainingTries == 0)
                    break;

                remaining.Clear();
            }

            // 4. Wait for processes to exit
            await Task.Delay(delay * tryNumber);
        }


        if (remaining.Count > 0)
        {
            if (MessageBox.Show($"{remaining.Count} processes in the tree are still running.\nForce kill the entire tree?",
                "Incomplete Shutdown", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    StopProcessAndChildren(rootPid);
                    MessageBox.Show("Tree force terminated.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error terminating tree: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        else
        {
            MessageBox.Show("All processes in tree closed successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private void EndProcess()
    {
        if (SelectedProcess == null) return;
        if (MessageBox.Show($"End process '{SelectedProcess.Name}' (PID: {SelectedProcess.Pid})?", "End Process", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            try
            {
                using var process = Process.GetProcessById(SelectedProcess.Pid);
                process.Kill();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed: {ex.Message}");
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private void EndProcessTree()
    {
        if (SelectedProcess == null)
            return;

        var result = MessageBox.Show(
            $"Are you sure you want to end process tree for '{SelectedProcess.Name}' (PID: {SelectedProcess.Pid}) and all its children?",
            "End Process Tree",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                StopProcessAndChildren(SelectedProcess.Pid);
                MessageBox.Show("Process tree terminated successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to end process tree: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private bool CanExecuteProcessAction() => SelectedProcess != null;

    private void StopProcessAndChildren(int pid)
    {
        if (!_viewModelCache.TryGetValue(pid, out var vm))
            return;

        var processInfo = vm.ProcessInfo;

        // Stop children first
        foreach (var child in processInfo.Children)
        {
            StopProcessAndChildren(child.Pid);
        }

        // Then stop the process itself
        try
        {
            using var process = Process.GetProcessById(pid);

            // PID Reuse Check: Verify StartTime if possible (requires capturing it earlier, 
            // but here we assume short duration between selection and action).
            // Ideally, ProcessInfo should carry StartTime.

            process.Refresh();
            process.Kill();
        }
        catch
        {
            // Process may have already exited
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private void ShowProcessDetails()
    {
        if (SelectedProcess == null) return;

        var details = new StringBuilder();
        details.AppendLine($"Process Name: {SelectedProcess.Name}");
        details.AppendLine($"Process ID: {SelectedProcess.Pid}");
        details.AppendLine($"CPU Usage: {SelectedProcess.CpuPercentage:F2}%");
        details.AppendLine($"Memory: {FormatBytes(SelectedProcess.MemoryBytes)}");
        details.AppendLine($"Virtual Memory: {FormatBytes(SelectedProcess.VirtualMemoryBytes)}");
        details.AppendLine($"Is Service: {(SelectedProcess.IsService ? "Yes" : "No")}");

        try
        {
            var process = Process.GetProcessById(SelectedProcess.Pid);
            details.AppendLine($"Start Time: {process.StartTime}");
            details.AppendLine($"Total Processor Time: {process.TotalProcessorTime}");
            details.AppendLine($"Threads: {process.Threads.Count}");
            details.AppendLine($"Handles: {process.HandleCount}");
            if (process.MainModule != null)
                details.AppendLine($"File Path: {process.MainModule.FileName}");
        }
        catch
        {
            details.AppendLine("\n(Extended details unavailable - Access Denied or Process Exited)");
        }

        if (!string.IsNullOrWhiteSpace(SelectedProcess.Parameters))
        {
            details.AppendLine($"\nCommand Line:");
            details.AppendLine(SelectedProcess.Parameters);
        }

        MessageBox.Show(details.ToString(), "Process Details", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private void OpenProcessDirectory()
    {
        if (SelectedProcess == null)
            return;

        try
        {
            var process = Process.GetProcessById(SelectedProcess.Pid);
            var fileName = process.MainModule?.FileName;

            if (!string.IsNullOrEmpty(fileName))
            {
                var directory = Path.GetDirectoryName(fileName);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Process.Start("explorer.exe", directory);
                }
                else
                {
                    MessageBox.Show("Process directory not found.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Cannot access process file path.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open process directory: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;
        const long TB = GB * 1024;

        if (bytes >= TB) return $"{bytes / (double)TB:F2} TB";
        if (bytes >= GB) return $"{bytes / (double)GB:F2} GB";
        if (bytes >= MB) return $"{bytes / (double)MB:F2} MB";
        if (bytes >= KB) return $"{bytes / (double)KB:F2} KB";
        return $"{bytes} B";
    }

    public void Dispose() => _refreshTimer?.Stop();
}