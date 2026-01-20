# Glossary of Terms

This document defines project-specific terminology, acronyms, and concepts used throughout the SystemProcesses project.

---

## Table of Contents

- [A](#a) | [B](#b) | [C](#c) | [D](#d) | [E](#e) | [F](#f) | [G](#g) | [H](#h) | [I](#i) | [J](#j) | [K](#k) | [L](#l) | [M](#m)
- [N](#n) | [O](#o) | [P](#p) | [Q](#q) | [R](#r) | [S](#s) | [T](#t) | [U](#u) | [V](#v) | [W](#w) | [X](#x) | [Y](#y) | [Z](#z)

---

## A

### Active Process
A process currently tracked in the application's internal cache (`activeProcesses` dictionary). Distinguished from "dead processes" that have terminated but not yet been pruned.

### Allocation
Memory allocation on the managed heap. The project aims to minimize allocations to reduce Garbage Collection pressure.

### Async Sink
A Serilog logging pattern where log writes happen on a background thread to prevent blocking the main application thread.

---

## B

### Background Thread
A worker thread used for heavy computations (process enumeration, data parsing) to keep the UI thread responsive.

### Benchmarking
Performance measurement using BenchmarkDotNet to validate optimization claims. Required for any performance-critical code change.

### BitmapSource
A WPF image type. Must be frozen (made immutable) before use across threads.

### Boxing
Converting a value type (int, struct, enum) to a reference type (object). Causes heap allocation. The project avoids boxing in hot paths.

### Buffer
Pre-allocated memory region reused across operations to avoid repeated allocations. Examples: `top5Buffer`, native P/Invoke buffer.

---

## C

### Cache-Aside Pattern
A caching strategy where the application checks cache first, loads data on miss, then stores it. Used by `ImageLoaderService`.

### Category 1 (Critical)
Issues that block shipping: security vulnerabilities, correctness bugs, stability problems, data corruption.

### Category 2 (High-Impact)
Issues that should be fixed: significant performance problems, usability issues, major maintainability concerns.

### Category 3 (Low-Impact)
Issues that can be deferred: micro-optimizations, stylistic preferences, theoretical edge cases.

### Composite Key
Using multiple fields as identity. The project uses `(PID, CreateTime)` to uniquely identify processes since PIDs can be reused.

### Commit Charge
Windows virtual memory metric representing the total amount of memory committed across the system (physical + page file).

### CPU Usage Percentage
Calculated as: `(CurrentProcessorTime - PreviousProcessorTime) / ElapsedWallClockTime / ProcessorCount * 100`.

### CSRSS
Client/Server Runtime Subsystem. A protected Windows system process that cannot be terminated or queried by most applications.

---

## D

### Delta Calculation
Computing the difference between two snapshots to derive rates (CPU usage per second, I/O bytes per second).

### Differential Update
Updating only changed items in a collection rather than clearing and rebuilding. Used by `SyncProcessCollection` algorithm.

### Dispatcher
WPF's mechanism for marshalling work to the UI thread. All UI updates must happen on the Dispatcher thread.

### Dispose Pattern
Implementation of `IDisposable` to release unmanaged resources (native handles, buffers, event subscriptions).

### DllImport
Legacy P/Invoke attribute (replaced by `LibraryImport` in .NET 7+).

### DPI
Dots Per Inch. Not extensively used in this project but relevant for WPF icon rendering.

---

## E

### Enricher
A Serilog component that adds contextual information to log entries (Process ID, Thread ID, timestamps).

### ETW
Event Tracing for Windows. A kernel-level event system. Future consideration for incremental process updates.

### Expansion State
The IsExpanded property of TreeView nodes. Must be preserved across UI refresh cycles for good UX.

---

## F

### Frozen Object
A WPF object made immutable via `.Freeze()`. Frozen objects can be safely used across threads.

### Full Rebuild
Clearing an ObservableCollection and re-adding all items. Avoided in favor of differential updates due to poor performance and UX.

---

## G

### GC (Garbage Collection)
.NET's automatic memory management system. The project minimizes GC pressure through zero-allocation patterns.

### Gen0/Gen1/Gen2 Collection
Garbage Collection generations. Gen0 is most frequent (short-lived objects), Gen2 is rare (long-lived objects).

### GDI+
Graphics Device Interface Plus. Windows API for 2D graphics. Used for extracting icons from executables.

### GlobalMemoryStatusEx
Windows API function that retrieves physical and virtual memory statistics.

### GRASP
General Responsibility Assignment Software Patterns. A set of OOP design principles referenced in project standards.

---

## H

### Handle
An operating system reference to a kernel object (process, thread, file, etc.). Must be closed to avoid resource leaks.

### Handle Count
Number of kernel object handles held by a process. Tracked for display in the UI.

### Hot Path
Code executed frequently (e.g., refresh loops, UI updates). Must be heavily optimized and profiled.

### Hungarian Notation
A naming convention using prefixes to indicate type (e.g., `strName`, `iPid`). Explicitly avoided in this project.

---

## I

### Icon Cache
A service (`IconCache.cs`) that extracts and caches executable icons using GDI+.

### Identity
Unique identification of a process. Uses composite key `(PID, CreateTime)` rather than PID alone.

### ImageLoaderService
Service responsible for asynchronously loading and caching process icons with thread-safety.

### In-Place Update
Modifying existing object properties rather than creating new objects. Key to zero-allocation architecture.

### Insertion Sort
Sorting algorithm used for maintaining `top5Buffer`. O(N) for small fixed-size arrays, avoids LINQ allocations.

### IntPtr
.NET type representing a pointer or handle. Platform-specific size (4 bytes on x86, 8 bytes on x64).

### I/O Bytes
Cumulative count of read and write operations performed by a process. Used to calculate I/O rate.

---

## K

### Kernel Time
CPU time spent executing Windows kernel code on behalf of a process.

### Kernel32.dll
Core Windows API library providing process, memory, and file system functions.

---

## L

### Latency
Time required to complete an operation. Target: <5ms for full process snapshot, <2ms for UI update.

### LibraryImport
Modern .NET 7+ P/Invoke attribute using source generators for better performance and compile-time validation.

### LINQ (Language Integrated Query)
.NET query syntax. Avoided in hot paths due to allocations (enumerators, delegates, intermediate collections).

### LiteDialog
Project-specific dialog service that creates Windows programmatically without XAML to minimize overhead.

### LOH (Large Object Heap)
Separate heap for objects ≥85KB. Not compacted by GC, causing fragmentation. Avoided via unmanaged buffers.

---

## M

### Marshal
.NET class for interop between managed and unmanaged code. Used for `AllocHGlobal`, `PtrToStringUni`, etc.

### MEMORYSTATUSEX
Windows structure containing memory statistics (total/available physical and virtual memory).

### MVVM (Model-View-ViewModel)
Design pattern separating UI (View), presentation logic (ViewModel), and data (Model).

---

## N

### Native API
Windows kernel APIs in ntdll.dll, often undocumented. Faster than Win32 APIs but less stable across Windows versions.

### Native Memory
Memory allocated outside the .NET managed heap (via `Marshal.AllocHGlobal`). Must be manually freed.

### NotifyIcon
System tray icon component. Project uses `H.NotifyIcon.Wpf` for pure WPF implementation.

### NtQueryInformationProcess
Native API for querying process-specific information (command line, parent PID, etc.).

### NtQuerySystemInformation
Native API that returns all process data in a single call. Core of the project's performance strategy.

### Ntdll.dll
Windows NT Layer DLL containing native kernel APIs.

---

## O

### ObservableCollection
WPF collection that raises notifications when items are added/removed. Used for data binding to UI.

### ObservableObject
Base class from CommunityToolkit.Mvvm providing `INotifyPropertyChanged` implementation.

### ObservableProperty
Attribute that generates property with change notification via source generator.

### Object Pooling
Reusing object instances rather than allocating new ones. Implemented via `Microsoft.Extensions.ObjectPool`.

### OECR
Observation → Evidence → Conclusion → Recommendation. Analysis format used in code reviews.

### Orphaned Process
A process whose parent has terminated. Displayed as root node in the process tree.

---

## P

### P/Invoke (Platform Invoke)
.NET mechanism for calling native Windows APIs from managed code.

### Parent PID
Process ID of the process that created (spawned) the current process. Used for building the process tree.

### PDH (Performance Data Helper)
Windows API for accessing performance counters (CPU, disk, network metrics).

### PerfView
Microsoft tool for analyzing .NET performance and memory usage.

### PFPSO
Principle → Formulation → Protocol → Standards → Output. Analysis framework used in project decisions.

### PID (Process ID)
Numeric identifier assigned by Windows to each running process. Can be reused after process termination.

### Pinning
Preventing the Garbage Collector from moving an object. Required for passing managed arrays to native code.

### Pointer Arithmetic
Calculating memory addresses using pointer offsets. Used for parsing native structures without marshalling.

### Pooled StringBuilder
A `StringBuilder` instance obtained from `StringBuilderPool` that's automatically returned on disposal.

### Process History
Internal structure (`ProcessHistory`) storing previous CPU time and I/O bytes for delta calculations.

### ProcessInfo
Core data model representing a single process with all its properties (PID, name, CPU, memory, etc.).

### ProcessItemViewModel
ViewModel wrapper around `ProcessInfo` adding UI-specific properties (selection, expansion, formatting).

### ProcessService
Core service class that queries Windows APIs and maintains the process cache.

### Producer-Consumer
Threading pattern where one thread produces data (ProcessService) and another consumes it (UI thread).

---

## Q

### Query
In PDH context: a collection of performance counters to be sampled together.

---

## R

### Refresh Cycle
Complete iteration of: query kernel → parse data → update cache → rebuild tree → sync UI.

### Refresh Rate
Frequency of refresh cycles. Default: 1 second (1 Hz).

### RelayCommand
CommunityToolkit.Mvvm attribute that generates `ICommand` implementation via source generator.

### Root Node
A process with no parent in the current snapshot (either `ParentPID == 0` or parent not found).

### Root Process
See Root Node. Examples: System (PID 0), processes started by Explorer, orphaned processes.

---

## S

### SafeHandle
.NET wrapper for unmanaged handles providing automatic cleanup via finalizer.

### Semaphore
Synchronization primitive limiting concurrent access. Project uses `SemaphoreSlim` for refresh coordination.

### Serilog
Structured logging library used for diagnostic output with async file sink.

### Service Process
A Windows background service (not GUI application). Identified via `EnumServicesStatusExW` API.

### Ship It Filter
Decision framework categorizing issues by impact (Category 1/2/3) to prioritize work.

### Sink
In Serilog: destination for log output (file, console, database, etc.).

### SOLID
Five object-oriented design principles: Single Responsibility, Open-Closed, Liskov Substitution, Interface Segregation, Dependency Inversion.

### Source Generator
Roslyn compiler feature that generates code at compile time. Used by CommunityToolkit.Mvvm for properties and commands.

### Span<T>
Stack-allocated or memory-mapped array slice. Enables safe access to unmanaged memory without allocation.

### stackalloc
C# keyword allocating memory on the stack. Avoided for buffers >1KB due to stack overflow risk.

### StatusInfoLengthMismatch
NTSTATUS code `0xC0000004` indicating the buffer provided to a native API is too small.

### StringBuilder Pool
Object pool for `StringBuilder` instances. Implemented in `Helpers/StringBuilderPool.cs`.

### Strangler Fig Pattern
Migration strategy incrementally replacing old system with new one. Referenced for .NET Framework → .NET Core migration.

### Structured Logging
Logging style where events have typed properties (not just string messages). Enables powerful querying.

### SyncProcessCollection
Project-specific algorithm performing differential update on `ObservableCollection` to minimize UI churn.

### SystemPrimitives
Static class containing all P/Invoke declarations for Windows APIs.

### SystemProcessInformation
Native Windows structure (undocumented) returned by `NtQuerySystemInformation`.

### SystemStats
Aggregate statistics structure containing CPU%, memory, disk, and process counts.

---

## T

### Thread Affinity
Requirement that an object be accessed only from the thread that created it. WPF objects have thread affinity unless frozen.

### Thread Count
Number of execution threads within a process.

### ToT (Tree of Thoughts)
Analytical thinking methodology for exploring decision branches. Referenced in project rules.

### Transitive Dependency
NuGet package required by a direct dependency (not explicitly referenced in .csproj).

### Tree Isolation
UI feature allowing focus on a single process subtree by hiding all other processes.

### TreeView
WPF control displaying hierarchical data. Must be virtualized for performance with large datasets.

---

## U

### Unmanaged Memory
Memory outside .NET's garbage-collected heap. Allocated via `Marshal.AllocHGlobal`, freed via `Marshal.FreeHGlobal`.

### Unsafe Code
C# code marked with `unsafe` keyword allowing direct pointer manipulation. Required for P/Invoke optimization.

### UnicodeString
Windows native string structure with Length (bytes), MaximumLength, and Buffer pointer.

### User Time
CPU time spent executing user-mode code (application code, not kernel).

---

## V

### ViewModel
MVVM component containing presentation logic and exposing data for View binding. Implements `INotifyPropertyChanged`.

### ViewModel Cache
Dictionary (`viewModelCache`) mapping PID to `ProcessItemViewModel` to preserve UI state across refreshes.

### Virtualization
WPF technique creating UI containers only for visible items. Essential for TreeView performance.

### VirtualizingStackPanel
WPF panel that virtualizes child elements. Must be explicitly enabled for TreeView.

---

## W

### Win32 API
User-mode Windows APIs in kernel32.dll, user32.dll, advapi32.dll, etc. Documented but slower than Native APIs.

### Win32Exception
Exception thrown when a Windows API call fails. Contains NativeErrorCode property.

### Windows Message
Event notification sent by Windows to application windows. Examples: WmWindowPosChanging (z-order about to change), WmActivateApp (application activation). Used by StatsView for message-driven topmost enforcement.

### WMI (Windows Management Instrumentation)
Windows infrastructure for querying system information. Not used in this project due to performance overhead.

### WndProc (Window Procedure)
Message handler function that processes Windows messages. Hooked via `HwndSource.AddHook()` in WPF to intercept messages before WPF processes them.

### Working Set Private
Amount of physical memory exclusively used by a process (not shared with other processes).

### WPF (Windows Presentation Foundation)
.NET UI framework using XAML and MVVM pattern.

---

## X

### XAML (eXtensible Application Markup Language)
XML-based markup language for defining WPF user interfaces.

### xUnit
.NET testing framework. Recommended for unit tests (not currently included in project).

---

## Y

### YAGNI (You Aren't Gonna Need It)
Agile principle: don't build features for hypothetical future requirements. Applied to avoid over-engineering.

---

## Z

### Zero-Allocation
Architecture goal of minimizing heap allocations to reduce GC pressure. Achieved through object reuse, pooling, stack allocation, and caching.

### Zero-Allocation Loop

### Z-Order
Window stacking order determining which windows appear on top of others. Windows with higher z-order (like HwndTopMost) appear above windows with lower z-order. StatsView uses message-driven enforcement to maintain topmost z-order above the taskbar.
The core refresh cycle pattern reusing buffers and objects to avoid allocations after initial warmup.

### Zero-Copy
Technique avoiding memory copying by using pointers or views (Span<T>) over existing data.

---

## Acronyms Quick Reference

| Acronym | Full Term |
|---------|-----------|
| API | Application Programming Interface |
| BCL | Base Class Library |
| CLR | Common Language Runtime |
| CPU | Central Processing Unit |
| CQRS | Command Query Responsibility Segregation |
| CVE | Common Vulnerabilities and Exposures |
| DDD | Domain-Driven Design |
| DI | Dependency Injection |
| DLL | Dynamic Link Library |
| GC | Garbage Collection |
| GDI | Graphics Device Interface |
| GUI | Graphical User Interface |
| I/O | Input/Output |
| IL | Intermediate Language |
| IoC | Inversion of Control |
| LOH | Large Object Heap |
| MVVM | Model-View-ViewModel |
| NT | New Technology (Windows NT) |
| OWASP | Open Web Application Security Project |
| PDH | Performance Data Helper |
| PID | Process Identifier |
| RAM | Random Access Memory |
| SOLID | Single responsibility, Open-closed, Liskov substitution, Interface segregation, Dependency inversion |
| UI | User Interface |
| UX | User Experience |
| VM | Virtual Memory |
| WMI | Windows Management Instrumentation |
| WPF | Windows Presentation Foundation |
| XAML | eXtensible Application Markup Language |
| YAGNI | You Aren't Gonna Need It |

---

## Project-Specific Terms by Category

### Performance Terms
- Zero-Allocation, Hot Path, Differential Update, Object Pooling, In-Place Update, Buffer Reuse

### Windows API Terms
- NtQuerySystemInformation, PInvoke, LibraryImport, UnicodeString, SafeHandle, Win32 API

### Architecture Terms
- MVVM, Producer-Consumer, Cache-Aside, Differential Update, SyncProcessCollection, Composite Key

### Memory Terms
- Managed Heap, Unmanaged Memory, Stack Allocation, LOH, Pinning, Boxing

### Threading Terms
- Dispatcher, Thread Affinity, Background Thread, Semaphore, Async/Await

### UI Terms
- Freezing, Virtualization, ObservableCollection, Data Binding, TreeView

---

## Usage Examples

### In Code Comments
```csharp
// Use stackalloc for temporary buffers to achieve zero-allocation
Span<char> buffer = stackalloc char[4];
```

### In Documentation
"The SyncProcessCollection algorithm performs a differential update on the ObservableCollection to minimize UI churn and preserve expansion state."

### In Discussions
"We're seeing GC pressure from LINQ in the hot path. Let's replace it with a manual loop using a reusable buffer to achieve zero-allocation."

---

## Related Documentation

- **Architecture.md**: System architecture and design patterns
- **Learnings.md**: Technical decisions and rationale
- **Coding-Standards.md**: Naming conventions and code style
- **API-Reference.md**: Detailed API documentation