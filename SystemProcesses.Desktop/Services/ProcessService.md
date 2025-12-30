# Technical Documentation: `ProcessService.cs`

## 1. Overview
The `ProcessService` class is the core analytical engine of the application. It is responsible for capturing high-fidelity system snapshots, calculating resource utilization deltas (CPU, I/O), and organizing raw process data into a hierarchical tree structure.

**Primary Purpose:** To provide a thread-safe, low-latency, and memory-efficient stream of `ProcessInfo` objects and `SystemStats` to the UI layer.

**Design Philosophy:** **Zero-Allocation**. The service is architected to minimize Garbage Collection (GC) pressure by reusing objects and buffers across update cycles. It relies heavily on **P/Invoke** (`NativeMethods`) to bypass the performance overhead of the managed `System.Diagnostics` namespace.

---

## 2. Architecture & State Management

The service maintains persistent state to calculate rates of change (deltas) between sampling intervals.

### 2.1 Data Structures
*   **`activeProcesses` (`Dictionary<int, ProcessInfo>`):** The primary cache. Maps Process IDs (PIDs) to reusable `ProcessInfo` instances. This ensures that as long as a process stays alive, its managed object is updated in place rather than reallocated.
*   **`prevProcessStats` (`Dictionary<int, ProcessHistory>`):** Stores the previous snapshot's CPU time and I/O bytes for every PID. Required to calculate "Usage %" and "Bytes/sec".
*   **`servicePids` (`HashSet<int>`):** A fast lookup set to tag processes as Windows Services.
*   **Reusable Buffers:**
    *   `currentPidsBuffer`: Tracks PIDs seen in the current snapshot to identify stopped processes.
    *   `stoppedPidsBuffer`: Used to batch-remove dead processes.
    *   `top5Buffer`: Fixed-size array for sorting top CPU consumers without LINQ allocations.
    *   `driveBuffer`: Reusable array for storage statistics.

### 2.2 Memory Management
*   **Native Buffer:** A large unmanaged memory block (`IntPtr buffer`) allocated via `Marshal.AllocHGlobal`. It resizes dynamically if `NtQuerySystemInformation` returns `StatusInfoLengthMismatch`.
*   **Stack Allocation:** Uses `stackalloc` for small, short-lived buffers (e.g., drive path construction) to avoid heap allocation entirely.

---

## 3. Core Logic Flow (`UpdateProcessSnapshot`)

This method is the "heartbeat" of the service, executing the following sequence:

1.  **Buffer Sizing:** Calls `NtQuerySystemInformation`. If the buffer is too small, it frees the old pointer and allocates a larger block (Request Size + 1MB padding).
2.  **PDH Collection:** Queries the Performance Data Helper for `PhysicalDisk(_Total)\% Idle Time` to calculate global disk activity.
3.  **Global Memory:** Calls `GlobalMemoryStatusEx` to fetch physical RAM and Commit Charge (Virtual Memory) limits.
4.  **Storage Enumeration:**
    *   Calls `GetLogicalDrives` to get a bitmask of active drives.
    *   Iterates bits 0-25 (A-Z).
    *   Uses `stackalloc char[4]` to construct root paths (e.g., `C:\`) without string allocation.
    *   Filters for `DriveTypeFixed` (3) to ensure UI responsiveness.
    *   Calls `GetDiskFreeSpaceExW` for capacity stats.
5.  **Process Iteration (Unsafe Pointer Arithmetic):**
    *   Traverses the `SystemProcessInformation` linked list in the native buffer.
    *   **String Safety:** Marshals `UnicodeString` buffers using `Length / 2` (converting OS Bytes to .NET Chars) to prevent buffer overreads.
    *   **Delta Calculation:**
        *   `CPU %` = `(CurrentKernel + CurrentUser - PrevTotal) / WallClockDelta / ProcessorCount`.
        *   `IO Rate` = `(CurrentTransfers - PrevTransfers) / WallClockDelta`.
    *   **Identity:** Updates existing `ProcessInfo` objects or creates new ones. (Note: Uses Composite Key `PID + CreateTime` logic for robust identity).
6.  **Pruning:** Identifies PIDs present in `activeProcesses` but missing from the current snapshot and removes them.
7.  **Top 5 Calculation:** Performs a single-pass insertion sort into `top5Buffer` to identify resource hogs without sorting the entire list.

---

## 4. Auxiliary Functions

### 4.1 `RefreshServicePids`
*   **Mechanism:** Uses `EnumServicesStatusExW` (advapi32) to map Service IDs to Process IDs.
*   **Optimization:** Accesses the returned raw buffer via pointers to extract `dwProcessId` without marshalling the full `EnumServiceStatusProcess` structures for every service.

### 4.2 `RebuildTreeStructure`
*   **Logic:** Clears the `Children` list of all active processes (keeping the backing array capacity). Re-links parents and children based on `ParentPid`.
*   **Root Nodes:** Processes with `ParentPid == 0` or whose parent is not found in the current snapshot are added to the `Roots` list.

### 4.3 `GetCommandLine`
*   **Mechanism:** Uses `NtQueryInformationProcess` with `ProcessCommandLineInformation` (60).
*   **Constraint:** Requires a handle with `ProcessQueryLimitedInformation` rights. Fails gracefully for protected system processes (e.g., CSRSS, Antivirus).

---

## 5. Thread Safety & Concurrency

*   **Internal State:** The service is **not** thread-safe internally. It relies on `activeProcesses` and `prevProcessStats` being modified by a single thread at a time.
*   **Public API:** The `GetProcessTreeAsync` method wraps the update logic in a `Task.Run` and acquires a `lock (activeProcesses)`. This ensures that:
    1.  The heavy computation runs off the UI thread.
    2.  Concurrent calls (though rare in a timer-based pull model) do not corrupt the state.
*   **Output Safety:** It returns a `List<ProcessInfo>`. While the list instance is reused, the consumer (UI) typically iterates it on the Dispatcher. The service does not modify the list again until the next `GetProcessTreeAsync` call acquires the lock.

---

## 6. Key Performance Metrics

*   **Complexity:** O(N) for snapshot parsing, O(N) for tree reconstruction.
*   **Allocations:** Near-zero per update cycle. Allocations only occur when:
    *   A new process starts (new `ProcessInfo`).
    *   The native buffer needs resizing.
    *   String marshalling (Name/CommandLine) - unavoidable as WPF requires `System.String`.
*   **Latency:** Typically < 5ms for a full system snapshot (depending on hardware), significantly faster than `System.Diagnostics.Process.GetProcesses()`.