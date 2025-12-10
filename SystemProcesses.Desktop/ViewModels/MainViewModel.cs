using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using SystemProcesses.Desktop.Models;
using SystemProcesses.Desktop.Services;

namespace SystemProcesses.Desktop.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IProcessService _processService;
    private readonly DispatcherTimer _refreshTimer;

    // Cache ViewModels to preserve state (Expansion, Selection)
    // Key: PID
    private readonly Dictionary<int, ProcessItemViewModel> _viewModelCache = new();

    private string _searchText = string.Empty;
    private bool _isTreeIsolated;
    private ProcessItemViewModel? _selectedProcess;
    private bool _isPaused;
    private int _refreshInterval = 1000;

    // Concurrency control
    private bool _isRefreshingProcesses;
    private bool _isRefreshPending;

    public ObservableCollection<ProcessItemViewModel> Processes { get; } = new();
    public ObservableCollection<string> RefreshIntervals { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
                Task.Run(RefreshProcessesAsync);
            }
        }
    }

    public bool IsTreeIsolated
    {
        get => _isTreeIsolated;
        set
        {
            if (_isTreeIsolated != value)
            {
                _isTreeIsolated = value;
                OnPropertyChanged();
                Task.Run(RefreshProcessesAsync);
            }
        }
    }

    public ProcessItemViewModel? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (_selectedProcess != value)
            {
                _selectedProcess = value;
                OnPropertyChanged();
                ((RelayCommand)EndProcessCommand).RaiseCanExecuteChanged();
                ((RelayCommand)EndProcessTreeCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ShowProcessDetailsCommand).RaiseCanExecuteChanged();
                ((RelayCommand)OpenProcessDirectoryCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused != value)
            {
                _isPaused = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PauseResumeText));

                if (_isPaused) _refreshTimer.Stop();
                else _refreshTimer.Start();
            }
        }
    }

    public string PauseResumeText => IsPaused ? "Resume" : "Pause";

    public string SelectedRefreshInterval
    {
        get => $"{_refreshInterval / 1000}";
        set
        {
            var seconds = int.Parse(value);
            var newInterval = seconds * 1000;
            if (_refreshInterval != newInterval)
            {
                _refreshInterval = newInterval;
                OnPropertyChanged();
                _refreshTimer.Interval = TimeSpan.FromMilliseconds(_refreshInterval);
            }
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand EndProcessCommand { get; }
    public ICommand EndProcessTreeCommand { get; }
    public ICommand ShowProcessDetailsCommand { get; }
    public ICommand OpenProcessDirectoryCommand { get; }
    public ICommand TogglePauseCommand { get; }

    public MainViewModel() : this(new ProcessService()) { }

    public MainViewModel(IProcessService processService)
    {
        _processService = processService;
        RefreshIntervals = new ObservableCollection<string> { "1", "2", "5", "10" };

        RefreshCommand = new RelayCommand(async _ => await RefreshProcessesAsync());
        EndProcessCommand = new RelayCommand(EndProcess, _ => SelectedProcess != null);
        EndProcessTreeCommand = new RelayCommand(EndProcessTree, _ => SelectedProcess != null);
        ShowProcessDetailsCommand = new RelayCommand(ShowProcessDetails, _ => SelectedProcess != null);
        OpenProcessDirectoryCommand = new RelayCommand(OpenProcessDirectory, _ => SelectedProcess != null);
        TogglePauseCommand = new RelayCommand(_ => IsPaused = !IsPaused);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_refreshInterval) };
        _refreshTimer.Tick += async (s, e) =>
        {
            // Don't stop timer, just skip if busy
            if (!_isRefreshingProcesses) await RefreshProcessesAsync();
        };
        _refreshTimer.Start();

        Task.Run(async () => await RefreshProcessesAsync());
    }

    private async Task RefreshProcessesAsync()
    {
        // FIX: Concurrency Loop
        // If a refresh is already running, mark pending so it runs again immediately after.
        // This ensures rapid updates (like typing) are not dropped.
        if (_isRefreshingProcesses)
        {
            _isRefreshPending = true;
            return;
        }

        _isRefreshingProcesses = true;

        try
        {
            do
            {
                _isRefreshPending = false;

                // 1. Get updated data graph
                var rootInfos = await _processService.GetProcessTreeAsync();

                // 2. Apply Filters
                var filteredRoots = ApplyFilters(rootInfos);

                // 3. Update UI on Dispatcher
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SyncProcessCollection(Processes, filteredRoots);
                });

            } while (_isRefreshPending);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            _isRefreshingProcesses = false;
        }
    }

    private List<ProcessInfo> ApplyFilters(List<ProcessInfo> roots)
    {
        if (string.IsNullOrWhiteSpace(SearchText) && (!IsTreeIsolated || SelectedProcess == null))
        {
            return roots;
        }

        if (IsTreeIsolated && SelectedProcess != null)
        {
            var target = FindProcessInGraph(roots, SelectedProcess.Pid);
            return target != null ? new List<ProcessInfo> { target } : new List<ProcessInfo>();
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            return FilterGraphBySearch(roots, SearchText);
        }

        return roots;
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
            bool match = node.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
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
                    Icon = node.Icon
                };

                // ProcessInfo.Children is a get-only List, so we use AddRange
                clone.Children.AddRange(filteredChildren);

                result.Add(clone);
            }
        }
        return result;
    }

    #region Cache Helpers
    public void BuildCache(ProcessItemViewModel root)
    {
        // Iterative stack-based traversal avoids recursion allocations
        var stack = new Stack<ProcessItemViewModel>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            _viewModelCache[node.Pid] = node;

            // Push children without LINQ/foreach to avoid allocations
            var children = node.Children;
            for (int i = 0; i < children.Count; i++)
            {
                stack.Push(children[i]);
            }
        }
    }

    public bool RemoveFromCache(int id, bool includeChildren)
    {
        if (!_viewModelCache.TryGetValue(id, out var node))
            return false;

        // Remove recursively from dictionary
        var stack = new Stack<ProcessItemViewModel>();
        stack.Push(node);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            _viewModelCache.Remove(current.Pid);

            if (includeChildren)
            {
                var children = current.Children;
                for (int i = 0; i < children.Count; i++)
                {
                    stack.Push(children[i]);
                }
            }
        }
        return true;
    }
    #endregion

    private ProcessInfo? FindProcessById(int pid)
    {
        return _viewModelCache.TryGetValue(pid, out var vm) ? vm!.ProcessInfo : null;
    }
    private void SyncProcessCollection(ObservableCollection<ProcessItemViewModel> collection, List<ProcessInfo> sourceInfos)
    {
        var sourcePids = new HashSet<int>(sourceInfos.Select(x => x.Pid));

        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (!sourcePids.Contains(collection[i].Pid))
            {
                // FIX: includeChildren = true to prevent leaks
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

            vm.Refresh();
            SyncProcessCollection(vm.Children, info.Children);
        }
    }

    private void EndProcess(object? parameter)
    {
        if (SelectedProcess == null)
            return;

        var result = MessageBox.Show(
            $"Are you sure you want to end process '{SelectedProcess.Name}' (PID: {SelectedProcess.Pid})?",
            "End Process",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var process = Process.GetProcessById(SelectedProcess.Pid);
                process.Kill();
                MessageBox.Show("Process terminated successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to end process: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void EndProcessTree(object? parameter)
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

    private void StopProcessAndChildren(int pid)
    {
        var processInfo = FindProcessById(pid);
        if (processInfo == null)
            return;

        // Kill children first
        foreach (var child in processInfo.Children)
        {
            StopProcessAndChildren(child.Pid);
        }

        // Then kill the process itself
        try
        {
            var process = Process.GetProcessById(pid);
            process.Kill();
        }
        catch
        {
            // Process may have already exited
        }
    }

    private void ShowProcessDetails(object? parameter)
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

    private void OpenProcessDirectory(object? parameter)
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

    // FIX: Updated to match BytesToAutoFormatConverter logic (TB, GB, MB, KB, B)
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

    public void Dispose() { _refreshTimer?.Stop(); }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}