# Technical Documentation: `SystemPrimitives.cs`

## 1. Overview
The `SystemPrimitives` class serves as the **Platform Invocation (P/Invoke)** layer for the `SystemProcesses.Desktop` application. It provides direct access to low-level Windows API functions located in `ntdll.dll`, `kernel32.dll`, `advapi32.dll`, `user32.dll`, and `pdh.dll`.

**Primary Purpose:** To bypass the overhead and limitations of the managed `System.Diagnostics` namespace, enabling high-performance, bulk retrieval of system process data, memory statistics, and hardware counters.

**Key Technologies:**
*   **Source-Generated Interop:** Uses `[LibraryImport]` (available in .NET 7+) for compile-time marshalling generation, reducing runtime overhead compared to the traditional `[DllImport]`.
*   **Unsafe Context:** Many structures rely on pointers (`IntPtr`, `void*`) and manual memory management (`Marshal.AllocHGlobal`) to ensure zero-allocation performance during data scraping.

---

## 2. Architecture & Categorization

The class is partitioned into five functional regions:

1.  **Process Information (`ntdll.dll`):** The core engine. Retrieves the entire process tree in a single system call.
2.  **Service Enumeration (`advapi32.dll`):** Identifies which processes are Windows Services.
3.  **Memory Information (`kernel32.dll`):** Global physical and virtual memory statistics.
4.  **Window Management (`user32.dll`):** UI thread window handle retrieval.
5.  **Performance Data Helper (`pdh.dll`):** High-level counters for Disk I/O (specifically Idle Time).
6.  **Disk Information (`kernel32.dll`):** Storage availability and capacity statistics.

---

## 3. API Reference

### 3.1 Process Information (`ntdll.dll`)

These methods interact with the Windows Kernel directly.

#### `NtQuerySystemInformation`
Retrieves a snapshot of system information. This is the critical "hot path" method for the application.

```csharp
public static partial int NtQuerySystemInformation(
    int SystemInformationClass,
    IntPtr SystemInformation,
    int SystemInformationLength,
    out int ReturnLength);
```
*   **SystemInformationClass:** Always `5` (`SystemProcessInformation`) in this context.
*   **SystemInformation:** Pointer to a buffer allocated via `Marshal.AllocHGlobal`.
*   **Return Value:** Returns `NTSTATUS`.
    *   `0x00000000` (`StatusSuccess`): Data retrieved successfully.
    *   `0xC0000004` (`StatusInfoLengthMismatch`): Buffer too small. The application must resize the buffer based on `ReturnLength` and retry.

#### `NtQueryInformationProcess`
Retrieves specific attributes of a single process handle, specifically used here to fetch Command Line arguments.

```csharp
public static partial int NtQueryInformationProcess(
    IntPtr ProcessHandle,
    int ProcessInformationClass,
    IntPtr ProcessInformation,
    int ProcessInformationLength,
    out int ReturnLength);
```
*   **ProcessInformationClass:** Uses `60` (`ProcessCommandLineInformation`).
*   **Note:** Requires a handle opened with `ProcessQueryLimitedInformation` rights.

---

### 3.2 Service Enumeration (`advapi32.dll`)

Used to map Process IDs (PIDs) to Windows Services.

#### `EnumServicesStatusExW`
Enumerates services in the Service Control Manager database.

*   **Pattern:** This method follows the "Double Call" pattern:
    1.  Call with `cbBufSize = 0`.
    2.  Check `pcbBytesNeeded`.
    3.  Allocate buffer.
    4.  Call again with the allocated buffer.
*   **StringMarshalling:** Explicitly set to `Utf16` to handle Unicode service names correctly.

---

### 3.3 Performance Data Helper (`pdh.dll`)

Used to fetch "PhysicalDisk(_Total)\% Idle Time" because calculating disk activity per process via `NtQuerySystemInformation` is complex and requires delta calculations on IO counters.

#### `PdhOpenQuery` / `PdhCloseQuery`
Manages the lifecycle of a PDH query session.

#### `PdhAddEnglishCounter`
Adds a counter to the query using locale-independent English names.
*   **Why English?** Using `PdhAddCounter` (localized) would fail on non-English Windows installations if the string "PhysicalDisk" was translated.

#### `PdhGetFormattedCounterValue`
Retrieves the calculated value (e.g., a double representing percentage).
*   **Format:** `PdhFmtDouble` (0x00000200) is used to get a `double` result.

### 3.4 Disk Information (`kernel32.dll`)

Used to retrieve storage statistics without the overhead of `System.IO.DriveInfo`.

#### `GetLogicalDrives`
Retrieves a bitmask representing the currently available disk drives.
*   **Return Value:** A `uint` bitmask where bit 0 = A:, bit 1 = B:, etc.

#### `GetDriveTypeW`
Determines the drive type (Fixed, Removable, Network, etc.) for a specific root path.
*   **Input:** Requires a pointer to a null-terminated string (e.g., `C:\`).
*   **Usage:** Filter for `DRIVE_FIXED` (3) to avoid blocking on network or removable drives.

#### `GetDiskFreeSpaceExW`
Retrieves storage capacity and free space.
*   **Parameters:** Uses `out ulong` for 64-bit size values, avoiding the 2GB limit of older APIs.
*   **Safety:** Returns `bool` (non-zero) on success.

---

## 4. Structure Layouts & Marshalling

Correct structure definition is critical for preventing memory corruption.

### 4.1 `SystemProcessInformation`
This struct represents a single node in the linked list returned by `NtQuerySystemInformation`.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct SystemProcessInformation
{
    public uint NextEntryOffset; // 0 if last item, otherwise bytes to next item
    public uint NumberOfThreads;
    // ... (64-bit aligned fields)
    public UnicodeString ImageName; // The process name
    // ...
    public IntPtr UniqueProcessId; // PID
    // ...
}
```
*   **Alignment:** The struct is naturally aligned for x64 (8-byte boundaries).
*   **NextEntryOffset:** Crucial for iterating the buffer. The loop logic is: `ptr = (byte*)ptr + ptr->NextEntryOffset`.

### 4.2 `UnicodeString`
The native Windows string structure.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct UnicodeString
{
    public ushort Length;        // Length in BYTES, not characters
    public ushort MaximumLength; // Max capacity in BYTES
    public IntPtr Buffer;        // Pointer to the UTF-16 string data
}
```
*   **Safety Warning:** The `Buffer` is **not** guaranteed to be null-terminated.
*   **Marshalling:** When converting to C# `string`, you **must** use `Marshal.PtrToStringUni(Buffer, Length / 2)`. Dividing by 2 is required because `Length` is bytes, but .NET expects a character count.

### 4.3 `PdhFmtCountervalue`
Uses an **Explicit Layout** to simulate a C-style Union.

```csharp
[StructLayout(LayoutKind.Explicit)]
public struct PdhFmtCountervalue
{
    [FieldOffset(0)] public uint CStatus;      // Data validity status
    [FieldOffset(8)] public double doubleValue; // Value if format was Double
    [FieldOffset(8)] public long longValue;     // Value if format was Long
}
```
*   **Union:** `doubleValue` and `longValue` occupy the same memory address (offset 8). Reading one interprets the bits of the other.

---

## 5. Best Practices & Safety Considerations

1.  **Handle Lifecycle:**
    *   Any `IntPtr` returned by `OpenProcess` or `OpenSCManagerW` **must** be closed using `CloseHandle` or `CloseServiceHandle`. Failure to do so leaks kernel handles.

2.  **Buffer Management:**
    *   `NtQuerySystemInformation` returns a snapshot size that changes dynamically. The calling code implements a retry loop: if `StatusInfoLengthMismatch` is returned, free the old buffer, allocate a larger one (suggested +1MB padding), and retry.

3.  **Thread Safety:**
    *   `SystemPrimitives` functions are stateless and thread-safe.
    *   However, the *buffers* passed to them are not. The calling service (`ProcessService`) must synchronize access to the shared buffer `IntPtr`.

4.  **Architecture Compatibility:**
    *   The structs use `IntPtr` and `UIntPtr` for pointer-sized fields (PIDs, memory addresses). This ensures the code runs correctly on both x86 (32-bit) and x64 (64-bit) processes.

5.  **Error Handling:**
    *   Most methods return `int` (NTSTATUS) or `bool`. The calling code must check these results.
    *   `[SetLastError = true]` is used on kernel32/advapi32 calls, allowing `Marshal.GetLastPInvokeError()` to retrieve failure details if needed.