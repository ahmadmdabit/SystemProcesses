# Coding Standards & Conventions

This document defines the coding standards, style guidelines, and best practices for the SystemProcesses project.

## 1. General C# Conventions

### 1.1 Naming Conventions

Follow Microsoft's official C# naming guidelines:

```csharp
// ✅ PascalCase for types, methods, properties, constants
public class ProcessService { }
public void UpdateSnapshot() { }
public int ProcessCount { get; }
public const int MaxProcesses = 2000;

// ✅ camelCase for private fields, parameters, local variables
private readonly Dictionary<int, ProcessInfo> activeProcesses;
public void AddProcess(int processId) { }
var localVariable = 42;

// ✅ _camelCase for private fields (acceptable alternative)
private readonly IProcessService _processService;

// ✅ IPascalCase for interfaces
public interface IProcessService { }

// ❌ Avoid Hungarian notation
// Bad: int iPid, string strName
// Good: int pid, string name
```

### 1.2 File Organization

```csharp
// File: ProcessService.cs
// Order:
// 1. Using directives (sorted, System.* first)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Serilog;

using SystemProcesses.Desktop.Models;

// 2. Namespace
namespace SystemProcesses.Desktop.Services;

// 3. Class declaration with XML comments
/// <summary>
/// Core service for system process enumeration and monitoring.
/// </summary>
public class ProcessService : IProcessService, IDisposable
{
    // 4. Constants
    private const int InitialBufferSize = 1024 * 1024;
    
    // 5. Private fields
    private readonly Dictionary<int, ProcessInfo> activeProcesses;
    private IntPtr buffer = IntPtr.Zero;
    
    // 6. Constructor
    public ProcessService() { }
    
    // 7. Public properties
    public int ProcessCount => activeProcesses.Count;
    
    // 8. Public methods
    public void UpdateSnapshot() { }
    
    // 9. Private methods
    private void ParseProcessData() { }
    
    // 10. IDisposable implementation
    public void Dispose() { }
}
```

### 1.3 Bracing Style

**Always use braces**, even for single-line statements:

```csharp
// ✅ Good
if (condition)
{
    DoSomething();
}

// ❌ Bad
if (condition)
    DoSomething();

// Exception: Guard clauses on single line are acceptable
if (value == null) return;
if (count == 0) throw new ArgumentException(nameof(count));
```

### 1.4 Line Length

- **Preferred**: 120 characters maximum
- **Hard Limit**: 140 characters
- Break long method chains across lines with proper indentation

```csharp
// ✅ Good
var result = collection
    .Where(x => x.IsActive)
    .OrderBy(x => x.Name)
    .Select(x => x.Id)
    .ToList();

// ❌ Bad - too long
var result = collection.Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => x.Id).ToList();
```

---

## 2. Unsafe Code Standards

### 2.1 When to Use Unsafe Code

**Allowed**:
- P/Invoke scenarios requiring pointer arithmetic
- Parsing native memory structures from Windows APIs
- Performance-critical loops where bounds checking is proven bottleneck (with benchmarks)

**Prohibited**:
- General application logic
- ViewModel or UI code
- Any code with managed alternatives that meet performance requirements

### 2.2 Unsafe Block Scope

Minimize the scope of `unsafe` blocks:

```csharp
// ✅ Good - minimal unsafe scope
public void ProcessData(IntPtr buffer, int length)
{
    var results = new List<ProcessInfo>();
    
    unsafe
    {
        byte* ptr = (byte*)buffer;
        byte* end = ptr + length;
        
        while (ptr < end)
        {
            var info = ParseProcess(ref ptr);
            results.Add(info);
        }
    }
    
    // Safe code continues here
    UpdateCache(results);
}

// ❌ Bad - entire method is unsafe
public unsafe void ProcessData(IntPtr buffer, int length)
{
    // Unnecessarily marks entire method
}
```

### 2.3 Pointer Validation

**Always validate pointers** before dereferencing:

```csharp
unsafe
{
    byte* ptr = (byte*)buffer;
    
    // ✅ Good - check for null and bounds
    if (ptr == null)
    {
        throw new ArgumentNullException(nameof(buffer));
    }
    
    byte* end = ptr + length;
    if (ptr >= end)
    {
        throw new ArgumentException("Invalid buffer length");
    }
    
    // Safe to dereference
    int value = *(int*)ptr;
}
```

### 2.4 Pointer Arithmetic Safety

```csharp
// ✅ Good - bounds checking
unsafe
{
    byte* current = (byte*)buffer;
    byte* end = current + bufferSize;
    
    while (current < end)
    {
        // Check before reading structure size
        if (current + sizeof(int) > end)
        {
            Log.Warning("Buffer overrun prevented");
            break;
        }
        
        int structSize = *(int*)current;
        
        // Check if full structure is available
        if (current + structSize > end)
        {
            break;
        }
        
        current += structSize;
    }
}

// ❌ Bad - no bounds checking
unsafe
{
    byte* current = (byte*)buffer;
    while (true) // No termination condition!
    {
        int structSize = *(int*)current; // Could read past buffer end
        current += structSize;
    }
}
```

### 2.5 stackalloc Guidelines

- **Only use for small, short-lived buffers** (< 1 KB recommended, < 16 KB absolute maximum)
- Always use `Span<T>` wrapper for safety
- Never return stackalloc memory from methods

```csharp
// ✅ Good - small temporary buffer
Span<char> buffer = stackalloc char[16];
buffer[0] = 'C';
buffer[1] = ':';
// ... use immediately

// ✅ Good - size-checked stackalloc
Span<int> numbers = count <= 32 
    ? stackalloc int[count] 
    : new int[count];

// ❌ Bad - too large for stack
Span<byte> huge = stackalloc byte[1024 * 1024]; // Stack overflow risk!

// ❌ Bad - returning stack memory
public Span<int> GetBuffer()
{
    return stackalloc int[10]; // DANGER: Returns stack reference
}
```

---

## 3. Performance Rules

### 3.1 Hot Path Optimization

**Hot paths** are code executed frequently (refresh loops, UI updates, event handlers).

#### Rule 3.1.1: No LINQ in Hot Paths

```csharp
// ❌ Bad - allocates enumerators, delegates, collections
public void RefreshProcesses()
{
    var top5 = processes
        .Where(p => p.CpuUsage > 0)
        .OrderByDescending(p => p.CpuUsage)
        .Take(5)
        .ToList();
}

// ✅ Good - manual iteration with reusable buffer
private readonly ProcessInfo?[] top5Buffer = new ProcessInfo?[5];

public void RefreshProcesses()
{
    Array.Clear(top5Buffer, 0, 5);
    
    foreach (var process in processes)
    {
        if (process.CpuUsage <= 0) continue;
        
        // Insertion sort into fixed buffer
        for (int i = 0; i < 5; i++)
        {
            if (top5Buffer[i] == null || process.CpuUsage > top5Buffer[i]!.CpuUsage)
            {
                // Shift and insert
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

#### Rule 3.1.2: Reuse Collections

```csharp
// ❌ Bad - allocates new collections every call
public List<ProcessInfo> GetActiveProcesses()
{
    var result = new List<ProcessInfo>(); // New allocation
    // ...
    return result;
}

// ✅ Good - reuse instance
private readonly List<ProcessInfo> reusableList = new(256);

public List<ProcessInfo> GetActiveProcesses()
{
    reusableList.Clear(); // O(1) if capacity unchanged
    // ...
    return reusableList;
}
```

#### Rule 3.1.3: Avoid String Allocations

```csharp
// ❌ Bad - allocates on every access
public string StatusText => $"CPU: {cpuUsage:F2}%";

// ✅ Good - use StringBuilder pooling
public string GetStatusText()
{
    using var psb = StringBuilderPool.Rent();
    psb.Builder.Append("CPU: ");
    psb.Builder.Append(cpuUsage.ToString("F2"));
    psb.Builder.Append('%');
    return psb.Build();
}

// ✅ Better - cache static values
private static readonly string[] percentCache = new string[101];
static ProcessInfo()
{
    for (int i = 0; i <= 100; i++)
    {
        percentCache[i] = $"{i}%";
    }
}

public string GetCpuText()
{
    int rounded = (int)Math.Round(cpuUsage);
    return rounded <= 100 ? percentCache[rounded] : $"{rounded}%";
}
```

### 3.2 Memory Allocation Rules

#### Rule 3.2.1: Prefer Span<T> for Temporary Buffers

```csharp
// ✅ Good - no heap allocation
public void ProcessBytes(ReadOnlySpan<byte> data)
{
    Span<char> hexBuffer = stackalloc char[data.Length * 2];
    // Convert to hex without allocation
}

// ❌ Bad - allocates array
public void ProcessBytes(byte[] data)
{
    char[] hexBuffer = new char[data.Length * 2];
}
```

#### Rule 3.2.2: Use Object Pooling for Reusable Objects

```csharp
// ✅ Good - pooled StringBuilder
using (var psb = StringBuilderPool.Rent())
{
    psb.Builder.Append("Value");
    string result = psb.Build();
}

// ❌ Bad - allocates every time
var sb = new StringBuilder();
sb.Append("Value");
string result = sb.ToString();
```

#### Rule 3.2.3: Pre-size Collections

```csharp
// ✅ Good - avoid resizing
var processes = new Dictionary<int, ProcessInfo>(capacity: 512);
var list = new List<ProcessInfo>(capacity: 64);

// ❌ Bad - will resize multiple times
var processes = new Dictionary<int, ProcessInfo>(); // Default 0 capacity
```

### 3.3 Boxing Prevention

```csharp
// ❌ Bad - boxes int to object
Log.Information("Process count: {Count}", processCount); // int boxed

// ✅ Good - explicit interpolation or formatting
Log.Information($"Process count: {processCount}"); // No boxing

// ❌ Bad - Enum.ToString() boxes
Log.Debug("State: {State}", ProcessState.Running);

// ✅ Good - use nameof or cached strings
private static readonly Dictionary<ProcessState, string> stateNames = new()
{
    [ProcessState.Running] = "Running",
    [ProcessState.Stopped] = "Stopped"
};
Log.Debug("State: {State}", stateNames[state]);
```

### 3.4 Benchmark Requirements

**Any performance claim must be backed by benchmarks:**

```csharp
// When proposing optimization, include:
// [Benchmark]
// public void CurrentImplementation() { /* ... */ }
// 
// [Benchmark]
// public void ProposedOptimization() { /* ... */ }
//
// Results:
// | Method                  | Mean     | Allocated |
// |------------------------ |---------:|----------:|
// | CurrentImplementation   | 245.3 us | 12.5 KB   |
// | ProposedOptimization    | 189.7 us |  2.1 KB   |
```

---

## 4. MVVM Pattern Standards

### 4.1 ViewModel Implementation

**Use CommunityToolkit.Mvvm source generators:**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public partial class MainViewModel : ObservableObject
{
    // ✅ Good - source generated property
    [ObservableProperty]
    private string searchText = string.Empty;
    
    // ✅ Good - source generated command
    [RelayCommand]
    private void RefreshData()
    {
        // Implementation
    }
    
    // ✅ Good - command with CanExecute
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshDataAsync()
    {
        // Implementation
    }
    
    private bool CanRefresh() => !isRefreshing;
}
```

### 4.2 Dependency Injection

**Always use constructor injection:**

```csharp
// ✅ Good - dependencies explicit
public class MainViewModel : ObservableObject
{
    private readonly IProcessService processService;
    private readonly IImageLoaderService imageLoader;
    
    public MainViewModel(
        IProcessService processService,
        IImageLoaderService imageLoader)
    {
        this.processService = processService ?? throw new ArgumentNullException(nameof(processService));
        this.imageLoader = imageLoader ?? throw new ArgumentNullException(nameof(imageLoader));
    }
}

// ❌ Bad - service locator pattern
public class MainViewModel : ObservableObject
{
    private readonly IProcessService processService;
    
    public MainViewModel()
    {
        processService = ServiceLocator.Get<IProcessService>(); // Anti-pattern
    }
}
```

### 4.3 ViewModel Lifetime

- **Singleton ViewModels**: `MainViewModel`, `StatsViewModel` (application lifetime)
- **Transient ViewModels**: `ProcessItemViewModel` (created per process)
- **Always implement `IDisposable`** for ViewModels with subscriptions

```csharp
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer refreshTimer;
    
    public MainViewModel()
    {
        refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        refreshTimer.Tick += OnRefreshTimerTick;
    }
    
    public void Dispose()
    {
        refreshTimer?.Stop();
        refreshTimer.Tick -= OnRefreshTimerTick;
    }
}
```

---

## 5. P/Invoke Standards

### 5.1 Use LibraryImport (.NET 7+)

```csharp
// ✅ Good - source generated, better performance
[LibraryImport("ntdll.dll")]
[UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
internal static partial int NtQuerySystemInformation(
    int SystemInformationClass,
    IntPtr SystemInformation,
    int SystemInformationLength,
    out int ReturnLength);

// ❌ Bad - legacy DllImport
[DllImport("ntdll.dll")]
internal static extern int NtQuerySystemInformation(
    int SystemInformationClass,
    IntPtr SystemInformation,
    int SystemInformationLength,
    out int ReturnLength);
```

### 5.2 Handle Management

```csharp
// ✅ Good - use SafeHandle
public sealed class SafeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeProcessHandle() : base(ownsHandle: true) { }
    
    protected override bool ReleaseHandle()
    {
        return CloseHandle(handle);
    }
}

// Usage
using var processHandle = OpenProcess(rights, false, pid);
if (processHandle.IsInvalid)
{
    return null;
}

// ❌ Bad - manual IntPtr cleanup
IntPtr handle = OpenProcess(rights, false, pid);
try
{
    // Use handle
}
finally
{
    CloseHandle(handle); // Easy to forget
}
```

### 5.3 Structure Marshalling

```csharp
// ✅ Good - explicit layout and sizes
[StructLayout(LayoutKind.Sequential)]
internal struct SystemProcessInformation
{
    public uint NextEntryOffset;
    public uint NumberOfThreads;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
    public byte[] Reserved1;
    public UnicodeString ImageName;
    // ...
}

// ✅ Good - read without marshalling (faster)
unsafe
{
    byte* ptr = (byte*)buffer;
    uint nextOffset = *(uint*)ptr;
    uint threadCount = *(uint*)(ptr + 4);
}

// ❌ Bad - unnecessary Marshal.PtrToStructure
var info = Marshal.PtrToStructure<SystemProcessInformation>(ptr); // Allocates
```

---

## 6. Error Handling

### 6.1 Exception Handling Strategy

```csharp
// ✅ Good - catch specific exceptions
try
{
    var handle = OpenProcess(rights, false, pid);
}
catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorAccessDenied)
{
    Log.Warning("Access denied for PID {Pid}", pid);
    return null;
}
catch (Win32Exception ex)
{
    Log.Error(ex, "Failed to open process {Pid}", pid);
    throw;
}

// ❌ Bad - swallowing all exceptions
try
{
    var handle = OpenProcess(rights, false, pid);
}
catch
{
    return null; // Silent failure
}
```

### 6.2 Validation

```csharp
// ✅ Good - fail fast with ArgumentException
public void UpdateProcess(ProcessInfo info)
{
    ArgumentNullException.ThrowIfNull(info);
    
    if (info.Pid <= 0)
    {
        throw new ArgumentException("Invalid PID", nameof(info));
    }
    
    // Continue with valid input
}

// ❌ Bad - defensive coding without validation
public void UpdateProcess(ProcessInfo? info)
{
    if (info == null || info.Pid <= 0)
    {
        return; // Silent failure
    }
}
```

### 6.3 Logging Standards

```csharp
// ✅ Good - structured logging with context
Log.Information("Process {Name} (PID: {Pid}) started at {StartTime}", 
    name, pid, startTime);

// ✅ Good - log exceptions with context
Log.Error(ex, "Failed to terminate process {Pid}", pid);

// ❌ Bad - string concatenation
Log.Information("Process " + name + " started"); // Allocates string

// ❌ Bad - logging in hot paths
foreach (var process in processes) // Runs 300+ times/sec
{
    Log.Debug("Processing {Pid}", process.Pid); // Too verbose
}
```

---

## 7. WPF-Specific Standards

### 7.1 Freezing WPF Objects

```csharp
// ✅ Good - freeze for thread-safety
var bitmap = new BitmapImage();
bitmap.BeginInit();
bitmap.UriSource = uri;
bitmap.CacheOption = BitmapCacheOption.OnLoad;
bitmap.EndInit();
bitmap.Freeze(); // Required for cross-thread use
return bitmap;

// ❌ Bad - non-frozen objects have thread affinity
var bitmap = new BitmapImage(uri);
return bitmap; // Cannot use from background thread
```

### 7.2 Dispatcher Access

```csharp
// ✅ Good - check if invoke needed
public void UpdateUI(string text)
{
    if (Application.Current.Dispatcher.CheckAccess())
    {
        StatusText = text;
    }
    else
    {
        Application.Current.Dispatcher.Invoke(() => StatusText = text);
    }
}

// ✅ Good - async pattern
public async Task UpdateUIAsync(string text)
{
    await Application.Current.Dispatcher.InvokeAsync(() => StatusText = text);
}
```

### 7.3 ObservableCollection Updates

```csharp
// ✅ Good - batch updates to minimize notifications
public void UpdateProcessList(List<ProcessInfo> newProcesses)
{
    // Differential update algorithm
    // Only add/remove/update changed items
}

// ❌ Bad - clear and rebuild
public void UpdateProcessList(List<ProcessInfo> newProcesses)
{
    Processes.Clear(); // Triggers full UI rebuild
    foreach (var p in newProcesses)
    {
        Processes.Add(p); // Multiple notifications
    }
}
```

---

## 8. Code Documentation

### 8.1 XML Documentation

**Required for**:
- All public/protected members
- All interfaces
- Complex private methods

```csharp
/// <summary>
/// Updates the process snapshot by querying the Windows kernel.
/// </summary>
/// <returns>
/// A tuple containing the root process list and system statistics.
/// </returns>
/// <exception cref="Win32Exception">
/// Thrown when the native API call fails.
/// </exception>
/// <remarks>
/// This method executes off the UI thread and should be called
/// via <see cref="Task.Run"/> to avoid blocking.
/// </remarks>
public async Task<(List<ProcessInfo> Roots, SystemStats Stats)> GetProcessTreeAsync()
{
    // Implementation
}
```

### 8.2 Inline Comments

```csharp
// ✅ Good - explain WHY, not WHAT
// NtQuerySystemInformation may return insufficient buffer size on first call.
// We retry with the reported required size + 1MB padding for safety.
if (status == StatusInfoLengthMismatch)
{
    buffer = ReallocateBuffer(requiredSize + (1024 * 1024));
}

// ❌ Bad - comments that restate code
// Check if status equals StatusInfoLengthMismatch
if (status == StatusInfoLengthMismatch)
{
    // Reallocate buffer
    buffer = ReallocateBuffer(size);
}
```

---

## 9. Unsafe Code Validation Patterns

### 9.1 Buffer Validation

**Always validate buffers before use**:

```csharp
private const int MaxBufferSize = 100 * 1024 * 1024; // 100 MB limit

private void ValidateBuffer(IntPtr buffer, int size)
{
    if (buffer == IntPtr.Zero)
    {
        throw new InvalidOperationException("Buffer not initialized");
    }
    
    if (size <= 0 || size > MaxBufferSize)
    {
        throw new ArgumentOutOfRangeException(nameof(size),
            $"Buffer size must be between 1 and {MaxBufferSize} bytes");
    }
}
```

### 9.2 Pointer Arithmetic Bounds Checking

**Validate pointer arithmetic to prevent buffer overflows**:

```csharp
private unsafe void ParseData(IntPtr buffer, int bufferSize)
{
    long offset = 0;
    
    while (offset < bufferSize)
    {
        // Validate pointer is within bounds
        if (offset + sizeof(DataStructure) > bufferSize)
        {
            Log.Warning("Pointer arithmetic exceeded buffer bounds at offset {Offset}", offset);
            break;
        }
        
        var ptr = (DataStructure*)((byte*)buffer + offset);
        
        // Validate next offset
        if (ptr->NextOffset < 0 || ptr->NextOffset > bufferSize - offset)
        {
            Log.Warning("Invalid NextOffset {Offset}", ptr->NextOffset);
            break;
        }
        
        if (ptr->NextOffset == 0) break;
        offset += ptr->NextOffset;
    }
}
```

### 9.3 String Encoding Validation

**Validate string encoding before marshalling**:

```csharp
private unsafe string? ExtractString(UnicodeString* stringPtr)
{
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
    
    try
    {
        return Marshal.PtrToStringUni(stringPtr->Buffer, stringPtr->Length / 2);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to extract string from pointer");
        return null;
    }
}
```

### 9.4 Handle Validation

**Always validate P/Invoke handles before use**:

```csharp
private string? GetProcessCommandLine(int pid)
{
    try
    {
        var handle = OpenProcess(ProcessAccessFlags.QueryLimited, false, pid);
        
        // Validate handle
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            Log.Warning("Failed to open process {Pid}, error {Error}",
                pid, Marshal.GetLastWin32Error());
            return null;
        }
        
        try
        {
            return QueryCommandLine(handle);
        }
        finally
        {
            CloseHandle(handle);
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Exception getting command line for PID {Pid}", pid);
        return null;
    }
}
```

### 9.5 Window Handle Validation

**Validate window handles in message handlers**:

```csharp
private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
{
    switch (msg)
    {
        case WmWindowPosChanging:
            // Validate window still exists before calling Win32 APIs
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            {
                return IntPtr.Zero;
            }
            
            SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize);
            handled = true;
            break;
    }
    
    return IntPtr.Zero;
}
```

---

## 10. Error Handling Patterns

### 10.1 Log and Continue

**For non-critical operations, log and continue**:

```csharp
private void ProcessAllItems(List<Item> items)
{
    foreach (var item in items)
    {
        try
        {
            ProcessItem(item);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to process item {ItemId}, continuing", item.Id);
            // Continue with next item
        }
    }
}
```

### 10.2 Detailed Error Logging

**Log error codes and context for debugging**:

```csharp
private void InitializeNativeApi()
{
    try
    {
        uint status = NativeApi.Initialize();
        
        if (status != 0)
        {
            Log.Warning("NativeApi.Initialize failed with status {Status:X8}", status);
            return;
        }
        
        Log.Information("NativeApi initialized successfully");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Exception during NativeApi initialization");
    }
}
```

### 10.3 Fallback Mechanisms

**Provide fallback options when primary approach fails**:

```csharp
private void InitializePdh()
{
    uint status = PdhAddEnglishCounter(query, 
        "\\PhysicalDisk(_Total)\\% Idle Time", 
        out counter);
    
    if (status != 0)
    {
        Log.Warning("PhysicalDisk counter failed, trying LogicalDisk fallback");
        
        status = PdhAddEnglishCounter(query,
            "\\LogicalDisk(_Total)\\% Idle Time",
            out counter);
        
        if (status != 0)
        {
            Log.Warning("LogicalDisk counter also failed, disk metrics unavailable");
            return;
        }
    }
    
    Log.Information("PDH counter initialized successfully");
}
```

---

## 11. Thread-Safety Patterns

### 11.1 ConcurrentDictionary for Shared Caches

**Use ConcurrentDictionary for thread-safe caching**:

```csharp
private readonly ConcurrentDictionary<int, ProcessViewModel> viewModelCache = new();

public ProcessViewModel GetOrCreate(int pid)
{
    // Atomic operation: no race condition possible
    return viewModelCache.GetOrAdd(pid, _ => new ProcessViewModel(pid));
}
```

### 11.2 SemaphoreSlim for Concurrency Control

**Use SemaphoreSlim to prevent concurrent operations**:

```csharp
private readonly SemaphoreSlim refreshSemaphore = new(1, 1);

public async Task RefreshAsync()
{
    // Non-blocking check: skip if already refreshing
    if (!await refreshSemaphore.WaitAsync(0))
    {
        Log.Debug("Refresh already in progress, skipping");
        return;
    }
    
    try
    {
        var data = await Task.Run(() => GetSnapshot());
        UpdateUI(data);
    }
    finally
    {
        refreshSemaphore.Release();
    }
}
```

---

## 12. Testing Guidelines

### 12.1 Unit Test Structure

```csharp
[TestFixture]
public class ProcessServiceTests
{
    private ProcessService service;
    
    [SetUp]
    public void Setup()
    {
        service = new ProcessService();
    }
    
    [TearDown]
    public void Cleanup()
    {
        service?.Dispose();
    }
    
    [Test]
    public void UpdateProcessSnapshotWithValidDataShouldUpdateCache()
    {
        // Arrange
        var expectedPid = 1234;
        
        // Act
        service.UpdateProcessSnapshot();
        
        // Assert
        var process = service.GetProcess(expectedPid);
        Assert.That(process, Is.Not.Null);
        Assert.That(process.Pid, Is.EqualTo(expectedPid));
    }
}
```

### 12.2 Benchmark Tests

```csharp
[MemoryDiagnoser]
public class ProcessServiceBenchmarks
{
    private ProcessService service;
    
    [GlobalSetup]
    public void Setup()
    {
        service = new ProcessService();
    }
    
    [Benchmark]
    public void UpdateProcessSnapshot()
    {
        service.UpdateProcessSnapshot();
    }
}
```

---

## 13. Code Review Checklist

Before submitting code for review, verify:

- [ ] No LINQ in hot paths (refresh loops, UI updates)
- [ ] Collections are pre-sized where possible
- [ ] Unsafe code has bounds checking
- [ ] P/Invoke handles are properly disposed
- [ ] WPF objects are frozen for cross-thread use
- [ ] ViewModels implement IDisposable if needed
- [ ] Public APIs have XML documentation
- [ ] Performance-critical changes have benchmarks
- [ ] No string allocations in frequently-called methods
- [ ] Error handling doesn't swallow exceptions silently
- [ ] Validation is present for all unsafe operations
- [ ] Thread-safe collections used for shared state
- [ ] Detailed logging for error conditions

---

## 14. Anti-Patterns to Avoid

### ❌ Service Locator
```csharp
var service = ServiceLocator.Current.Get<IProcessService>();
```

### ❌ God Objects
```csharp
public class EverythingManager // Does too much
{
    public void LoadProcesses() { }
    public void SaveSettings() { }
    public void UpdateUI() { }
    public void ManageNetwork() { }
}
```

### ❌ Premature Optimization
```csharp
// Don't optimize without profiling first
// Don't use unsafe code unless benchmarks prove necessity
```

### ❌ Ignoring IDisposable
```csharp
var service = new ProcessService(); // Implements IDisposable
// ... use service
// Forgot to dispose - leaks native memory
```

### ❌ Unvalidated Unsafe Code
```csharp
// ❌ BAD - No validation
unsafe void ParseData(IntPtr buffer)
{
    var ptr = (DataStructure*)buffer;
    // Dereference without checking bounds
}

// ✅ GOOD - With validation
unsafe void ParseData(IntPtr buffer, int size)
{
    if (buffer == IntPtr.Zero || size < sizeof(DataStructure))
        return;
    
    var ptr = (DataStructure*)buffer;
    // Safe to dereference
}
```

---

## 15. Useful Resources

- **C# Coding Conventions**: https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
- **Framework Design Guidelines**: https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/
- **High Performance .NET**: https://github.com/dotnet/performance
- **BenchmarkDotNet**: https://benchmarkdotnet.org/
- **NUnit Documentation**: https://docs.nunit.org/
- **Unsafe Code Guidelines**: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/unsafe
