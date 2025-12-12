using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Serilog;

using SystemProcesses.Desktop.Helpers;
using SystemProcesses.Desktop.Models;
using SystemProcesses.Desktop.Services;

namespace SystemProcesses.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IProcessService processService;
    private readonly ILiteDialogService liteDialogService;
    private readonly IImageLoaderService imageLoaderService;
    private readonly DispatcherTimer refreshTimer;

    // Flag to prevent infinite loops during shutdown
    public bool IsExitConfirmed { get; private set; }

    // Cache ViewModels to preserve state (Expansion, Selection)
    // Key: PID
    private readonly Dictionary<int, ProcessItemViewModel> viewModelCache = [];

    // Reusable collections for SyncProcessCollection to ensure Zero-Allocation
    private readonly HashSet<int> reusablePidSet = [];

    private readonly Stack<ProcessItemViewModel> reusableStack = new();

    // Zero-Allocation Cache for strings "0" to "100"
    private static readonly BitmapSource[] cpuIconsCache = new BitmapSource[101];

    [ObservableProperty]
    private ImageSource cpuTrayIconImageSource = cpuIconsCache[0];

    [ObservableProperty]
    private string searchText = string.Empty;

    private int? isolationTargetPid;

    // Manual Property Implementation intenionally (Replaces [ObservableProperty])
    private bool isTreeIsolated;

    public bool IsTreeIsolated
    {
        get => isTreeIsolated;
        set
        {
            if (isTreeIsolated == value) return;

            if (value)
            {
                // ACTIVATE: Capture the current selection as the fixed root
                if (SelectedProcess != null)
                {
                    isolationTargetPid = SelectedProcess.Pid;
                    isTreeIsolated = true;
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
                isolationTargetPid = null;
                isTreeIsolated = false;
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
    private ProcessItemViewModel? selectedProcess;

    [ObservableProperty] private int totalProcessCount;
    [ObservableProperty] private int totalThreadCount;
    [ObservableProperty] private int totalHandleCount;
    [ObservableProperty] private long totalMemoryBytes;
    [ObservableProperty] private double totalCpuUsage;
    [ObservableProperty] private long totalPhysicalMemory;
    [ObservableProperty] private long availablePhysicalMemory;
    [ObservableProperty] private long totalCommitLimit;
    [ObservableProperty] private long availableCommitLimit;
    [ObservableProperty] private long totalIoBytesPerSec;
    [ObservableProperty] private double diskActivePercent;
    [ObservableProperty] private double ramFreePercentage;
    [ObservableProperty] private double vmFreePercentage;

    [ObservableProperty] private string storageStatsText = string.Empty;
    [ObservableProperty] private string storageStatsTrayText = string.Empty;

    [ObservableProperty]
    private string trayToolTipTextHeader = "System Processes\nInitializing...";

    [ObservableProperty]
    private string trayToolTipTextBody = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseResumeText))]
    private bool isPaused;

    private int refreshInterval = 1000;

    // Concurrency control
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    private bool isRefreshPending;

    public ObservableCollection<ProcessItemViewModel> Processes { get; } = [];
    public ObservableCollection<string> RefreshIntervals { get; }

    public string PauseResumeText => IsPaused ? "Resume" : "Pause";

    public string SelectedRefreshInterval
    {
        get => $"{refreshInterval / 1000}";
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
            if (refreshInterval != newInterval)
            {
                refreshInterval = newInterval;
                OnPropertyChanged();
                refreshTimer.Interval = TimeSpan.FromMilliseconds(refreshInterval);
            }
        }
    }

    public MainViewModel() : this(new ProcessService(), new LiteDialogService(), new ImageLoaderService())
    {
    }

    public MainViewModel(IProcessService processService, ILiteDialogService liteDialogService, IImageLoaderService imageLoaderService)
    {
        this.processService = processService;
        this.liteDialogService = liteDialogService;
        this.imageLoaderService = imageLoaderService;

        InitializeCpuIconsCacheAsync().GetAwaiter().GetResult();

        RefreshIntervals = ["1", "2", "5", "10", "20", "Disabled"];

        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(refreshInterval) };
        refreshTimer.Tick += async (s, e) =>
        {
            // Don't stop timer, just skip if busy
            if (refreshLock.CurrentCount > 0) await RefreshProcessesAsync();
        };
        refreshTimer.Start();

        Task.Run(RefreshProcessesAsync);
    }

    private async Task InitializeCpuIconsCacheAsync()
    {
        cpuIconsCache[0] = await imageLoaderService.LoadAsync("pack://application:,,,/Resources/Images/TrayIcon/SystemProcess-Tray.ico", 32, 32);
        for (int i = 1; i < 10; i++)
        {
            cpuIconsCache[i] = await imageLoaderService.LoadAsync($"pack://application:,,,/Resources/Images/TrayIcon/SystemProcess-Tray-0{i}.ico", 32, 32);
        }
        for (int i = 10; i < 100; i++)
        {
            cpuIconsCache[i] = await imageLoaderService.LoadAsync($"pack://application:,,,/Resources/Images/TrayIcon/SystemProcess-Tray-{i}.ico", 32, 32);
        }
        cpuIconsCache[100] = await imageLoaderService.LoadAsync("pack://application:,,,/Resources/Images/TrayIcon/SystemProcess-Tray-100.ico", 32, 32);
    }

    partial void OnSearchTextChanged(string value) => Task.Run(RefreshProcessesAsync);

    partial void OnIsPausedChanged(bool value)
    {
        if (isPaused) refreshTimer.Stop();
        else refreshTimer.Start();
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
        if (refreshLock.CurrentCount == 0)
        {
            isRefreshPending = true;
            return;
        }

        await refreshLock.WaitAsync();

        try
        {
            do
            {
                isRefreshPending = false;
                var (rootInfos, stats) = await processService.GetProcessTreeAsync();
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

                    // Calculate Percentages (Zero-Alloc)
                    if (stats.TotalPhysicalMemory > 0)
                        RamFreePercentage = (double)stats.AvailablePhysicalMemory / stats.TotalPhysicalMemory * 100.0;

                    if (stats.TotalCommitLimit > 0)
                        VmFreePercentage = (double)stats.AvailableCommitLimit / stats.TotalCommitLimit * 100.0;

                    TotalIoBytesPerSec = stats.TotalIoBytesPerSec;
                    DiskActivePercent = stats.DiskActivePercent;

                    // Update Storage Stats
                    UpdateStorageStats(stats);

                    // Update Tray Tooltip
                    UpdateTrayState(stats);
                });
            } while (isRefreshPending);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error: {message}", ex.Message);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private List<ProcessInfo> ApplyFilters(List<ProcessInfo> roots)
    {
        // Check if we have an active isolation target
        if (IsTreeIsolated && isolationTargetPid.HasValue)
        {
            // Use the CAPTURED Pid, not the current selection
            var target = FindProcessInGraph(roots, isolationTargetPid.Value);

            // If the isolated process still exists, show it.
            // If it died, show empty list (or could fallback to full list, but empty is safer for "Isolation")
            return target != null ? [target] : [];
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
        reusablePidSet.Clear();
        foreach (var info in sourceInfos)
        {
            reusablePidSet.Add(info.Pid);
        }

        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (!reusablePidSet.Contains(collection[i].Pid))
            {
                RemoveFromCache(collection[i].Pid, true);
                collection.RemoveAt(i);
            }
        }

        for (int i = 0; i < sourceInfos.Count; i++)
        {
            var info = sourceInfos[i];
            ProcessItemViewModel? vm;

            if (!viewModelCache.TryGetValue(info.Pid, out vm))
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
        reusableStack.Clear();
        reusableStack.Push(root);

        while (reusableStack.Count > 0)
        {
            var node = reusableStack.Pop();
            viewModelCache[node.Pid] = node;

            // Push children without LINQ/foreach to avoid allocations
            var children = node.Children;
            for (int i = 0; i < children.Count; i++)
            {
                reusableStack.Push(children[i]);
            }
        }
    }

    public bool RemoveFromCache(int id, bool includeChildren)
    {
        if (!viewModelCache.TryGetValue(id, out var node))
            return false;

        // Remove recursively from dictionary
        reusableStack.Clear();
        reusableStack.Push(node);

        while (reusableStack.Count > 0)
        {
            var current = reusableStack.Pop();
            viewModelCache.Remove(current.Pid);

            if (includeChildren)
            {
                var children = current.Children;
                for (int i = 0; i < children.Count; i++)
                {
                    reusableStack.Push(children[i]);
                }
            }
        }
        return true;
    }

    private void UpdateStorageStats(SystemStats stats)
    {
        if (stats.DriveCount == 0 || stats.Drives == null)
        {
            StorageStatsText = string.Empty;
            StorageStatsTrayText = string.Empty;
            return;
        }

        using var sb = StringBuilderPool.Rent();
        using var sb2 = StringBuilderPool.Rent();
        for (int i = 0; i < stats.DriveCount; i++)
        {
            var d = stats.Drives[i];
            if (sb.Builder.Length > 0) sb.Builder.Append("   ");

            double percent = 0;
            if (d.TotalSize > 0)
                percent = (double)d.AvailableFreeSpace / d.TotalSize * 100.0;

            // Format: C: 20 GB / 200 GB (Available 10%)
            sb.Builder.Append($"{d.Letter}: {FormatBytes(d.AvailableFreeSpace)} ({percent:F0}%)");
            //sb.Builder.Append($"{d.Letter}: {FormatBytes(d.AvailableFreeSpace)} / {FormatBytes(d.TotalSize)} ({percent:F0}%)");
            sb2.Builder.AppendLine($"{d.Letter}: {FormatBytes(d.AvailableFreeSpace)} ({percent:F0}%)");
        }
        StorageStatsText = sb.Build();
        StorageStatsTrayText = sb2.Build().TrimEnd();
    }

    private void UpdateTrayState(SystemStats stats)
    {
        // ...... PART 1: Update Icon (CPU Number) ......

        // Clamp value to 0-100 to ensure we never go out of bounds of our cache
        // Cast to int is safe because we only need whole numbers for the icon
        int cpuInt = (int)Math.Clamp(stats.TotalCpu, 0, 100);

        // Use the static cache to avoid new allocation
        CpuTrayIconImageSource = cpuIconsCache[cpuInt];

        // ...... PART 2: Update Tooltip (StringBuilder Pool) ......

        // RAM
        double ramPercent = 0;
        if (stats.TotalPhysicalMemory > 0)
            ramPercent = ((double)(stats.TotalPhysicalMemory - stats.AvailablePhysicalMemory) / stats.TotalPhysicalMemory) * 100;

        // VM
        double vmPercent = 0;
        if (stats.TotalCommitLimit > 0)
            vmPercent = ((double)(stats.TotalCommitLimit - stats.AvailableCommitLimit) / stats.TotalCommitLimit) * 100;

        TrayToolTipTextHeader = $"CPU: {stats.TotalCpu:F0}%  RAM: {ramPercent:F0}%  VM: {vmPercent:F0}%  Disk: {stats.DiskActivePercent:F0}%";

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

        TrayToolTipTextBody = sb.Build().TrimEnd();
    }

    // Shared Confirmation Logic
    public async Task<bool> ConfirmExitAsync()
    {
        if (IsExitConfirmed) return true;

        if (await liteDialogService.ShowAsync(new LiteDialogRequest(
                title: "Exit Application",
                message: "Are you sure you want to exit System Processes?",
                buttons: LiteDialogButton.YesNo,
                image: LiteDialogImage.Question
            )) == LiteDialogResult.Yes)
        {
            IsExitConfirmed = true;
            return true;
        }

        return false;
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
    private async Task ExitApplication()
    {
        if (await ConfirmExitAsync())
        {
            Application.Current.Shutdown();
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private async Task CopyProcessPath()
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
                Log.Warning(ex, "Ignored");
                await liteDialogService.ShowAsync(new LiteDialogRequest(
                    title: "Error",
                    message: $"Failed to copy path: {ex.Message}",
                    buttons: LiteDialogButton.OK,
                    image: LiteDialogImage.Error
                ));
            }
        }
        else
        {
            await liteDialogService.ShowAsync(new LiteDialogRequest(
                title: "Info",
                message: "Path is unavailable for this process.",
                buttons: LiteDialogButton.OK,
                image: LiteDialogImage.Information
            ));
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private async Task GracefulEndProcessAsync()
    {
        if (SelectedProcess == null) return;

        // Capture state to prevent race conditions
        int pid = SelectedProcess.Pid;
        string name = SelectedProcess.Name;

        if (await liteDialogService.ShowAsync(new LiteDialogRequest(
                title: "Graceful End",
                message: $"Send close request to '{name}' (PID: {pid})?",
                buttons: LiteDialogButton.YesNo,
                image: LiteDialogImage.Question
            )) != LiteDialogResult.Yes)
        {
            return;
        }

        await Task.Run(async () =>
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
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            if (await liteDialogService.ShowAsync(new LiteDialogRequest(
                                    title: "Process Unresponsive",
                                    message: $"Process '{name}' did not close within 3 seconds.\nForce kill it?",
                                    buttons: LiteDialogButton.YesNo,
                                    image: LiteDialogImage.Warning
                                )) == LiteDialogResult.Yes)
                            {
                                process.Kill();
                            }
                        });
                    }
                }
                else
                {
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        if (await liteDialogService.ShowAsync(new LiteDialogRequest(
                                title: "No Window Found",
                                message: $"Could not send close request (No Window or Unresponsive).\nForce kill '{name}'?",
                                buttons: LiteDialogButton.YesNo,
                                image: LiteDialogImage.Warning
                            )) == LiteDialogResult.Yes)
                        {
                            process.Kill();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Ignored");
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                    await liteDialogService.ShowAsync(new LiteDialogRequest(
                        title: "Error",
                        message: $"Error: {ex.Message}",
                        buttons: LiteDialogButton.OK,
                        image: LiteDialogImage.Error
                    )));
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

        if (await liteDialogService.ShowAsync(new LiteDialogRequest(
                title: "Graceful End Tree",
                message: $"Send close request to '{rootName}' (PID: {rootPid}) and all children?",
                buttons: LiteDialogButton.YesNo,
                image: LiteDialogImage.Question
            )) != LiteDialogResult.Yes)
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
        if (viewModelCache.TryGetValue(rootPid, out var rootVm))
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
                catch (Exception ex)
                {
                    /* Ignore access denied or already exited */
                    Log.Warning(ex, "Ignored");
                }
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
                catch (Exception ex)
                {
                    /* Assume exited if cannot access */
                    Log.Warning(ex, "Ignored");
                }
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
            if (await liteDialogService.ShowAsync(new LiteDialogRequest(
                    title: "Incomplete Shutdown",
                    message: $"{remaining.Count} processes in the tree are still running.\nForce kill the entire tree?",
                    buttons: LiteDialogButton.YesNo,
                    image: LiteDialogImage.Warning
                )) == LiteDialogResult.Yes)
            {
                try
                {
                    StopProcessAndChildren(rootPid);
                    await liteDialogService.ShowAsync(new LiteDialogRequest(
                        title: "Success",
                        message: "Tree force terminated.",
                        buttons: LiteDialogButton.OK,
                        image: LiteDialogImage.Success
                    ));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Ignored");
                    await liteDialogService.ShowAsync(new LiteDialogRequest(
                        title: "Error",
                        message: $"Error terminating tree: {ex.Message}",
                        buttons: LiteDialogButton.OK,
                        image: LiteDialogImage.Error
                    ));
                }
            }
        }
        else
        {
            await liteDialogService.ShowAsync(new LiteDialogRequest(
                title: "Success",
                message: "All processes in tree closed successfully.",
                buttons: LiteDialogButton.OK,
                image: LiteDialogImage.Success
            ));
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private async Task EndProcess()
    {
        if (SelectedProcess == null) return;
        if (await liteDialogService.ShowAsync(new LiteDialogRequest(
                title: "End Process",
                message: $"End process '{SelectedProcess.Name}' (PID: {SelectedProcess.Pid})?",
                buttons: LiteDialogButton.YesNo,
                image: LiteDialogImage.Warning
            )) == LiteDialogResult.Yes)
        {
            try
            {
                using var process = Process.GetProcessById(SelectedProcess.Pid);
                process.Kill();
                await liteDialogService.ShowAsync(new LiteDialogRequest(
                    title: "Success",
                    message: "Process terminated successfully.",
                    buttons: LiteDialogButton.OK,
                    image: LiteDialogImage.Success
                ));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Ignored");
                await liteDialogService.ShowAsync(new LiteDialogRequest(
                    title: "Error",
                    message: $"Failed to end process: {ex.Message}",
                    buttons: LiteDialogButton.OK,
                    image: LiteDialogImage.Error
                ));
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private async Task EndProcessTree()
    {
        if (SelectedProcess == null)
            return;

        if (await liteDialogService.ShowAsync(new LiteDialogRequest(
                title: "End Process Tree",
                message: $"Are you sure you want to end process tree for '{SelectedProcess.Name}' (PID: {SelectedProcess.Pid}) and all its children?",
                buttons: LiteDialogButton.YesNo,
                image: LiteDialogImage.Warning
            )) == LiteDialogResult.Yes)
        {
            try
            {
                StopProcessAndChildren(SelectedProcess.Pid);
                await liteDialogService.ShowAsync(new LiteDialogRequest(
                    title: "Success",
                    message: "Process tree terminated successfully.",
                    buttons: LiteDialogButton.OK,
                    image: LiteDialogImage.Success
                ));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Ignored");
                await liteDialogService.ShowAsync(new LiteDialogRequest(
                    title: "Error",
                    message: $"Failed to end process tree: {ex.Message}",
                    buttons: LiteDialogButton.OK,
                    image: LiteDialogImage.Error
                ));
            }
        }
    }

    private bool CanExecuteProcessAction() => SelectedProcess != null;

    private void StopProcessAndChildren(int pid)
    {
        if (!viewModelCache.TryGetValue(pid, out var vm))
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
        catch (Exception ex)
        {
            // Process may have already exited
            Log.Warning(ex, "Ignored");
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private async Task ShowProcessDetails()
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
        catch (Exception ex)
        {
            details.AppendLine("\n(Extended details unavailable - Access Denied or Process Exited)");
            Log.Warning(ex, "(Extended details unavailable - Access Denied or Process Exited)");
        }

        if (!string.IsNullOrWhiteSpace(SelectedProcess.Parameters))
        {
            details.AppendLine($"\nCommand Line:");
            details.AppendLine(SelectedProcess.Parameters);
        }

        await liteDialogService.ShowAsync(new LiteDialogRequest(
            title: "Process Details",
            message: details.ToString(),
            buttons: LiteDialogButton.OK,
            image: LiteDialogImage.Information
        ));
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private async Task OpenProcessDirectory()
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
                    await liteDialogService.ShowAsync(new LiteDialogRequest(
                        title: "Error",
                        message: "Process directory not found.",
                        buttons: LiteDialogButton.OK,
                        image: LiteDialogImage.Error
                    ));
                }
            }
            else
            {
                await liteDialogService.ShowAsync(new LiteDialogRequest(
                    title: "Error",
                    message: "Cannot access process file path.",
                    buttons: LiteDialogButton.OK,
                    image: LiteDialogImage.Error
                ));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Ignored");
            await liteDialogService.ShowAsync(new LiteDialogRequest(
                title: "Error",
                message: $"Failed to open process directory: {ex.Message}",
                buttons: LiteDialogButton.OK,
                image: LiteDialogImage.Error
            ));
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

    public void Dispose() => refreshTimer?.Stop();
}