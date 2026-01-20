# Code Examples & Patterns

This document provides practical code examples demonstrating key patterns and techniques used in the SystemProcesses project.

---

## Table of Contents

1. [Zero-Allocation Patterns](#zero-allocation-patterns)
2. [Object Pooling](#object-pooling)
3. [MVVM Implementation](#mvvm-implementation)
4. [P/Invoke & Native API Usage](#pinvoke--native-api-usage)
5. [Unsafe Code Patterns](#unsafe-code-patterns)
6. [WPF-Specific Patterns](#wpf-specific-patterns)
7. [Threading & Async Patterns](#threading--async-patterns)
8. [Performance Optimization Examples](#performance-optimization-examples)

---

## Zero-Allocation Patterns

### Pattern 1: Reusable Collections

**Bad** - Creates new collection every time:
```csharp
public List<ProcessInfo> GetActiveProcesses()
{
    var result = new List<ProcessInfo>(); // Allocation
    
    foreach (var process in allProcesses)
    {
        if (process.IsActive)
            result.Add(process);
    }
    
    return result;
}
```

**Good** - Reuses collection instance:
```csharp
private readonly List<ProcessInfo> reusableList = new(256);

public List<ProcessInfo> GetActiveProcesses()
{
    reusableList.Clear(); // O(1), retains capacity
    
    foreach (var process in allProcesses)
    {
        if (process.IsActive)
            reusableList.Add(process);
    }
    
    return reusableList;
}
```

---

### Pattern 2: Stack Allocation for Temporary Buffers

**Bad** - Heap allocation:
```csharp
public string FormatDrivePath(char driveLetter)
{
    char[] buffer = new char[3]; // Heap allocation
    buffer[0] = driveLetter;
    buffer[1] = ':';
    buffer[2] = '\\';
    return new string(buffer);
}
```

**Good** - Stack allocation:
```csharp
public string FormatDrivePath(char driveLetter)
{
    Span<char> buffer = stackalloc char[3]; // Stack, zero heap allocation
    buffer[0] = driveLetter;
    buffer[1] = ':';
    buffer[2] = '\\';
    return new string(buffer);
}
```

---

### Pattern 3: In-Place Object Updates

**Bad** - Creates new objects:
```csharp
public void UpdateProcessList(List<ProcessInfo> newData)
{
    processes.Clear();
    
    foreach (var process in newData)
    {
        processes.Add(new ProcessInfo(process)); // Allocation
    }
}
```

**Good** - Updates existing objects:
```csharp
private readonly Dictionary<int, ProcessInfo> processCache = new();

public void UpdateProcessList(List<ProcessInfo> newData)
{
    foreach (var newProcess in newData)
    {
        if (processCache.TryGetValue(newProcess.Pid, out var existing))
        {
            // Update in-place
            existing.CpuUsage = newProcess.CpuUsage;
            existing.Memory = newProcess.Memory;
        }
        else
        {
            // Only allocate for NEW processes
            processCache[newProcess.Pid] = newProcess;
        }
    }
}
```

---

### Pattern 4: Cached Static Strings

**Bad** - Allocates on every access:
```csharp
public string GetCpuText(int cpuPercent)
{
    return $"{cpuPercent}%"; // New string every call
}
```

**Good** - Pre-computed cache:
```csharp
private static readonly string[] cpuTextCache = new string[101];

static ProcessInfo()
{
    for (int i = 0; i <= 100; i++)
    {
        cpuTextCache[i] = $"{i}%";
    }
}

public string GetCpuText(int cpuPercent)
{
    return cpuPercent <= 100 ? cpuTextCache[cpuPercent] : $"{cpuPercent}%";
}
```

---

## Object Pooling

### Example 1: Using StringBuilderPool

**Basic Usage**:
```csharp
using SystemProcesses.Desktop.Helpers;

public string FormatProcessInfo(ProcessInfo process)
{
    using (var psb = StringBuilderPool.Rent())
    {
        psb.Builder.Append("PID: ");
        psb.Builder.Append(process.Pid);
        psb.Builder.Append(", Name: ");
        psb.Builder.Append(process.Name);
        psb.Builder.Append(", CPU: ");
        psb.Builder.Append(process.CpuUsage.ToString("F2"));
        psb.Builder.Append('%');
        
        return psb.Build();
    } // Automatically returned to pool
}
```

**With Initial Text**:
```csharp
public string BuildStatusMessage(string prefix, int count)
{
    using (var psb = StringBuilderPool.Rent(prefix))
    {
        psb.Builder.Append(": ");
        psb.Builder.Append(count);
        psb.Builder.Append(" items");
        return psb.Build();
    }
}
```

**Common Mistake** - Don't do this:
```csharp
public string BadExample()
{
    var psb = StringBuilderPool.Rent();
    psb.Builder.Append("Text");
    string result = psb.Build();
    psb.Dispose(); // ❌ Called AFTER Build()
    return result;
}

// CORRECT:
public string GoodExample()
{
    using (var psb = StringBuilderPool.Rent())
    {
        psb.Builder.Append("Text");
        return psb.Build(); // ✅ Dispose happens AFTER return
    }
}
```

---

### Example 2: Implementing Custom Object Pool

```csharp
using Microsoft.Extensions.ObjectPool;

// 1. Define pooling policy
public class ProcessInfoPoolPolicy : IPooledObjectPolicy<ProcessInfo>
{
    public ProcessInfo Create()
    {
        return new ProcessInfo();
    }
    
    public bool Return(ProcessInfo obj)
    {
        // Reset object state
        obj.Pid = 0;
        obj.Name = string.Empty;
        obj.CpuUsage = 0;
        obj.Children.Clear();
        
        // Return true to keep in pool, false to discard
        return true;
    }
}

// 2. Create pool instance
private static readonly ObjectPool<ProcessInfo> processPool;

static ProcessService()
{
    var policy = new ProcessInfoPoolPolicy();
    processPool = new DefaultObjectPool<ProcessInfo>(policy, maxRetained: 100);
}

// 3. Use pool
public ProcessInfo RentProcessInfo()
{
    var process = processPool.Get();
    return process;
}

public void ReturnProcessInfo(ProcessInfo process)
{
    processPool.Return(process);
}
```

---

## MVVM Implementation

### Example 1: ViewModel with Source Generators

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public partial class MainViewModel : ObservableObject
{
    // ===== PROPERTIES =====
    
    // Simple property
    [ObservableProperty]
    private string searchText = string.Empty;
    
    // Property that notifies another property
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProcesses))]
    private int processCount;
    
    // Computed property
    public bool HasProcesses => ProcessCount > 0;
    
    // Property that triggers command reevaluation
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool isRefreshing;
    
    // ===== COMMANDS =====
    
    // Simple synchronous command
    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }
    
    // Async command with CanExecute
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            await processService.RefreshAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }
    
    private bool CanRefresh() => !IsRefreshing;
    
    // Command with parameter
    [RelayCommand]
    private void SelectProcess(ProcessItemViewModel? process)
    {
        if (process != null)
        {
            SelectedProcess = process;
        }
    }
}
```

---

### Example 2: Manual Property Implementation (When Source Generator Can't Be Used)

```csharp
public partial class MainViewModel : ObservableObject
{
    // When you need custom logic in setter
    private bool isTreeIsolated;
    
    public bool IsTreeIsolated
    {
        get => isTreeIsolated;
        set
        {
            if (isTreeIsolated == value) return;
            
            if (value)
            {
                // Custom validation logic
                if (SelectedProcess == null)
                {
                    OnPropertyChanged(); // Revert UI
                    return;
                }
                
                isolationTargetPid = SelectedProcess.Pid;
            }
            
            isTreeIsolated = value;
            OnPropertyChanged();
            
            // Trigger dependent logic
            ApplyTreeFilter();
        }
    }
}
```

---

### Example 3: Dependency Injection in ViewModel

```csharp
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IProcessService processService;
    private readonly IImageLoaderService imageLoader;
    private readonly ILiteDialogService dialogService;
    
    // Constructor injection
    public MainViewModel(
        IProcessService processService,
        IImageLoaderService imageLoader,
        ILiteDialogService dialogService)
    {
        this.processService = processService ?? throw new ArgumentNullException(nameof(processService));
        this.imageLoader = imageLoader ?? throw new ArgumentNullException(nameof(imageLoader));
        this.dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        
        InitializeCommands();
        StartRefreshTimer();
    }
    
    public void Dispose()
    {
        processService?.Dispose();
        imageLoader?.ClearCache();
    }
}
```

---

## P/Invoke & Native API Usage

### Example 1: Basic P/Invoke with LibraryImport

```csharp
using System.Runtime.InteropServices;

internal static partial class SystemPrimitives
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial IntPtr OpenProcess(
        int dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int dwProcessId);
    
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr hObject);
    
    // Constants
    private const int ProcessQueryLimitedInformation = 0x1000;
}

// Usage
public static string? GetProcessPath(int pid)
{
    IntPtr handle = SystemPrimitives.OpenProcess(
        ProcessQueryLimitedInformation, 
        false, 
        pid);
    
    if (handle == IntPtr.Zero)
    {
        int error = Marshal.GetLastWin32Error();
        Log.Warning("Failed to open process {Pid}, error {Error}", pid, error);
        return null;
    }
    
    try
    {
        // Use handle...
        return GetProcessPathFromHandle(handle);
    }
    finally
    {
        SystemPrimitives.CloseHandle(handle);
    }
}
```

---

### Example 2: Safe Handle Pattern

```csharp
using Microsoft.Win32.SafeHandles;

public sealed class SafeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeProcessHandle() : base(ownsHandle: true)
    {
    }
    
    protected override bool ReleaseHandle()
    {
        return CloseHandle(handle);
    }
    
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}

// Usage
public static string? GetProcessPathSafe(int pid)
{
    using var handle = OpenProcessSafe(pid);
    
    if (handle.IsInvalid)
    {
        return null;
    }
    
    return GetProcessPathFromHandle(handle.DangerousGetHandle());
}
```

---

### Example 3: Marshalling Structures

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct MEMORYSTATUSEX
{
    public uint dwLength;
    public uint dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;
}

[LibraryImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

// Usage
public static (long TotalRam, long AvailableRam) GetMemoryInfo()
{
    var memStatus = new MEMORYSTATUSEX
    {
        dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
    };
    
    if (!GlobalMemoryStatusEx(ref memStatus))
    {
        int error = Marshal.GetLastWin32Error();
        throw new Win32Exception(error);
    }
    
    return ((long)memStatus.ullTotalPhys, (long)memStatus.ullAvailPhys);
}
```

---

## Unsafe Code Patterns

### Example 1: Pointer Iteration Over Native Buffer

```csharp
public unsafe void ParseProcessData(IntPtr buffer, int bufferSize)
{
    byte* current = (byte*)buffer;
    byte* end = current + bufferSize;
    
    while (current < end)
    {
        // Safety check before reading
        if (current + sizeof(uint) > end)
        {
            Log.Warning("Buffer overrun prevented");
            break;
        }
        
        // Read NextEntryOffset
        uint nextOffset = *(uint*)current;
        
        // Read PID (offset 72 on x64)
        if (current + 76 > end) break;
        int pid = *(int*)(current + 72);
        
        // Read Parent PID (offset 80 on x64)
        if (current + 84 > end) break;
        int parentPid = *(int*)(current + 80);
        
        // Process the data...
        ProcessEntry(pid, parentPid);
        
        // Move to next entry
        if (nextOffset == 0) break; // Last entry
        current += nextOffset;
    }
}
```

---

### Example 2: Safe UnicodeString Marshalling

```csharp
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UnicodeString
{
    public ushort Length;        // Bytes, not characters!
    public ushort MaximumLength;
    public char* Buffer;
}

public static unsafe string ConvertUnicodeString(UnicodeString* str)
{
    // Safety checks
    if (str == null || str->Buffer == null)
        return string.Empty;
    
    if (str->Length == 0)
        return string.Empty;
    
    // Length is in bytes, convert to character count
    int charCount = str->Length / sizeof(char);
    
    // Validate we don't exceed maximum
    if (charCount > str->MaximumLength / sizeof(char))
        charCount = str->MaximumLength / sizeof(char);
    
    // Create string from pointer
    return new string(str->Buffer, 0, charCount);
}
```

---

### Example 3: Span<T> for Safe Pointer Operations

```csharp
public unsafe void ProcessBytesWithSpan(IntPtr buffer, int length)
{
    // Wrap pointer in Span for bounds checking
    Span<byte> data = new Span<byte>((void*)buffer, length);
    
    // Safe iteration with bounds checking
    for (int i = 0; i < data.Length; i++)
    {
        byte value = data[i]; // Bounds-checked
        ProcessByte(value);
    }
    
    // Or use slicing
    Span<byte> header = data.Slice(0, 64);
    Span<byte> body = data.Slice(64);
}
```

---

## WPF-Specific Patterns

### Example 1: Freezing Images for Thread-Safety

```csharp
public async Task<BitmapSource?> LoadImageAsync(string path)
{
    return await Task.Run(() =>
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load immediately
            bitmap.EndInit();
            
            // CRITICAL: Freeze for cross-thread use
            bitmap.Freeze();
            
            return (BitmapSource)bitmap;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load image from {Path}", path);
            return null;
        }
    });
}
```

---

### Example 2: Dispatcher Invocation

```csharp
public class MainViewModel : ObservableObject
{
    private readonly Dispatcher dispatcher;
    
    public MainViewModel()
    {
        dispatcher = Application.Current.Dispatcher;
    }
    
    // Safe UI update from background thread
    public void UpdateStatusFromBackgroundThread(string message)
    {
        if (dispatcher.CheckAccess())
        {
            // Already on UI thread
            StatusText = message;
        }
        else
        {
            // Marshal to UI thread
            dispatcher.Invoke(() => StatusText = message);
        }
    }
    
    // Async version
    public async Task UpdateStatusAsync(string message)
    {
        await dispatcher.InvokeAsync(() => StatusText = message);
    }
}
```

---

### Example 3: ObservableCollection Differential Update

```csharp
public void SyncProcessCollection(List<ProcessInfo> newData)
{
    // Build HashSet of new PIDs for O(1) lookup
    var newPids = new HashSet<int>(newData.Select(p => p.Pid));
    
    // Pass 1: Remove items no longer present
    for (int i = Processes.Count - 1; i >= 0; i--)
    {
        if (!newPids.Contains(Processes[i].Pid))
        {
            Processes.RemoveAt(i);
        }
    }
    
    // Pass 2: Update existing and add new
    var existingPids = new HashSet<int>(Processes.Select(p => p.Pid));
    
    foreach (var newProcess in newData)
    {
        var existing = Processes.FirstOrDefault(p => p.Pid == newProcess.Pid);
        
        if (existing != null)
        {
            // Update in-place
            existing.CpuUsage = newProcess.CpuUsage;
            existing.Memory = newProcess.Memory;
        }
        else
        {
            // Add new
            Processes.Add(new ProcessItemViewModel(newProcess));
        }
    }
}
```

---

### Example 4: Win32 Message-Driven Always-On-Top Window

**Pattern**: Use Windows message interception for event-driven z-order enforcement without polling.

**Implementation Overview**:
1. **Hook WndProc**: Register message handler via `HwndSource.AddHook()`
2. **Intercept WmWindowPosChanging**: Modify `WINDOWPOS` structure before z-order change occurs
3. **Handle WmActivateApp**: Re-assert topmost on application activation events
4. **Validate Handles**: Use `IsWindow()` before Win32 calls to prevent teardown failures

**Key Benefits**:
- Zero-allocation (uses `IntPtr` directly, no boxing)
- Event-driven (no periodic polling overhead)
- Proactive enforcement (prevents z-order changes before they occur)
- ~0.016% CPU overhead with message-driven approach

**Reference**: See `Views/StatsView.xaml.cs` for complete implementation using `WmWindowPosChanging` and `WmActivateApp` messages.

---

### Example 5: Multi-Monitor Dialog Positioning

**Pattern**: Use native Win32 APIs to position dialogs on the correct monitor in multi-monitor setups.

**Implementation**:
```csharp
private void EnsureOnScreen(Rect ownerBounds)
{
    // Find the monitor that contains the owner window center point
    var ownerCenter = new SystemPrimitives.Point
    {
        x = (int)(ownerBounds.Left + ownerBounds.Width / 2),
        y = (int)(ownerBounds.Top + ownerBounds.Height / 2)
    };

    // Get monitor handle for the point where owner window is located
    IntPtr hMonitor = SystemPrimitives.MonitorFromPoint(
        ownerCenter, 
        SystemPrimitives.MonitorDefaultToNearest);

    if (hMonitor != IntPtr.Zero)
    {
        // Get monitor info including work area (excludes taskbar)
        var monitorInfo = new SystemPrimitives.MonitorInfo
        {
            cbSize = (uint)Marshal.SizeOf<SystemPrimitives.MonitorInfo>()
        };

        if (SystemPrimitives.GetMonitorInfoW(hMonitor, ref monitorInfo))
        {
            var workArea = monitorInfo.rcWork;

            // Adjust if off-screen horizontally
            if (this.Left < workArea.left)
                this.Left = workArea.left;
            else if (this.Left + this.ActualWidth > workArea.right)
                this.Left = workArea.right - this.ActualWidth;

            // Adjust if off-screen vertically
            if (this.Top < workArea.top)
                this.Top = workArea.top;
            else if (this.Top + this.ActualHeight > workArea.bottom)
                this.Top = workArea.bottom - this.ActualHeight;
        }
    }
}
```

**Key Points**:
- Uses owner window center point to determine target monitor
- Applies work area bounds (excludes taskbar) for positioning
- Falls back to nearest monitor if point is off-screen
- Zero dependencies (uses native Win32 APIs only)

**Benefits**:
- Dialogs appear on correct monitor in multi-monitor setups
- Taskbar-aware positioning prevents overlap
- No System.Windows.Forms dependency
- <1ms overhead per dialog show

**Reference**: See `Helpers/LiteDialog.cs` for complete implementation.

---

## Threading & Async Patterns

### Example 1: Producer-Consumer with SemaphoreSlim

```csharp
public partial class MainViewModel : ObservableObject
{
    private readonly SemaphoreSlim refreshSemaphore = new(1, 1);
    private readonly IProcessService processService;
    
    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Try to acquire, skip if already running
        if (!await refreshSemaphore.WaitAsync(0))
        {
            Log.Debug("Refresh already in progress, skipping");
            return;
        }
        
        try
        {
            IsRefreshing = true;
            
            // Execute heavy work off UI thread
            var (roots, stats) = await Task.Run(() => 
                processService.GetProcessTreeAsync());
            
            // Back on UI thread for binding updates
            UpdateProcessTree(roots);
            UpdateSystemStats(stats);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Refresh failed");
        }
        finally
        {
            IsRefreshing = false;
            refreshSemaphore.Release();
        }
    }
}
```

---

### Example 2: Timer-Based Background Work

```csharp
public class MainViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer refreshTimer;
    private CancellationTokenSource? cancellationTokenSource;
    
    public MainViewModel()
    {
        refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        refreshTimer.Tick += OnRefreshTimerTick;
    }
    
    public void StartMonitoring()
    {
        cancellationTokenSource = new CancellationTokenSource();
        refreshTimer.Start();
    }
    
    public void StopMonitoring()
    {
        refreshTimer.Stop();
        cancellationTokenSource?.Cancel();
    }
    
    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshAsync();
    }
    
    public void Dispose()
    {
        refreshTimer?.Stop();
        refreshTimer.Tick -= OnRefreshTimerTick;
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }
}
```

---

### Example 3: Async Resource Loading with Caching

```csharp
public class ImageLoaderService : IImageLoaderService
{
    private readonly ConcurrentDictionary<string, BitmapSource> cache = new();
    private readonly SemaphoreSlim loadSemaphore = new(1, 1);
    
    public async Task<BitmapSource?> LoadImageAsync(string path)
    {
        // Check cache first
        if (cache.TryGetValue(path, out var cached))
        {
            return cached;
        }
        
        // Prevent duplicate loads
        await loadSemaphore.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (cache.TryGetValue(path, out cached))
            {
                return cached;
            }
            
            // Load image off UI thread
            var image = await Task.Run(() => LoadImageFromDisk(path));
            
            if (image != null)
            {
                cache[path] = image;
            }
            
            return image;
        }
        finally
        {
            loadSemaphore.Release();
        }
    }
}
```

---

## Performance Optimization Examples

### Example 1: Avoiding LINQ in Hot Paths

**Before** (allocates):
```csharp
public void FindTop5Processes()
{
    var top5 = processes
        .Where(p => p.CpuUsage > 0)
        .OrderByDescending(p => p.CpuUsage)
        .Take(5)
        .ToList();
}
```

**After** (zero allocation):
```csharp
private readonly ProcessInfo?[] top5Buffer = new ProcessInfo?[5];

public void FindTop5Processes()
{
    // Clear previous
    Array.Clear(top5Buffer, 0, 5);
    
    // Insertion sort into fixed buffer
    foreach (var process in processes)
    {
        if (process.CpuUsage <= 0) continue;
        
        for (int i = 0; i < 5; i++)
        {
            if (top5Buffer[i] == null || 
                process.CpuUsage > top5Buffer[i]!.CpuUsage)
            {
                // Shift right
                for (int j = 4; j > i; j--)
                {
                    top5Buffer[j] = top5Buffer[j - 1];
                }
                
                top5Buffer[i] = process;
                break;
            }
        }
    }
}
```

---

### Example 2: Batch Property Change Notifications

**Before** (multiple notifications):
```csharp
public void UpdateStats(SystemStats stats)
{
    CpuUsage = stats.CpuUsagePercent;      // Notification 1
    MemoryUsage = stats.MemoryUsagePercent; // Notification 2
    DiskUsage = stats.DiskActivePercent;    // Notification 3
}
```

**After** (single batch update):
```csharp
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(StatusSummary))]
private SystemStats currentStats;

public void UpdateStats(SystemStats stats)
{
    CurrentStats = stats; // Single notification
}

public string StatusSummary => 
    $"CPU: {CurrentStats.CpuUsagePercent:F1}% | " +
    $"RAM: {CurrentStats.MemoryUsagePercent}% | " +
    $"Disk: {CurrentStats.DiskActivePercent:F1}%";
```

---

### Example 3: Collection Pre-Sizing

**Before**:
```csharp
var processes = new Dictionary<int, ProcessInfo>(); // Capacity 0
// Will resize multiple times as items are added
```

**After**:
```csharp
// Pre-size based on expected count
var processes = new Dictionary<int, ProcessInfo>(capacity: 512);
var list = new List<ProcessInfo>(capacity: 64);
```

---

## Validation & Error Handling Examples

### Example 1: Buffer Validation

**Pattern**: Always validate buffers before unsafe operations.

```csharp
private const int MaxBufferSize = 100 * 1024 * 1024; // 100 MB

private unsafe void ProcessBuffer(IntPtr buffer, int size)
{
    // Validate buffer
    if (buffer == IntPtr.Zero)
    {
        Log.Warning("Buffer is null");
        return;
    }
    
    if (size <= 0 || size > MaxBufferSize)
    {
        Log.Warning("Invalid buffer size: {Size}", size);
        return;
    }
    
    // Safe to use buffer
    byte* ptr = (byte*)buffer;
    for (int i = 0; i < size; i++)
    {
        // Process byte
    }
}
```

---

### Example 2: Pointer Arithmetic Bounds Checking

**Pattern**: Validate pointer arithmetic to prevent buffer overflows.

```csharp
private unsafe void ParseStructures(IntPtr buffer, int bufferSize)
{
    long offset = 0;
    
    while (offset < bufferSize)
    {
        // Validate we have enough space for structure header
        if (offset + sizeof(StructureHeader) > bufferSize)
        {
            Log.Warning("Insufficient buffer space at offset {Offset}", offset);
            break;
        }
        
        var header = (StructureHeader*)((byte*)buffer + offset);
        
        // Validate next offset
        if (header->NextOffset < 0 || header->NextOffset > bufferSize - offset)
        {
            Log.Warning("Invalid NextOffset {Offset} at position {Position}", 
                header->NextOffset, offset);
            break;
        }
        
        // Process structure
        ProcessStructure(header);
        
        if (header->NextOffset == 0) break;
        offset += header->NextOffset;
    }
}
```

---

### Example 3: String Encoding Validation

**Pattern**: Validate string encoding before marshalling.

```csharp
private unsafe string? ExtractUnicodeString(UnicodeString* stringPtr)
{
    // Null pointer check
    if (stringPtr == null || stringPtr->Buffer == IntPtr.Zero)
    {
        return null;
    }
    
    // UTF-16 length must be even (2 bytes per character)
    if (stringPtr->Length % 2 != 0)
    {
        Log.Warning("Invalid UTF-16 string length: {Length}", stringPtr->Length);
        return null;
    }
    
    // Maximum reasonable string length (1 MB)
    if (stringPtr->Length > 1024 * 1024)
    {
        Log.Warning("String length exceeds maximum: {Length}", stringPtr->Length);
        return null;
    }
    
    try
    {
        // Safe to marshal
        return Marshal.PtrToStringUni(stringPtr->Buffer, stringPtr->Length / 2);
    }
    catch (ArgumentOutOfRangeException ex)
    {
        Log.Warning(ex, "String marshalling failed");
        return null;
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Unexpected error during string extraction");
        return null;
    }
}
```

---

### Example 4: P/Invoke Handle Validation

**Pattern**: Always validate handles before use and ensure cleanup.

```csharp
private string? GetProcessCommandLine(int pid)
{
    IntPtr handle = IntPtr.Zero;
    
    try
    {
        // Open process
        handle = OpenProcess(ProcessAccessFlags.QueryLimited, false, pid);
        
        // Validate handle
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            int error = Marshal.GetLastWin32Error();
            Log.Warning("Failed to open process {Pid}, error {Error:X8}", pid, error);
            return null;
        }
        
        // Query command line
        return QueryCommandLine(handle);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Exception getting command line for PID {Pid}", pid);
        return null;
    }
    finally
    {
        // Always cleanup
        if (handle != IntPtr.Zero)
        {
            CloseHandle(handle);
        }
    }
}
```

---

### Example 5: Window Handle Validation in Message Handler

**Pattern**: Validate window handles in WndProc before calling Win32 APIs.

```csharp
private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
{
    switch (msg)
    {
        case WmWindowPosChanging:
            // Validate window still exists
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                Log.Debug("Window handle invalid in WmWindowPosChanging");
                return IntPtr.Zero;
            }
            
            // Safe to call SetWindowPos
            SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize);
            handled = true;
            break;
            
        case WmActivateApp:
            // Validate before using handle
            if (hwnd != IntPtr.Zero && IsWindow(hwnd))
            {
                SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize);
            }
            break;
    }
    
    return IntPtr.Zero;
}
```

---

### Example 6: Detailed Error Logging with Fallback

**Pattern**: Log errors with context and provide fallback mechanisms.

```csharp
private void InitializePerformanceCounters()
{
    try
    {
        // Try primary counter
        uint status = PdhAddEnglishCounter(pdhQuery,
            "\\PhysicalDisk(_Total)\\% Idle Time",
            IntPtr.Zero,
            out pdhCounter);
        
        if (status != 0)
        {
            Log.Warning("PdhAddEnglishCounter failed for PhysicalDisk, " +
                "status {Status:X8}, trying LogicalDisk fallback", status);
            
            // Try fallback counter
            status = PdhAddEnglishCounter(pdhQuery,
                "\\LogicalDisk(_Total)\\% Idle Time",
                IntPtr.Zero,
                out pdhCounter);
            
            if (status != 0)
            {
                Log.Warning("PdhAddEnglishCounter failed for LogicalDisk, " +
                    "status {Status:X8}, disk metrics unavailable", status);
                return;
            }
            
            Log.Information("Using LogicalDisk counter as fallback");
        }
        
        Log.Information("Performance counters initialized successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Exception during performance counter initialization");
    }
}
```

---

### Example 7: Thread-Safe Cache with ConcurrentDictionary

**Pattern**: Use ConcurrentDictionary for thread-safe caching without explicit locks.

```csharp
private readonly ConcurrentDictionary<int, ProcessViewModel> viewModelCache = new();

public ProcessViewModel GetOrCreateViewModel(int pid)
{
    // Atomic operation: no race condition possible
    return viewModelCache.GetOrAdd(pid, _ => 
    {
        Log.Debug("Creating new ViewModel for PID {Pid}", pid);
        return new ProcessViewModel(pid);
    });
}

public void RemoveViewModel(int pid)
{
    if (viewModelCache.TryRemove(pid, out var vm))
    {
        Log.Debug("Removed ViewModel for PID {Pid}", pid);
        vm.Dispose();
    }
}

public void ClearCache()
{
    foreach (var vm in viewModelCache.Values)
    {
        vm.Dispose();
    }
    viewModelCache.Clear();
    Log.Information("ViewModel cache cleared");
}
```

---

### Example 8: Comprehensive Resource Cleanup

**Pattern**: Implement full IDisposable pattern with finalizer.

```csharp
public class ProcessService : IDisposable
{
    private IntPtr buffer = IntPtr.Zero;
    private bool disposed;
    
    public ProcessService()
    {
        buffer = Marshal.AllocHGlobal(1024 * 1024);
        Log.Debug("ProcessService initialized");
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;
        
        if (disposing)
        {
            Log.Debug("ProcessService disposing managed resources");
            // Dispose managed resources
        }
        
        // Always cleanup unmanaged resources
        if (buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
            Log.Debug("ProcessService unmanaged buffer freed");
        }
        
        disposed = true;
    }
    
    ~ProcessService()
    {
        Log.Warning("ProcessService finalizer called - Dispose was not called");
        Dispose(false);
    }
}
```

---

## Result<T> Pattern Examples

### Example 1: Basic Result<T> Usage

**Pattern**: Use Result<T> for operations that can fail for expected reasons.

```csharp
using SystemProcesses.Desktop.Helpers;

// ✅ Good - Result<T> for expected failures
public Result<ImageSource> GetIcon(string? processPath)
{
    if (string.IsNullOrEmpty(processPath))
    {
        return new Result<ImageSource>.Failure(
            new ArgumentNullException(nameof(processPath)),
            "Process path is null or empty");
    }

    try
    {
        using var icon = Icon.ExtractAssociatedIcon(processPath);
        if (icon == null)
        {
            return new Result<ImageSource>.Failure(
                new FileNotFoundException("No icon associated with file"),
                $"Icon.ExtractAssociatedIcon returned null for {processPath}");
        }

        var imageSource = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

        imageSource.Freeze();
        return new Result<ImageSource>.Success(imageSource);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to extract icon from {ProcessPath}", processPath);
        return new Result<ImageSource>.Failure(ex, $"Icon extraction failed for {processPath}");
    }
}

// Usage: Graceful degradation
var iconResult = GetIcon(processPath);
var icon = iconResult.GetValueOrDefault(defaultIcon);

// Usage: Explicit handling
iconResult.Match(
    onSuccess: img => DisplayIcon(img),
    onFailure: (ex, ctx) => Log.Warning(ex, "Failed: {Context}", ctx));
```

---

### Example 2: Result<T> with Validation

**Pattern**: Validate input and return Result<T> with specific error context.

```csharp
public Result<SafeProcessHandle> TryOpen(int pid, uint access)
{
    // Validate input
    if (pid <= 0)
    {
        return new Result<SafeProcessHandle>.Failure(
            new ArgumentException("PID must be greater than 0", nameof(pid)),
            $"Invalid PID: {pid}");
    }

    // Attempt operation
    var rawHandle = SystemPrimitives.OpenProcess(access, false, pid);
    
    if (rawHandle == IntPtr.Zero)
    {
        return new Result<SafeProcessHandle>.Failure(
            new UnauthorizedAccessException("OpenProcess failed"),
            $"Failed to open process {pid} with access 0x{access:X8} (access denied or process exited)");
    }

    // Success
    var handle = new SafeProcessHandle();
    handle.SetHandle(rawHandle);
    return new Result<SafeProcessHandle>.Success(handle);
}

// Usage
var result = TryOpen(pid, ProcessQueryLimitedInformation);

result.Match(
    onSuccess: handle =>
    {
        using (handle)
        {
            // Use handle
        }
    },
    onFailure: (ex, ctx) =>
    {
        Log.Warning(ex, "Failed to open process: {Context}", ctx);
    });
```

---

### Example 3: Result<T> in Service Methods

**Pattern**: Return Result<T> from service methods to provide error context to callers.

```csharp
public class ProcessService
{
    // ✅ Good - Result<T> for expected failures
    public Result<string> GetCommandLine(int pid)
    {
        if (pid <= 4)
        {
            return new Result<string>.Failure(
                new InvalidOperationException("Cannot query system processes"),
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

                var unicodeString = Marshal.PtrToStructure<SystemPrimitives.UnicodeString>(buffer);
                if (unicodeString.Buffer == IntPtr.Zero)
                {
                    return new Result<string>.Failure(
                        new InvalidOperationException("UnicodeString buffer is null"),
                        $"Command line buffer is null for PID {pid}");
                }

                string commandLine = Marshal.PtrToStringUni(unicodeString.Buffer, unicodeString.Length / 2) ?? string.Empty;
                return new Result<string>.Success(commandLine);
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

    // ✅ Good - Result<T> for expected failures
    public Result<string> GetProcessPath(int pid)
    {
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
}

// Usage in ViewModel
public partial class MainViewModel : ObservableObject
{
    private readonly ProcessService processService;

    private void UpdateProcessInfo(int pid)
    {
        // Get command line
        var cmdResult = processService.GetCommandLine(pid);
        string commandLine = cmdResult.GetValueOrDefault(string.Empty);

        // Get process path
        var pathResult = processService.GetProcessPath(pid);
        string? processPath = pathResult.GetValueOrDefault(null);

        // Update UI
        CommandLineText = commandLine;
        ProcessPathText = processPath ?? "Unknown";
    }
}
```

---

### Example 4: Result<T> Composition

**Pattern**: Chain multiple Result<T> operations with Match().

```csharp
public class ProcessAnalyzer
{
    private readonly ProcessService processService;
    private readonly IconCache iconCache;

    public void AnalyzeProcess(int pid)
    {
        // Get command line
        var cmdResult = processService.GetCommandLine(pid);
        
        cmdResult.Match(
            onSuccess: commandLine =>
            {
                // Extract executable path from command line
                string exePath = ExtractExecutablePath(commandLine);
                
                // Load icon
                var iconResult = iconCache.GetIcon(exePath);
                
                iconResult.Match(
                    onSuccess: icon =>
                    {
                        Log.Information("Successfully analyzed process {Pid}: {CommandLine}", pid, commandLine);
                        DisplayProcessInfo(pid, commandLine, icon);
                    },
                    onFailure: (ex, ctx) =>
                    {
                        Log.Warning(ex, "Failed to load icon: {Context}", ctx);
                        DisplayProcessInfo(pid, commandLine, defaultIcon);
                    });
            },
            onFailure: (ex, ctx) =>
            {
                Log.Warning(ex, "Failed to get command line: {Context}", ctx);
                // Fallback behavior
            });
    }
}
```

---

### Example 5: Result<T> vs Exceptions

**Pattern**: Use Result<T> for expected failures, exceptions for unexpected failures.

```csharp
// ✅ Good - Result<T> for expected failures
public Result<SafeHGlobalHandle> TryAllocate(int size)
{
    if (size <= 0)
    {
        return new Result<SafeHGlobalHandle>.Failure(
            new ArgumentException("Size must be greater than zero", nameof(size)),
            $"Invalid allocation size: {size} bytes");
    }

    try
    {
        IntPtr ptr = Marshal.AllocHGlobal(size);
        
        if (ptr == IntPtr.Zero)
        {
            return new Result<SafeHGlobalHandle>.Failure(
                new OutOfMemoryException($"Marshal.AllocHGlobal returned null"),
                $"Failed to allocate {size} bytes (out of memory)");
        }

        var handle = new SafeHGlobalHandle(size);
        handle.SetHandle(ptr);
        return new Result<SafeHGlobalHandle>.Success(handle);
    }
    catch (Exception ex)
    {
        return new Result<SafeHGlobalHandle>.Failure(ex, $"Exception occurred while allocating {size} bytes");
    }
}

// ✅ Good - Throw for critical failures
public void InitializeService()
{
    try
    {
        buffer = Marshal.AllocHGlobal(bufferSize);
        if (buffer == IntPtr.Zero)
        {
            throw new OutOfMemoryException(
                $"Failed to allocate {bufferSize} bytes for core buffer");
        }
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Failed to initialize ProcessService");
        throw; // Critical failure - propagate
    }
}

// ❌ Bad - Throwing for expected failures
public SafeHGlobalHandle Allocate(int size)
{
    if (size <= 0)
    {
        throw new ArgumentException("Size must be greater than zero"); // Use Result<T> instead
    }

    IntPtr ptr = Marshal.AllocHGlobal(size);
    if (ptr == IntPtr.Zero)
    {
        throw new OutOfMemoryException(); // Use Result<T> instead
    }

    var handle = new SafeHGlobalHandle(size);
    handle.SetHandle(ptr);
    return handle;
}
```

---

## Complete Real-World Example: Process Refresh Cycle

```csharp
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IProcessService processService;
    private readonly SemaphoreSlim refreshSemaphore = new(1, 1);
    private readonly DispatcherTimer refreshTimer;
    private readonly Dictionary<int, ProcessItemViewModel> viewModelCache = new(512);
    
    [ObservableProperty]
    private ObservableCollection<ProcessItemViewModel> processes = new();
    
    [ObservableProperty]
    private SystemStats systemStats;
    
    public MainViewModel(IProcessService processService)
    {
        this.processService = processService;
        
        refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        refreshTimer.Tick += async (s, e) => await RefreshAsync();
        refreshTimer.Start();
    }
    
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!await refreshSemaphore.WaitAsync(0))
            return;
        
        try
        {
            // Heavy work off UI thread
            var (roots, stats) = await Task.Run(() => 
                processService.GetProcessTreeAsync());
            
            // UI updates on UI thread
            SystemStats = stats;
            SyncProcessCollection(roots);
        }
        finally
        {
            refreshSemaphore.Release();
        }
    }
    
    private void SyncProcessCollection(List<ProcessInfo> newRoots)
    {
        var newPids = new HashSet<int>();
        CollectAllPids(newRoots, newPids);
        
        // Remove dead processes
        for (int i = Processes.Count - 1; i >= 0; i--)
        {
            if (!newPids.Contains(Processes[i].Pid))
            {
                viewModelCache.Remove(Processes[i].Pid);
                Processes.RemoveAt(i);
            }
        }
        
        // Update/add processes
        foreach (var process in newRoots)
        {
            SyncProcess(process);
        }
    }
    
    private void SyncProcess(ProcessInfo process)
    {
        if (viewModelCache.TryGetValue(process.Pid, out var vm))
        {
            // Update existing
            vm.UpdateFrom(process);
        }
        else
        {
            // Create new
            vm = new ProcessItemViewModel(process);
            viewModelCache[process.Pid] = vm;
            Processes.Add(vm);
        }
        
        // Recursively sync children
        foreach (var child in process.Children)
        {
            SyncProcess(child);
        }
    }
    
    private void CollectAllPids(List<ProcessInfo> processes, HashSet<int> pids)
    {
        foreach (var process in processes)
        {
            pids.Add(process.Pid);
            CollectAllPids(process.Children, pids);
        }
    }
    
    public void Dispose()
    {
        refreshTimer?.Stop();
        refreshSemaphore?.Dispose();
        processService?.Dispose();
    }
}
```

---

## Summary

These examples demonstrate the core patterns used throughout the SystemProcesses project:

1. **Zero-Allocation**: Reuse objects, use stack allocation, cache strings
2. **Object Pooling**: Leverage `ObjectPool<T>` for frequently-created objects
3. **MVVM**: Use source generators, dependency injection, and proper disposal
4. **P/Invoke**: Safe handle management, proper error checking, marshalling
5. **Unsafe Code**: Bounds checking, pointer validation, Span<T> usage
6. **WPF**: Freeze objects, dispatcher invocation, differential updates
7. **Threading**: SemaphoreSlim, async/await, proper cancellation
8. **Performance**: Avoid LINQ in hot paths, pre-size collections, batch updates

Always profile before and after optimization to validate improvements!