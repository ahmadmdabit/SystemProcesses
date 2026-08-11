using System;
using System.Collections.Concurrent;
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
    private readonly RuntimeUnitExitor processExitor;
    private readonly DispatcherTimer refreshTimer;
    private readonly TelemetryService telemetryService;

    // Event for notifying StatsView of system statistics updates
    public event EventHandler? StatsUpdated;

    // Flag to prevent infinite loops during shutdown
    public bool IsExitConfirmed { get; private set; }

    // Cache ViewModels to preserve state (Expansion, Selection)
    // Key: PID
    // THREAD-SAFE: ConcurrentDictionary prevents race conditions between UI and background refresh threads
    private readonly ConcurrentDictionary<int, ProcessItemViewModel> viewModelCache = [];

    // Reusable buffer for CleanupStaleViewModels
    private readonly HashSet<int> activePidsBuffer = [];

    // Zero-Allocation Cache for strings "0" to "100"
    private static readonly BitmapSource[] cpuIconsCache = new BitmapSource[AppConstants.CpuIconCacheSize];

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

    // Current system statistics for StatsView binding
    public SystemStats SystemStats { get; private set; }

    [ObservableProperty] private string storageStatsText = string.Empty;
    [ObservableProperty] private string storageStatsTrayText = string.Empty;

    [ObservableProperty]
    private string trayToolTipTextHeader = "System Processes\nInitializing...";

    [ObservableProperty]
    private string trayToolTipTextBody = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseResumeText))]
    private bool isPaused;

    private int refreshInterval = AppConstants.DefaultRefreshIntervalMs;

    // Concurrency control
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    private bool isRefreshPending;

    public ObservableCollection<ProcessItemViewModel> Processes { get; } = [];
    public ObservableCollection<string> RefreshIntervals { get; }

    private bool isRefreshPopupOpen;

    /// <summary>
    /// Gets/sets whether the refresh interval popup is open.
    /// </summary>
    public bool IsRefreshPopupOpen
    {
        get => isRefreshPopupOpen;
        set => SetProperty(ref isRefreshPopupOpen, value);
    }

    [RelayCommand]
    private void ToggleRefreshPopup() => IsRefreshPopupOpen = !IsRefreshPopupOpen;

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
        this.processExitor = new RuntimeUnitExitor(liteDialogService, viewModelCache);

        // Initialize telemetry service (disabled by default, can be enabled via config)
        string diagnosticDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SystemProcesses", "Diagnostics");
        this.telemetryService = new TelemetryService(diagnosticDir, isEnabled: false);

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
        cpuIconsCache[0] = await imageLoaderService.LoadAsync("pack://application:,,,/Resources/Images/TrayIcon/SystemProcess-Tray.ico", AppConstants.IconDecodePixelWidth, AppConstants.IconDecodePixelHeight);
        for (int i = 1; i < 10; i++)
        {
            cpuIconsCache[i] = await imageLoaderService.LoadAsync($"pack://application:,,,/Resources/Images/TrayIcon/SystemProcess-Tray-0{i}.ico", AppConstants.IconDecodePixelWidth, AppConstants.IconDecodePixelHeight);
        }
        for (int i = 10; i < 100; i++)
        {
            cpuIconsCache[i] = await imageLoaderService.LoadAsync($"pack://application:,,,/Resources/Images/TrayIcon/SystemProcess-Tray-{i}.ico", AppConstants.IconDecodePixelWidth, AppConstants.IconDecodePixelHeight);
        }
        cpuIconsCache[100] = await imageLoaderService.LoadAsync("pack://application:,,,/Resources/Images/TrayIcon/SystemProcess-Tray-100.ico", AppConstants.IconDecodePixelWidth, AppConstants.IconDecodePixelHeight);
    }

    partial void OnSearchTextChanged(string value) => Task.Run(RefreshProcessesAsync);

    partial void OnIsPausedChanged(bool value)
    {
        if (isPaused) refreshTimer.Stop();
        else refreshTimer.Start();
    }

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    /// <summary>
    /// Refreshes the process tree and updates all UI elements.
    /// </summary>
    /// <remarks>
    /// This method implements concurrency control to prevent overlapping refresh cycles.
    /// If a refresh is already running, the request is marked as pending and executed
    /// immediately after the current refresh completes. This ensures rapid updates
    /// (like typing in search) are not dropped.
    /// </remarks>
    [RelayCommand]
    private async Task RefreshProcessesAsync()
    {
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

                // Record refresh cycle start for telemetry
                telemetryService.RecordRefreshCycleStart();

                var (rootInfos, stats) = await processService.GetProcessTreeAsync();
                var filteredRoots = ApplyFilters(rootInfos);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SyncProcessCollection(Processes, filteredRoots);
                    if (string.IsNullOrWhiteSpace(SearchText) && !IsTreeIsolated)
                    {
                        CleanupStaleViewModels(rootInfos);
                    }
                    UpdateStatistics(stats);
                    UpdateTrayState(stats);
                    StatsUpdated?.Invoke(this, EventArgs.Empty);
                });

                // Record refresh cycle end for telemetry
                telemetryService.RecordRefreshCycleEnd();

                // Update memory metrics periodically (every 10 cycles)
                if (telemetryService.GetMetricsSnapshot().RefreshCycleCount % 10 == 0)
                {
                    telemetryService.UpdateMemoryMetrics();
                }
            } while (isRefreshPending);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during process refresh: {Message}", ex.Message);
            telemetryService.RecordException(ex, "RefreshProcessesAsync");
        }
        finally
        {
            refreshLock.Release();
        }
    }

    /// <summary>
    /// Updates all statistics properties from the system stats snapshot.
    /// </summary>
    /// <param name="stats">The system statistics snapshot.</param>
    /// <remarks>
    /// This method updates process count, memory, CPU, and storage statistics.
    /// All calculations are zero-allocation (no LINQ, no string allocations).
    /// </remarks>
    private void UpdateStatistics(SystemStats stats)
    {
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

        // Store SystemStats for StatsView
        SystemStats = stats;

        // Update Storage Stats
        UpdateStorageStats(stats);
    }

    /// <summary>
    /// Applies search and isolation filters to the process tree.
    /// </summary>
    /// <param name="roots">The root processes to filter.</param>
    /// <returns>The filtered process tree, or empty list if isolation target not found.</returns>
    /// <remarks>
    /// If tree isolation is active, returns only the isolated process and its children.
    /// Otherwise, applies search text filter to process names and PIDs.
    /// </remarks>
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

    /// <summary>
    /// Finds a process in the process tree by PID.
    /// </summary>
    /// <param name="nodes">The nodes to search.</param>
    /// <param name="pid">The process ID to find.</param>
    /// <returns>The ProcessInfo if found, null otherwise.</returns>
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

    /// <summary>
    /// Filters the process tree by search text.
    /// </summary>
    /// <param name="nodes">The nodes to filter.</param>
    /// <param name="text">The search text.</param>
    /// <returns>A filtered process tree containing only matching processes and their ancestors.</returns>
    /// <remarks>
    /// Clones matching processes to attach filtered children, preserving the original tree structure.
    /// </remarks>
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
                    ProcessPath = node.ProcessPath,
                    CreateTime = node.CreateTime
                };
                // ProcessInfo.Children is a get-only List, so we use AddRange
                clone.Children.AddRange(filteredChildren);
                result.Add(clone);
            }
        }
        return result;
    }

    /// <summary>
    /// Synchronizes the UI collection with the source process data using differential updates.
    /// </summary>
    /// <param name="collection">The ObservableCollection to update.</param>
    /// <param name="sourceInfos">The source process data.</param>
    /// <param name="depth">The tree depth (for hierarchy tracking).</param>
    /// <remarks>
    /// <para>
    /// This method implements zero-allocation differential updates:
    /// - Removes processes that no longer exist
    /// - Adds new processes
    /// - Reorders existing processes
    /// - Updates process data in-place
    /// - Preserves UI state (expansion, selection, scroll position)
    /// </para>
    /// <para>Uses method-local HashSet per call frame to prevent cross-depth corruption.</para>
    /// </remarks>
    private void SyncProcessCollection(ObservableCollection<ProcessItemViewModel> collection, List<ProcessInfo> sourceInfos, int depth = 0)
    {
        // Method-local set: each recursive call frame gets its own set, preventing
        // corruption of parent validation when child recursion clears a shared set.
        var sourcePidSet = new HashSet<int>(sourceInfos.Count);
        for (int i = 0; i < sourceInfos.Count; i++)
        {
            sourcePidSet.Add(sourceInfos[i].Pid);
        }

        // 1. Remove items no longer in sourceInfos at this level
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (!sourcePidSet.Contains(collection[i].Pid))
            {
                collection.RemoveAt(i);
            }
        }

        // 2. Insert, reorder, or replace items
        for (int i = 0; i < sourceInfos.Count; i++)
        {
            var info = sourceInfos[i];
            ProcessItemViewModel? vm;

            // Retrieve or instantiate ViewModel, validating CreateTime against PID reuse.
            // If the PID was recycled by a different process (different CreateTime),
            // discard the old ViewModel and create a fresh one.
            if (!viewModelCache.TryGetValue(info.Pid, out vm) || vm.ProcessInfo.CreateTime != info.CreateTime)
            {
                vm = new ProcessItemViewModel(info);
                viewModelCache[info.Pid] = vm;
            }

            if (i < collection.Count)
            {
                if (collection[i].Pid == info.Pid)
                {
                    if (collection[i] != vm)
                    {
                        collection[i] = vm;
                    }
                }
                else
                {
                    int existingIdx = -1;
                    for (int k = i + 1; k < collection.Count; k++)
                    {
                        if (collection[k].Pid == info.Pid)
                        {
                            existingIdx = k;
                            break;
                        }
                    }

                    if (existingIdx != -1)
                    {
                        collection.Move(existingIdx, i);
                        if (collection[i] != vm)
                        {
                            collection[i] = vm;
                        }
                    }
                    else
                    {
                        collection.Insert(i, vm);
                    }
                }
            }
            else
            {
                collection.Insert(i, vm);
            }

            vm.Depth = depth;
            vm.Refresh();
            SyncProcessCollection(vm.Children, info.Children, depth + 1);
        }

        // 3. Trim any trailing excess items (collection longer than source)
        while (collection.Count > sourceInfos.Count)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }

    /// <summary>
    /// Removes ViewModels from the cache that are no longer present in the process tree.
    /// Called after a full synchronization to clean up stale entries from exited processes.
    /// Only runs when search/isolation is inactive to avoid evicting ViewModels that are
    /// temporarily hidden by filtering.
    /// </summary>
    private void CleanupStaleViewModels(List<ProcessInfo> roots)
    {
        activePidsBuffer.Clear();
        CollectAllPids(roots, activePidsBuffer);

        foreach (var kvp in viewModelCache)
        {
            if (!activePidsBuffer.Contains(kvp.Key))
            {
                viewModelCache.TryRemove(kvp.Key, out _);
            }
        }
    }

    private static void CollectAllPids(IEnumerable<ProcessInfo> nodes, HashSet<int> pids)
    {
        foreach (var node in nodes)
        {
            pids.Add(node.Pid);
            if (node.Children.Count > 0)
            {
                CollectAllPids(node.Children, pids);
            }
        }
    }

    /// <summary>
    /// Updates storage statistics display text.
    /// </summary>
    /// <param name="stats">The system statistics snapshot.</param>
    /// <remarks>
    /// Updates both the main UI storage stats text and the tray tooltip storage text.
    /// Uses StringBuilderPool for zero-allocation string formatting.
    /// </remarks>
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

    /// <summary>
    /// Updates the system tray icon and tooltip.
    /// </summary>
    /// <param name="stats">The system statistics snapshot.</param>
    /// <remarks>
    /// Updates the tray icon based on CPU usage (0-100 icons pre-loaded).
    /// Updates the tooltip with CPU, RAM, VM, and disk usage percentages.
    /// Uses StringBuilderPool for zero-allocation string formatting.
    /// </remarks>
    private void UpdateTrayState(SystemStats stats)
    {
        // ...... PART 1: Update Icon (CPU Number) ......

        // Clamp value to 0-100 to ensure we never go out of bounds of our cache
        // Cast to int is safe because we only need whole numbers for the icon
        int cpuInt = (int)Math.Clamp(stats.TotalCpu, 0, AppConstants.CpuPercentageMaxClamp);

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
                Log.Warning(ex, "Failed to copy process path to clipboard for PID {Pid}", SelectedProcess?.Pid ?? -1);
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

    /// <summary>
    /// Sends a graceful close request to the selected process.
    /// </summary>
    /// <remarks>
    /// Delegates to ProcessExitor service for graceful exition.
    /// Attempts to close the process window gracefully. If the process doesn't respond
    /// within 3 seconds, prompts the user to force stop it.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private async Task GracefulEndProcessAsync()
    {
        if (SelectedProcess == null) return;
        await processExitor.GracefullyExitAsync(SelectedProcess.Pid, SelectedProcess.Name);
    }

    /// <summary>
    /// Sends a graceful close request to the selected process and all its children.
    /// </summary>
    /// <remarks>
    /// Delegates to ProcessExitor service for graceful tree exition.
    /// Attempts to close the entire process tree gracefully. Waits up to 3 seconds per attempt
    /// for processes to exit. If any processes remain after 3 attempts, prompts the user to
    /// force stop the entire tree.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private async Task GracefulEndProcessTreeAsync()
    {
        if (SelectedProcess == null) return;
        await processExitor.GracefullyExitTreeAsync(SelectedProcess.Pid, SelectedProcess.Name);
    }

    /// <summary>
    /// Force exits the selected process immediately.
    /// </summary>
    /// <remarks>
    /// Delegates to ProcessExitor service for force exition.
    /// Sends a SIGKILL signal to the process, exiting it immediately without
    /// allowing cleanup. Use GracefulEndProcessAsync for graceful shutdown.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private async Task EndProcess()
    {
        if (SelectedProcess == null) return;
        await processExitor.ForceExitAsync(SelectedProcess.Pid, SelectedProcess.Name);
    }

    /// <summary>
    /// Force exits the selected process and all its children.
    /// </summary>
    /// <remarks>
    /// Delegates to ProcessExitor service for force tree exition.
    /// Recursively exits the entire process tree. Children are exitd first
    /// (bottom-up approach) to avoid orphaned processes.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanExecuteProcessAction))]
    private async Task EndProcessTree()
    {
        if (SelectedProcess == null) return;
        await processExitor.ForceExitTreeAsync(SelectedProcess.Pid, SelectedProcess.Name);
    }

    /// <summary>
    /// Determines whether process action commands can execute.
    /// </summary>
    /// <returns>True if a process is selected, false otherwise.</returns>
    private bool CanExecuteProcessAction() => SelectedProcess != null;

    /// <summary>
    /// Shows detailed information about the selected process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Displays process name, PID, CPU usage, memory, virtual memory, service status,
    /// start time, processor time, thread count, handle count, and command line.
    /// </para>
    /// <para>
    /// Extended details (start time, threads, handles, file path) are retrieved from
    /// the live process and may fail with access denied or process exited errors.
    /// </para>
    /// </remarks>
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
            Log.Warning(ex, "Failed to retrieve extended process details for PID {Pid}", SelectedProcess.Pid);
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

    /// <summary>
    /// Opens the directory containing the selected process executable.
    /// </summary>
    /// <remarks>
    /// <para>Retrieves the process executable path and opens its directory in Windows Explorer.</para>
    /// <para>
    /// Error handling:
    /// - Access denied: Logged as warning, error shown to user
    /// - Process exited: Logged as warning, error shown to user
    /// - Directory not found: Error shown to user
    /// </para>
    /// </remarks>
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
            Log.Warning(ex, "Failed to open process directory for PID {Pid}", SelectedProcess.Pid);
            await liteDialogService.ShowAsync(new LiteDialogRequest(
                title: "Error",
                message: $"Failed to open process directory: {ex.Message}",
                buttons: LiteDialogButton.OK,
                image: LiteDialogImage.Error
            ));
        }
    }

    /// <summary>
    /// Formats a byte count into a human-readable string (B, KB, MB, GB, TB).
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <returns>A formatted string with appropriate unit.</returns>
    /// <remarks>
    /// Uses zero-allocation approach with constants and conditional logic.
    /// No LINQ, no string interpolation in hot path.
    /// </remarks>
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

    /// <summary>
    /// Cleans up resources used by the ViewModel.
    /// </summary>
    public void Dispose()
    {
        refreshTimer?.Stop();
        telemetryService?.Dispose();
    }
}
