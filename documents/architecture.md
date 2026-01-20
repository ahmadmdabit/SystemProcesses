# SystemProcesses - Architecture Documentation

## 1. Project Overview

**SystemProcesses** is a high-performance, zero-allocation Windows system monitor built with .NET 9 and WPF. It serves as a lightweight Task Manager alternative that prioritizes minimal resource consumption and extreme performance.

### Core Philosophy: Zero-Allocation Architecture

The entire application is designed around minimizing Garbage Collection (GC) pressure through:
- **Object Reuse**: `ProcessInfo` objects and internal buffers are reused across update cycles
- **Unsafe Operations**: `stackalloc` and pointer arithmetic for parsing kernel structures
- **Object Pooling**: `StringBuilderPool` for string formatting
- **Manual Memory Management**: Unmanaged buffers for P/Invoke operations

## 2. System Architecture

### 2.1 Architectural Layers

```
┌─────────────────────────────────────────────────────┐
│              Presentation Layer (WPF)                │
│  MainWindow.xaml, Views/*, Converters/*             │
└────────────────┬────────────────────────────────────┘
                 │ Data Binding
┌────────────────▼────────────────────────────────────┐
│           ViewModel Layer (MVVM)                     │
│  MainViewModel, ProcessItemViewModel, StatsViewModel │
│  (CommunityToolkit.Mvvm.ComponentModel)             │
└────────────────┬────────────────────────────────────┘
                 │ Service Injection
┌────────────────▼────────────────────────────────────┐
│              Services Layer                          │
│  ProcessService, ImageLoaderService, IconCache       │
│  SystemPrimitives (P/Invoke)                        │
└────────────────┬────────────────────────────────────┘
                 │ P/Invoke
┌────────────────▼────────────────────────────────────┐
│         Windows Kernel (Native APIs)                 │
│  ntdll.dll, advapi32.dll, pdh.dll, kernel32.dll     │
└─────────────────────────────────────────────────────┘
```

### 2.2 Project Structure

```
SystemProcesses.Desktop/
├── Services/               # Core business logic and system interop
│   ├── ProcessService.cs          # Main process data engine
│   ├── SystemPrimitives.cs        # P/Invoke definitions
│   ├── ImageLoaderService.cs      # Async image loading & caching
│   ├── IconCache.cs               # GDI+ icon extraction
│   └── IProcessService.cs         # Service contracts
├── ViewModels/            # MVVM presentation logic
│   ├── MainViewModel.cs           # Primary UI coordinator
│   ├── ProcessItemViewModel.cs    # Per-process wrapper
│   └── StatsViewModel.cs          # System statistics
├── Models/                # Data structures
│   └── ProcessInfo.cs             # Core process data object
├── Views/                 # XAML UI components
│   ├── ProcessTreeView.xaml
│   └── StatsView.xaml             # Always-on-top stats overlay
├── Helpers/               # Utility classes
│   ├── StringBuilderPool.cs       # Object pooling for strings
│   └── LiteDialog.cs              # Minimal WPF dialogs
├── Converters/            # XAML value converters
└── Resources/             # Images, icons, themes
```

## 3. Data Flow Architecture

### 3.1 Process Data Pipeline

```
┌──────────────────┐
│  Windows Kernel  │
│  (ntdll.dll)     │
└────────┬─────────┘
         │ NtQuerySystemInformation (single syscall)
         │ Returns: Raw memory block (1-2 MB)
         ▼
┌──────────────────────────────────┐
│  ProcessService.UpdateProcessSnapshot  │
│  - Unsafe pointer iteration      │
│  - Parse SystemProcessInformation│
│  - Calculate CPU/IO deltas       │
└────────┬─────────────────────────┘
         │ Dictionary<int, ProcessInfo>
         │ (Reuses existing objects)
         ▼
┌──────────────────────────────────┐
│  ProcessService.RebuildTreeStructure   │
│  - Link parent/child relationships│
│  - Identify root processes       │
└────────┬─────────────────────────┘
         │ List<ProcessInfo> Roots
         │ SystemStats
         ▼
┌──────────────────────────────────┐
│  MainViewModel.RefreshAsync      │
│  - SyncProcessCollection algorithm│
│  - Differential UI updates       │
│  - Preserve expansion state      │
└────────┬─────────────────────────┘
         │ ObservableCollection changes
         │ INotifyPropertyChanged events
         ▼
┌──────────────────────────────────┐
│  WPF Data Binding Engine         │
│  - Virtualized TreeView          │
│  - Efficient rendering           │
└──────────────────────────────────┘
```

### 3.2 Threading Model

**Producer-Consumer Pattern with Single Background Thread**

- **UI Thread (Dispatcher)**: Renders WPF controls, handles user input
- **Background Thread (Task.Run)**: Executes `ProcessService.UpdateProcessSnapshot`
- **Synchronization**: `SemaphoreSlim` ensures only one refresh cycle at a time
- **Data Transfer**: Immutable snapshot passed from background to UI thread via `await`

```csharp
// Simplified threading flow
public async Task RefreshAsync()
{
    await semaphore.WaitAsync(); // Prevent concurrent refreshes
    try
    {
        // Execute off UI thread
        var (roots, stats) = await processService.GetProcessTreeAsync();
        
        // Return to UI thread for binding updates
        SyncProcessCollection(roots);
        UpdateSystemStats(stats);
    }
    finally
    {
        semaphore.Release();
    }
}
```

## 4. Key Design Patterns

### 4.1 MVVM (Model-View-ViewModel)

- **Models**: `ProcessInfo`, `SystemStats`, `DriveStats` (pure data)
- **ViewModels**: `MainViewModel`, `ProcessItemViewModel` (presentation logic + `INotifyPropertyChanged`)
- **Views**: XAML files with data binding

**Implementation**: Uses `CommunityToolkit.Mvvm` for source generators:
- `[ObservableProperty]` auto-generates `INotifyPropertyChanged` boilerplate
- `[RelayCommand]` generates `ICommand` implementations

### 4.2 Object Pooling Pattern

**`StringBuilderPool`** (Microsoft.Extensions.ObjectPool):
```csharp
using (var psb = StringBuilderPool.Rent())
{
    psb.Builder.Append("Value");
    string result = psb.Build();
} // Automatic return to pool
```

**Purpose**: Avoid repeated allocations of `StringBuilder` instances in hot paths.

### 4.3 Differential Update Algorithm

**`SyncProcessCollection`** in `MainViewModel`:
- Compares incoming `List<ProcessInfo>` against existing `ObservableCollection<ProcessItemViewModel>`
- Only adds/removes/updates changed items
- Preserves UI state (expansion, selection) across refreshes
- **Benefit**: Prevents UI flickering and reduces WPF rendering overhead

### 4.4 Cache-Aside Pattern

**`ImageLoaderService`** and **`IconCache`**:
- Checks in-memory cache first
- On miss, loads from disk/GDI+ extraction
- Freezes `BitmapSource` for thread-safety
- Stores in `ConcurrentDictionary`

## 5. Memory Management Strategy

### 5.1 Managed Heap Optimization

1. **Dictionary Reuse**: `Dictionary<int, ProcessInfo> activeProcesses` updated in-place
2. **List Capacity Management**: Pre-sized collections (e.g., `new List<ProcessInfo>(64)`)
3. **Avoid LINQ in Hot Paths**: Manual loops prevent enumerator allocations
4. **String Interning**: Cache static strings like PID text representations

### 5.2 Unmanaged Memory

**ProcessService Native Buffer**:
```csharp
private IntPtr buffer = IntPtr.Zero;
private int bufferSize = 1024 * 1024; // Initial 1 MB

// Allocation
buffer = Marshal.AllocHGlobal(bufferSize);

// Dynamic resizing
if (status == StatusInfoLengthMismatch)
{
    Marshal.FreeHGlobal(buffer);
    bufferSize = requiredSize + (1024 * 1024); // +1 MB padding
    buffer = Marshal.AllocHGlobal(bufferSize);
}

// Cleanup in Dispose
Marshal.FreeHGlobal(buffer);
```

### 5.3 Stack Allocation

**Drive Path Construction** (ProcessService):
```csharp
Span<char> drivePath = stackalloc char[4];
drivePath[0] = (char)('A' + i);
drivePath[1] = ':';
drivePath[2] = '\\';
drivePath[3] = '\0';
```
**Benefit**: Zero heap allocations for temporary buffers.

## 6. Performance Characteristics

### 6.1 Measured Latencies

- **Full System Snapshot**: < 5ms (typical), < 10ms (worst-case with 500+ processes)
- **UI Update (Differential Sync)**: < 2ms for 50 process changes
- **Icon Extraction**: 5-15ms per unique executable (cached thereafter)

### 6.2 Memory Footprint

- **Idle**: ~15 MB working set
- **Monitoring 300 Processes**: ~30 MB working set
- **GC Pressure**: Near-zero allocations per refresh cycle after initial warmup

### 6.3 Complexity Analysis

- **`UpdateProcessSnapshot`**: O(N) where N = number of processes
- **`RebuildTreeStructure`**: O(N) parent-child linking
- **`SyncProcessCollection`**: O(M + N) where M = old list size, N = new list size

## 7. Security & Permissions

### 7.1 Privilege Requirements

- **Standard User**: Can view own processes and some system processes
- **Administrator**: Required for:
  - Viewing protected processes (CSRSS, services)
  - Terminating elevated processes
  - Reading command-line arguments of all processes

### 7.2 API Security Constraints

- **`NtQueryInformationProcess`**: Requires `ProcessQueryLimitedInformation` handle rights
- **`OpenProcess`**: Some processes (anti-malware, kernel services) cannot be opened even as Admin

## 8. Extensibility Points

### 8.1 Service Interfaces

- **`IProcessService`**: Implement alternative data sources (remote machines, historical data)
- **`IImageLoaderService`**: Custom icon providers
- **`ILiteDialogService`**: Custom dialog implementations

### 8.2 ViewModel Extension

`ProcessItemViewModel` can be extended with computed properties without modifying `ProcessInfo`:
```csharp
public string FormattedMemory => $"{Info.WorkingSetPrivate / 1024 / 1024:N2} MB";
```

## 9. Technical Constraints

### 9.1 Platform

- **OS**: Windows 10/11 (x64 recommended)
- **Framework**: .NET 9.0 (requires matching runtime)
- **Architecture**: Windows-specific P/Invoke (no cross-platform support)

### 9.2 API Limitations

- **Undocumented APIs**: `NtQuerySystemInformation` structure may change in future Windows versions
- **Kernel Calls**: Require `unsafe` code blocks and `AllowUnsafeBlocks` project setting

## 10. Build Configuration

### 10.1 Release Optimizations

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <Optimize>true</Optimize>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  <DebugType>none</DebugType>
  <DefineConstants>TRACE</DefineConstants>
</PropertyGroup>
```

### 10.2 Required Project Settings

```xml
<PropertyGroup>
  <TargetFramework>net9.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

## 11. Logging & Diagnostics

### 11.1 Serilog Configuration

- **Sinks**: Async file sink (`logs/SystemProcesses-{Date}.log`)
- **Enrichers**: Process ID, Thread ID
- **Minimum Level**: Information (Release), Debug (Debug builds)

### 11.2 Key Log Points

- Service initialization failures (PDH, Native APIs)
- Process enumeration errors
- UI synchronization issues
- Unhandled exceptions

## 12. Future Architecture Considerations

### 12.1 Scalability

- Current design handles up to ~1000 processes efficiently
- For larger systems, consider:
  - Incremental updates (only changed PIDs)
  - Lazy tree expansion (load children on-demand)

### 12.2 Cross-Platform

To support Linux/macOS would require:
- Abstraction layer over `ProcessService`
- Platform-specific implementations using `/proc` (Linux) or `libproc` (macOS)
- Alternative to WPF (Avalonia UI)
