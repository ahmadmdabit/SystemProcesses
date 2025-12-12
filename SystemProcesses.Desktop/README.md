<p align="center">
  <a href="#" target="_blank">
    <img src="Resources/Images/AppIcon/SystemProcess.png" width="200" alt="Project Logo">
  </a>
</p>

# SystemProcesses

**A high-performance, zero-allocation system monitor built with .NET 9 and WPF.**

![Platform](https://img.shields.io/badge/platform-Windows-blue) ![Framework](https://img.shields.io/badge/.NET-9.0-purple) ![License](https://img.shields.io/badge/license-MIT-green)

`SystemProcesses` is a lightweight Task Manager alternative engineered for minimal resource usage. Unlike standard tools that rely on the heavy `System.Diagnostics.Process` API, this project interacts directly with the Windows Kernel (`ntdll.dll`) to scrape system data with near-zero garbage collection overhead.

## 🚀 Key Features

*   **Extreme Performance:** Uses `NtQuerySystemInformation` to fetch the entire process tree in a single system call (< 5ms latency).
*   **Zero-Allocation Architecture:**
    *   Reuses `ProcessInfo` objects and internal buffers across update cycles.
    *   Uses `stackalloc` and `Unsafe` pointer arithmetic for parsing kernel structures.
    *   Implements `StringBuilderPool` for string formatting.
*   **Optimized UI Rendering:**
    *   Custom `SyncProcessCollection` algorithm updates WPF ViewModels in-place to prevent UI flickering and object churn.
    *   Virtualized `TreeView` handles thousands of nodes efficiently.
*   **Resource Efficiency:**
    *   **Icons:** Extracted once, frozen, and cached using `ImageLoaderService`.
    *   **Strings:** `PidText` and other static strings are cached to avoid boxing.
*   **Modern Stack:** Built on .NET 9, utilizing `LibraryImport`, `Span<T>`, and the MVVM Toolkit.

## 🛠 Architecture & Optimizations

This project demonstrates advanced .NET systems programming techniques:

### 1. Kernel Interop (`SystemPrimitives.cs`)
Instead of the slow `System.Diagnostics` API, we use **P/Invoke** to call undocumented Windows APIs.
*   **`NtQuerySystemInformation`**: Retrieves a raw memory block containing all process data.
*   **`EnumServicesStatusExW`**: Maps Service IDs to PIDs directly from the Service Control Manager.
*   **`PdhAddEnglishCounterW`**: Reads "PhysicalDisk(_Total)\% Idle Time" for accurate I/O stats.

### 2. Memory Management (`ProcessService.cs`)
*   **Manual Buffering:** Allocates unmanaged memory (`Marshal.AllocHGlobal`) for kernel data, resizing only when necessary.
*   **Pointer Arithmetic:** Iterates over the raw byte stream using `unsafe` pointers to extract data without marshalling full structures.
*   **Struct Reuse:** The `activeProcesses` dictionary updates existing instances. New allocations only occur when a *new* process starts.

### 3. Thread-Safe UI (`MainViewModel.cs`)
*   **Producer-Consumer:** The `ProcessService` runs on a background thread.
*   **Synchronization:** A `SemaphoreSlim` ensures only one refresh cycle runs at a time.
*   **Differential Updates:** The UI layer compares the new data snapshot against the existing `ObservableCollection`, adding/removing/updating only what changed.

## 📋 Requirements

*   **OS:** Windows 10 / 11 (x64 recommended)
*   **Runtime:** .NET 9.0 Runtime
*   **Rights:** Administrator privileges are recommended for full visibility (e.g., viewing details of System processes).

## ⚡ Quick Start

### Build from Source

```bash
# 1. Clone the repository
git clone https://github.com/ahmadmdabit/SystemProcesses.git
cd SystemProcesses

# 2. Restore dependencies
dotnet restore

# 3. Build in Release mode (Recommended for performance)
dotnet build -c Release

# 4. Run
dotnet run --project SystemProcesses.Desktop -c Release
```

### Configuration

The application is designed to work out-of-the-box.
*   **Refresh Rate:** Adjustable via the UI toolbar (Default: 1s).
*   **Logging:** Logs are written to `logs/SystemProcesses-.log` using Serilog (Async/File sink).

## 📖 User Guide

For detailed usage instructions, feature breakdowns, and troubleshooting, please refer to the [User Guide](UserGuide.md).

## 📂 Project Structure

*   **`Services/`**
    *   `ProcessService.cs`: Core engine. Handles P/Invoke and data parsing.
    *   `SystemPrimitives.cs`: Native API definitions (`[LibraryImport]`).
    *   `ImageLoaderService.cs`: Async, cached, thread-safe image loading.
    *   `IconCache.cs`: GDI+ icon extraction and freezing.
*   **`ViewModels/`**
    *   `MainViewModel.cs`: UI orchestration and state management.
    *   `ProcessItemViewModel.cs`: Lightweight wrapper for `ProcessInfo`.
*   **`Helpers/`**
    *   `StringBuilderPool.cs`: Object pool for string construction.
    *   `LiteDialog.cs`: Zero-XAML, code-only dialogs for minimal overhead.

## 🔍 Technical Deep Dive: The "Zero-Alloc" Loop

The core update loop in `ProcessService.UpdateProcessSnapshot` follows this pattern to ensure minimal GC pressure:

1.  **Query:** Call `NtQuerySystemInformation` into a pre-allocated `IntPtr` buffer.
2.  **Iterate:** Use `byte*` pointers to traverse the linked list of `SystemProcessInformation` structures.
3.  **Lookup:** Check `Dictionary<int, ProcessInfo>` for the PID.
    *   **Found:** Update properties (CPU, Mem, IO) on the *existing* object.
    *   **Not Found:** Allocate *one* new `ProcessInfo` and add to dictionary.
4.  **Prune:** Use a pooled `HashSet<int>` to track seen PIDs. Remove any PIDs in the dictionary that weren't seen in the current snapshot.
5.  **Result:** A list of updated objects is returned. No new lists or wrapper objects are created for existing processes.

## 🤝 Contributing

Contributions are welcome! Please ensure any PRs maintaining the **Zero-Allocation** philosophy:
*   Avoid LINQ in hot paths (refresh loops).
*   Use `StringBuilderPool` for string concatenation.
*   Profile memory usage before submitting.

## 📄 License

Licensed under the MIT License. See [LICENSE](LICENSE) for details.