# API Reference Documentation

This document provides reference documentation for the Windows Native APIs and project interfaces used in SystemProcesses.

---

## Table of Contents

1. [Windows Native APIs](#windows-native-apis)
2. [Project Service Interfaces](#project-service-interfaces)
3. [Data Structures](#data-structures)
4. [Helper Classes](#helper-classes)

---

## Windows Native APIs

### ntdll.dll - NT Native API

#### NtQuerySystemInformation

**Purpose**: Retrieves system-wide information about processes, threads, handles, and performance.

**Signature**:
```csharp
[LibraryImport("ntdll.dll")]
internal static partial int NtQuerySystemInformation(
    int SystemInformationClass,
    IntPtr SystemInformation,
    int SystemInformationLength,
    out int ReturnLength);
```

**Parameters**:
- `SystemInformationClass`: Information class to query. Use `5` for `SystemProcessInformation`.
- `SystemInformation`: Pointer to buffer receiving the data.
- `SystemInformationLength`: Size of the buffer in bytes.
- `ReturnLength`: Receives the actual size needed or written.

**Return Values**:
- `0` (StatusSuccess): Operation succeeded
- `0xC0000004` (StatusInfoLengthMismatch): Buffer too small, check `ReturnLength`
- `0xC0000005` (StatusAccessViolation): Invalid buffer pointer

**Usage Pattern**:
```csharp
int bufferSize = 1024 * 1024; // Start with 1 MB
IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

int status = NtQuerySystemInformation(5, buffer, bufferSize, out int returnLength);

if (status == 0xC0000004) // StatusInfoLengthMismatch
{
    Marshal.FreeHGlobal(buffer);
    bufferSize = returnLength + (1024 * 1024); // Add 1 MB padding
    buffer = Marshal.AllocHGlobal(bufferSize);
    status = NtQuerySystemInformation(5, buffer, bufferSize, out returnLength);
}

if (status == 0) // Success
{
    // Parse buffer
}
```

**Important Notes**:
- This is an undocumented API; structures may change between Windows versions
- Requires no special privileges for basic process enumeration
- Returns ALL processes in a single call (atomic snapshot)
- Buffer format: Linked list of `SystemProcessInformation` structures

---

#### NtQueryInformationProcess

**Purpose**: Retrieves information about a specific process.

**Signature**:
```csharp
[LibraryImport("ntdll.dll")]
internal static partial int NtQueryInformationProcess(
    IntPtr ProcessHandle,
    int ProcessInformationClass,
    IntPtr ProcessInformation,
    int ProcessInformationLength,
    out int ReturnLength);
```

**Parameters**:
- `ProcessHandle`: Handle to the process (from `OpenProcess`)
- `ProcessInformationClass`: Type of information:
  - `0`: ProcessBasicInformation
  - `27`: ProcessCommandLineInformation
  - `60`: ProcessCommandLineInformation (alternative)
- `ProcessInformation`: Buffer for the data
- `ProcessInformationLength`: Buffer size
- `ReturnLength`: Actual size needed/written

**Common Use Case - Get Command Line**:
```csharp
IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
if (handle == IntPtr.Zero) return null;

try
{
    // First call to get required size
    NtQueryInformationProcess(handle, 60, IntPtr.Zero, 0, out int requiredSize);
    
    // Allocate buffer
    IntPtr buffer = Marshal.AllocHGlobal(requiredSize);
    try
    {
        int status = NtQueryInformationProcess(handle, 60, buffer, requiredSize, out _);
        if (status == 0)
        {
            var unicode = Marshal.PtrToStructure<UnicodeString>(buffer);
            string commandLine = Marshal.PtrToStringUni(unicode.Buffer, unicode.Length / 2);
            return commandLine;
        }
    }
    finally
    {
        Marshal.FreeHGlobal(buffer);
    }
}
finally
{
    CloseHandle(handle);
}
```

**Privileges Required**: Handle must have `ProcessQueryLimitedInformation` access rights.

---

### kernel32.dll - Windows Kernel API

#### OpenProcess

**Purpose**: Opens an existing process object.

**Signature**:
```csharp
[LibraryImport("kernel32.dll", SetLastError = true)]
internal static partial IntPtr OpenProcess(
    int dwDesiredAccess,
    [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
    int dwProcessId);
```

**Access Rights Constants**:
```csharp
private const int ProcessQueryInformation = 0x0400;
private const int ProcessQueryLimitedInformation = 0x1000;
private const int ProcessTerminate = 0x0001;
private const int ProcessVmRead = 0x0010;
```

**Return Value**:
- Valid handle: Non-zero `IntPtr`
- Failure: `IntPtr.Zero` (call `Marshal.GetLastWin32Error()`)

**Usage**:
```csharp
IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
if (handle == IntPtr.Zero)
{
    int error = Marshal.GetLastWin32Error();
    Log.Warning("Failed to open process {Pid}, error {Error}", pid, error);
    return;
}

try
{
    // Use handle
}
finally
{
    CloseHandle(handle);
}
```

**Important**: Always close handles with `CloseHandle` or use `SafeHandle`.

---

#### CloseHandle

**Purpose**: Closes an open object handle.

**Signature**:
```csharp
[LibraryImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static partial bool CloseHandle(IntPtr hObject);
```

**Usage**:
```csharp
if (!CloseHandle(handle))
{
    int error = Marshal.GetLastWin32Error();
    Log.Error("Failed to close handle, error {Error}", error);
}
```

---

#### GlobalMemoryStatusEx

**Purpose**: Retrieves physical and virtual memory statistics.

**Signature**:
```csharp
[LibraryImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

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
```

**Usage**:
```csharp
var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
if (GlobalMemoryStatusEx(ref memStatus))
{
    long totalRam = (long)memStatus.ullTotalPhys;
    long availRam = (long)memStatus.ullAvailPhys;
    int memoryLoadPercent = (int)memStatus.dwMemoryLoad;
}
```

---

#### GetLogicalDrives

**Purpose**: Returns a bitmask representing available drive letters.

**Signature**:
```csharp
[LibraryImport("kernel32.dll")]
internal static partial uint GetLogicalDrives();
```

**Usage**:
```csharp
uint drives = GetLogicalDrives();
for (int i = 0; i < 26; i++) // A-Z
{
    if ((drives & (1 << i)) != 0)
    {
        char driveLetter = (char)('A' + i);
        // Drive exists
    }
}
```

---

#### GetDiskFreeSpaceExW

**Purpose**: Retrieves disk space information.

**Signature**:
```csharp
[LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static partial bool GetDiskFreeSpaceExW(
    string lpDirectoryName,
    out ulong lpFreeBytesAvailable,
    out ulong lpTotalNumberOfBytes,
    out ulong lpTotalNumberOfFreeBytes);
```

**Usage**:
```csharp
if (GetDiskFreeSpaceExW("C:\\", out ulong freeBytes, out ulong totalBytes, out _))
{
    long freeMB = (long)(freeBytes / 1024 / 1024);
    long totalMB = (long)(totalBytes / 1024 / 1024);
    int freePercent = (int)(freeBytes * 100 / totalBytes);
}
```

---

#### GetDriveTypeW

**Purpose**: Determines drive type (fixed, removable, network, etc.).

**Signature**:
```csharp
[LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
internal static partial uint GetDriveTypeW(string lpRootPathName);
```

**Return Values**:
- `0`: DriveTypeUnknown
- `1`: DriveTypeNoRootDir
- `2`: DriveTypeRemovable
- `3`: DriveTypeFixed (what we want)
- `4`: DriveTypeRemote
- `5`: DriveTypeCdrom
- `6`: DriveTypeRamdisk

**Usage**:
```csharp
uint driveType = GetDriveTypeW("C:\\");
if (driveType == 3) // DriveTypeFixed
{
    // Process fixed disk
}
```

---

### user32.dll - User Interface API

#### SetWindowPos

**Purpose**: Changes window size, position, and z-order.

**Signature**: See `SystemPrimitives.cs` for complete definition using `LibraryImport`.

**Parameters**:
- `hWnd`: Window handle
- `hWndInsertAfter`: Z-order position (use `HwndTopMost` = -1 for always-on-top)
- `X, Y, cx, cy`: Position and size
- `uFlags`: Combination of Swp_* flags (NOMOVE, NOSIZE, NOACTIVATE, etc.)

**Return Value**: True on success, false on failure.

**Usage Context**: Used by StatsView for message-driven topmost enforcement. Called with `SwpNOMOVE | SwpNOSIZE | SwpNoactivate` to change only z-order without affecting position or stealing focus.

**Performance**: ~50μs per call

---

#### IsWindow

**Purpose**: Validates if a window handle identifies an existing window.

**Signature**: See `SystemPrimitives.cs` for complete definition.

**Return Value**: True if window exists, false otherwise.

**Usage Context**: Used to validate handles before `SetWindowPos` calls, preventing failures during window teardown. Adds ~5μs per call but eliminates benign error logging.

---

### advapi32.dll - Advanced Windows API

#### EnumServicesStatusExW

**Purpose**: Enumerates Windows services and their process IDs.

**Signature**:
```csharp
[LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static partial bool EnumServicesStatusExW(
    IntPtr hSCManager,
    int InfoLevel,
    int dwServiceType,
    int dwServiceState,
    IntPtr lpServices,
    int cbBufSize,
    out int pcbBytesNeeded,
    out int lpServicesReturned,
    ref int lpResumeHandle,
    string pszGroupName);
```

**Usage Pattern**:
```csharp
IntPtr scm = OpenSCManager(null, null, SCManagerEnumerateService);
if (scm == IntPtr.Zero) return;

try
{
    // Get required buffer size
    EnumServicesStatusExW(scm, 0, ServiceWin32, ServiceStateAll, 
        IntPtr.Zero, 0, out int bytesNeeded, out _, ref resumeHandle, null);
    
    // Allocate buffer
    IntPtr buffer = Marshal.AllocHGlobal(bytesNeeded);
    try
    {
        if (EnumServicesStatusExW(scm, 0, ServiceWin32, ServiceStateAll,
            buffer, bytesNeeded, out _, out int servicesReturned, ref resumeHandle, null))
        {
            unsafe
            {
                byte* ptr = (byte*)buffer;
                for (int i = 0; i < servicesReturned; i++)
                {
                    int pid = *(int*)(ptr + 16); // ProcessId offset
                    servicePids.Add(pid);
                    ptr += 40; // Size of EnumServiceStatusProcess
                }
            }
        }
    }
    finally
    {
        Marshal.FreeHGlobal(buffer);
    }
}
finally
{
    CloseServiceHandle(scm);
}
```

---

### pdh.dll - Performance Data Helper

#### PdhOpenQuery

**Purpose**: Creates a query for collecting performance counter data.

**Signature**:
```csharp
[LibraryImport("pdh.dll")]
internal static partial int PdhOpenQuery(
    IntPtr szDataSource,
    IntPtr dwUserData,
    out IntPtr phQuery);
```

**Return**: `0` on success, error code otherwise.

---

#### PdhAddEnglishCounter

**Purpose**: Adds a performance counter to a query using English counter names.

**Signature**:
```csharp
[LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
internal static partial int PdhAddEnglishCounter(
    IntPtr hQuery,
    string szFullCounterPath,
    IntPtr dwUserData,
    out IntPtr phCounter);
```

**Common Counters**:
- `"\\PhysicalDisk(_Total)\\% Idle Time"` - Disk idle percentage
- `"\\Processor(_Total)\\% Processor Time"` - CPU usage
- `"\\Memory\\Available MBytes"` - Available memory

---

#### PdhCollectQueryData

**Purpose**: Collects current data for all counters in a query.

**Signature**:
```csharp
[LibraryImport("pdh.dll")]
internal static partial int PdhCollectQueryData(IntPtr hQuery);
```

**Important**: Must be called at least twice; first call initializes counters.

---

#### PdhGetFormattedCounterValue

**Purpose**: Retrieves formatted counter value.

**Signature**:
```csharp
[LibraryImport("pdh.dll")]
internal static partial int PdhGetFormattedCounterValue(
    IntPtr hCounter,
    uint dwFormat,
    out uint lpdwType,
    out PdhFmtCountervalue pValue);

[StructLayout(LayoutKind.Explicit)]
internal struct PdhFmtCountervalue
{
    [FieldOffset(0)] public uint CStatus;
    [FieldOffset(8)] public long longValue;
    [FieldOffset(8)] public double doubleValue;
}
```

**Format Constants**:
- `0x00000200`: PdhFmtDouble
- `0x00000400`: PdhFmtLong

**Usage**:
```csharp
PdhCollectQueryData(pdhQuery);
int status = PdhGetFormattedCounterValue(pdhDiskIdleCounter, 0x00000200, 
    out _, out var value);
if (status == 0)
{
    double diskIdle = value.doubleValue;
    double diskActive = 100.0 - diskIdle;
}
```

---

## Project Service Interfaces

### IProcessService

**Purpose**: Contract for process enumeration and monitoring services.

**Location**: `Services/IProcessService.cs`

```csharp
public interface IProcessService
{
    /// <summary>
    /// Retrieves the current process tree and system statistics.
    /// </summary>
    /// <returns>
    /// A tuple containing:
    /// - List of root processes (processes without parents or orphaned)
    /// - SystemStats with CPU, memory, disk usage
    /// </returns>
    Task<(List<ProcessInfo> Roots, SystemStats Stats)> GetProcessTreeAsync();
    
    /// <summary>
    /// Gets a specific process by PID.
    /// </summary>
    ProcessInfo? GetProcess(int pid);
    
    /// <summary>
    /// Refreshes service PID cache (Windows services).
    /// </summary>
    void RefreshServicePids();
}
```

**Implementation**: `ProcessService` in `Services/ProcessService.cs`

**Thread Safety**: Methods are thread-safe via internal locking.

---

### IImageLoaderService

**Purpose**: Asynchronous, cached image loading for process icons.

**Location**: `Services/ImageLoaderService.cs`

```csharp
public interface IImageLoaderService
{
    /// <summary>
    /// Loads an image asynchronously with caching.
    /// </summary>
    /// <param name="imagePath">Full path to executable or image file</param>
    /// <returns>BitmapSource (frozen for thread-safety) or null if load fails</returns>
    Task<BitmapSource?> LoadImageAsync(string imagePath);
    
    /// <summary>
    /// Clears the image cache.
    /// </summary>
    void ClearCache();
    
    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    (int Count, long SizeBytes) GetCacheStats();
}
```

**Implementation Details**:
- Uses `ConcurrentDictionary` for thread-safe caching
- Extracts icons from executables via `IconCache`
- Always returns frozen `BitmapSource` for cross-thread use
- Falls back to default icon on failure

---

### ILiteDialogService

**Purpose**: Minimal, zero-XAML dialog service.

**Location**: `Helpers/LiteDialog.cs`

```csharp
public interface ILiteDialogService
{
    /// <summary>
    /// Shows a simple message dialog.
    /// </summary>
    void ShowMessage(string title, string message, Window? owner = null);
    
    /// <summary>
    /// Shows a confirmation dialog.
    /// </summary>
    /// <returns>True if user clicked Yes/OK, false otherwise</returns>
    bool ShowConfirmation(string title, string message, Window? owner = null);
    
    /// <summary>
    /// Shows an error dialog.
    /// </summary>
    void ShowError(string title, string message, Exception? exception = null, Window? owner = null);
}
```

**Implementation**: `LiteDialogService` uses code-only `Window` creation (no XAML).

---

## Data Structures

### ProcessInfo

**Purpose**: Core data model for a single process.

**Location**: `Models/ProcessInfo.cs`

```csharp
public class ProcessInfo
{
    // Identity
    public int Pid { get; set; }
    public int ParentPid { get; set; }
    public DateTime CreateTime { get; set; }
    
    // Metadata
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    
    // Resource Usage
    public double CpuUsage { get; set; }           // Percentage (0-100)
    public long WorkingSetPrivate { get; set; }    // Bytes
    public long VirtualMemorySize { get; set; }    // Bytes
    public long IoReadBytes { get; set; }
    public long IoWriteBytes { get; set; }
    
    // Counts
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    
    // Flags
    public bool IsService { get; set; }
    public bool IsSystemProcess { get; set; }
    
    // Hierarchy
    public List<ProcessInfo> Children { get; set; } = new();
}
```

**Important Notes**:
- `ProcessInfo` instances are **reused** across refresh cycles
- Use `(Pid, CreateTime)` as composite key for identity
- `Children` list is cleared and rebuilt on each tree reconstruction
- Property changes are made in-place to avoid allocations

---

### SystemStats

**Purpose**: Aggregated system-wide statistics.

**Location**: `Models/ProcessInfo.cs` (nested struct)

```csharp
public struct SystemStats
{
    // CPU
    public double CpuUsagePercent { get; set; }
    
    // Memory
    public long TotalPhysicalMemory { get; set; }
    public long AvailablePhysicalMemory { get; set; }
    public int MemoryUsagePercent { get; set; }
    
    // Virtual Memory (Commit Charge)
    public long TotalCommitLimit { get; set; }
    public long AvailableCommit { get; set; }
    public int CommitUsagePercent { get; set; }
    
    // Disk
    public double DiskActivePercent { get; set; }
    
    // Counts
    public int ProcessCount { get; set; }
    public int ThreadCount { get; set; }
    public long HandleCount { get; set; }
    
    // Storage
    public DriveStats[] Drives { get; set; }
}
```

---

### DriveStats

**Purpose**: Per-drive storage statistics.

```csharp
public struct DriveStats
{
    public char DriveLetter { get; set; }        // 'C', 'D', etc.
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public int FreePercent { get; set; }
}
```

---

### SystemProcessInformation (Native Structure)

**Purpose**: Kernel structure returned by `NtQuerySystemInformation`.

**Layout** (simplified):
```csharp
// This is NOT defined in code; we use pointer arithmetic instead
unsafe struct SystemProcessInformation
{
    uint NextEntryOffset;        // Offset +0: Pointer to next entry (0 = last)
    uint NumberOfThreads;         // Offset +4
    byte[48] Reserved1;           // Offset +8
    UnicodeString ImageName;     // Offset +56
    int BasePriority;             // Offset +68
    IntPtr UniqueProcessId;       // Offset +72 (PID)
    IntPtr InheritedFromUniqueProcessId; // Offset +80 (Parent PID)
    // ... many more fields
    ulong KernelTime;             // Kernel mode time
    ulong UserTime;               // User mode time
    ulong ReadTransferCount;      // I/O read bytes
    ulong WriteTransferCount;     // I/O write bytes
}
```

**Parsing Pattern**:
```csharp
unsafe
{
    byte* current = (byte*)buffer;
    
    while (true)
    {
        uint nextOffset = *(uint*)current;
        
        // Read PID (offset 72 for x64, 68 for x86)
        int pid = IntPtr.Size == 8 
            ? *(int*)(current + 72) 
            : *(int*)(current + 68);
        
        // Read Parent PID
        int parentPid = IntPtr.Size == 8
            ? *(int*)(current + 80)
            : *(int*)(current + 72);
        
        // Parse UnicodeString at offset 56
        var imageNamePtr = (UnicodeString*)(current + 56);
        ushort length = imageNamePtr->Length;
        string name = Marshal.PtrToStringUni(imageNamePtr->Buffer, length / 2);
        
        // More fields...
        
        if (nextOffset == 0) break; // Last entry
        current += nextOffset;
    }
}
```

---

### UnicodeString (Native Structure)

**Purpose**: Windows kernel string representation.

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct UnicodeString
{
    public ushort Length;        // Length in bytes (NOT characters)
    public ushort MaximumLength;
    public IntPtr Buffer;        // Pointer to UTF-16 string
}
```

**Safe Marshalling**:
```csharp
string ConvertUnicodeString(UnicodeString str)
{
    if (str.Buffer == IntPtr.Zero || str.Length == 0)
        return string.Empty;
    
    // Length is in bytes, divide by 2 for character count
    int charCount = str.Length / 2;
    return Marshal.PtrToStringUni(str.Buffer, charCount);
}
```

---

## Helper Classes

### StringBuilderPool

**Purpose**: Object pooling for `StringBuilder` instances.

**Location**: `Helpers/StringBuilderPool.cs`

**Usage**:
```csharp
// Rent a builder
using (var psb = StringBuilderPool.Rent())
{
    psb.Builder.Append("CPU: ");
    psb.Builder.Append(cpuUsage.ToString("F2"));
    psb.Builder.Append('%');
    
    string result = psb.Build();
} // Automatically returned to pool on dispose

// Pre-initialize with text
using (var psb = StringBuilderPool.Rent("Prefix: "))
{
    psb.Builder.Append(value);
    return psb.Build();
}
```

**Configuration**:
- Default Capacity: 256 characters
- Max Retained Builders: 32 per thread
- Max Builder Capacity: 65,536 characters (larger builders are discarded)

---

### IconCache

**Purpose**: Extracts and caches icons from executable files.

**Location**: `Services/IconCache.cs`

**Key Methods**:
```csharp
public static class IconCache
{
    /// <summary>
    /// Extracts icon from executable and converts to BitmapSource.
    /// </summary>
    public static BitmapSource? ExtractIcon(string executablePath);
}
```

**Implementation Notes**:
- Uses GDI+ (`System.Drawing.Icon.ExtractAssociatedIcon`)
- Converts to WPF `BitmapSource` via `Imaging.CreateBitmapSourceFromHIcon`
- Freezes result for thread-safety
- Returns null on failure (protected files, non-executables)

---

## Performance Characteristics

### API Call Latencies (Typical)

| API | Latency | Notes |
|-----|---------|-------|
| `NtQuerySystemInformation` | 3-8 ms | Depends on process count |
| `OpenProcess` | 0.01 ms | Very fast |
| `NtQueryInformationProcess` | 0.05 ms | Per process |
| `GlobalMemoryStatusEx` | 0.02 ms | Cached by kernel |
| `GetDiskFreeSpaceExW` | 1-5 ms | Disk I/O dependent |
| `EnumServicesStatusExW` | 5-20 ms | Service count dependent |
| `PdhCollectQueryData` | 0.5 ms | First call slower |

### Memory Allocations

| Operation | Allocations | Notes |
|-----------|-------------|-------|
| Full snapshot (300 processes) | <10 KB | After warmup |
| New process detected | ~2 KB | `ProcessInfo` + strings |
| Tree reconstruction | 0 bytes | In-place updates |
| Icon extraction (cache miss) | ~50 KB | GDI+ overhead |
| String formatting (pooled) | 0 bytes | Reused builders |

---

## Error Handling Patterns

### P/Invoke Validation

```csharp
// Always check return values
IntPtr handle = OpenProcess(rights, false, pid);
if (handle == IntPtr.Zero)
{
    int error = Marshal.GetLastWin32Error();
    
    // Handle specific errors
    if (error == 5) // ErrorAccessDenied
    {
        Log.Debug("Access denied for PID {Pid}", pid);
        return null;
    }
    
    Log.Error("Failed to open process {Pid}, error {Error}", pid, error);
    return null;
}
```

### Safe Disposal Pattern

```csharp
IntPtr handle = IntPtr.Zero;
try
{
    handle = OpenProcess(rights, false, pid);
    if (handle == IntPtr.Zero) return;
    
    // Use handle
}
catch (Exception ex)
{
    Log.Error(ex, "Error processing PID {Pid}", pid);
}
finally
{
    if (handle != IntPtr.Zero)
    {
        CloseHandle(handle);
    }
}
```

---

## Additional Resources

### Official Documentation
- **Windows API Index**: https://learn.microsoft.com/en-us/windows/win32/api/
- **Native API Reference**: https://ntdoc.m417z.com/
- **Process and Thread Functions**: https://learn.microsoft.com/en-us/windows/win32/procthread/

### Community Resources
- **Process Hacker Source**: https://github.com/processhacker/processhacker
- **System Informer**: https://systeminformer.sourceforge.io/
- **pinvoke.net**: http://www.pinvoke.net/

### Performance Tools
- **BenchmarkDotNet**: https://benchmarkdotnet.org/
- **PerfView**: https://github.com/microsoft/perfview
- **dotMemory**: https://www.jetbrains.com/dotmemory/