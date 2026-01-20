# Error Handling Policy

**Document Version**: 1.0  
**Last Updated**: 2026-01-20  
**Status**: Active

---

## Overview

This document defines the standardized error handling patterns used throughout the SystemProcesses codebase. All developers must follow these patterns to ensure consistent, observable, and maintainable error handling.

---

## Core Principles

1. **Observable**: All errors must be logged with sufficient context for troubleshooting
2. **Graceful**: Application should continue functioning even when individual operations fail
3. **Consistent**: Same types of errors should be handled the same way throughout the codebase
4. **Type-Safe**: Use `Result<T>` pattern for expected failures instead of exceptions
5. **Documented**: Document which methods throw vs return defaults

---

## Error Handling Patterns

### Pattern 1: Expected Failures (Use Result<T>)

**When to use**: Operations that can fail for expected reasons (file not found, access denied, process exited, etc.)

**When NOT to use**: Unexpected exceptions (OutOfMemoryException, StackOverflowException, etc.) - these should throw

**Implementation**:
```csharp
using SystemProcesses.Desktop.Helpers;

public Result<string> GetCommandLine(int pid)
{
    // Validate input
    if (pid <= 4)
    {
        return new Result<string>.Failure(
            new InvalidOperationException("Cannot query system processes"),
            $"PID {pid} is a system process");
    }

    IntPtr hProcess = SystemPrimitives.OpenProcess(
        SystemPrimitives.ProcessQueryLimitedInformation, false, pid);
    
    if (hProcess == IntPtr.Zero)
    {
        return new Result<string>.Failure(
            new UnauthorizedAccessException("OpenProcess failed"),
            $"Failed to open process {pid} (access denied or exited)");
    }

    try
    {
        int bufferSize = 0;
        SystemPrimitives.NtQueryInformationProcess(hProcess,
            SystemPrimitives.ProcessCommandLineInformation, IntPtr.Zero, 0, out bufferSize);

        if (bufferSize == 0)
        {
            return new Result<string>.Failure(
                new InvalidOperationException("Buffer size is 0"),
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
                    new InvalidOperationException($"Query failed with status 0x{status:X8}"),
                    $"NtQueryInformationProcess failed for PID {pid}: 0x{status:X8}");
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
        Log.Warning(ex, "Exception querying command line for PID {Pid}", pid);
        return new Result<string>.Failure(ex, $"Exception in GetCommandLine(pid={pid})");
    }
    finally
    {
        SystemPrimitives.CloseHandle(hProcess);
    }
}

// Usage Pattern 1: Graceful degradation with default value
var result = GetCommandLine(pid);
string commandLine = result.GetValueOrDefault(string.Empty);

// Usage Pattern 2: Explicit success/failure handling
result.Match(
    onSuccess: cmd => Console.WriteLine($"Command: {cmd}"),
    onFailure: (ex, ctx) => Log.Warning(ex, "Failed: {Context}", ctx));

// Usage Pattern 3: Throw on failure (when needed)
string commandLine = result.GetValueOrThrow();
```

**Benefits**:
- Type-safe error handling without exception overhead
- Clear success/failure semantics
- Error context captured for debugging
- Composable with Match() for sophisticated error handling
- Graceful degradation with GetValueOrDefault()
- No exception stack unwinding for expected failures

**Error Context Examples**:
- `"PID 4 is a system process"`
- `"Failed to open process 1234 (access denied or exited)"`
- `"NtQueryInformationProcess failed for PID 5678: 0xC0000008"`

---

### Pattern 2: Unexpected Exceptions (Log and Continue)

**When to use**: Unexpected exceptions that should not crash the application

**Implementation**:
```csharp
public void ProcessItem(ProcessInfo process)
{
    try
    {
        // Process the item
        UpdateUI(process);
    }
    catch (Exception ex)
    {
        // Log with full context
        Log.Error(ex, 
            "Unexpected error processing process {ProcessName} (PID {Pid})",
            process.Name, process.Pid);
        
        // Continue execution - don't crash
    }
}
```

**Logging Format**:
- **Level**: Error (for unexpected), Warning (for expected)
- **Message**: Describe what was being attempted
- **Context**: Include relevant identifiers (PID, name, etc.)
- **Exception**: Always include the exception object

---

### Pattern 3: Critical Failures (Log and Throw)

**When to use**: Failures that prevent the application from functioning

**Implementation**:
```csharp
public void InitializeService()
{
    try
    {
        // Critical initialization
        buffer = Marshal.AllocHGlobal(bufferSize);
        if (buffer == IntPtr.Zero)
        {
            throw new OutOfMemoryException(
                $"Failed to allocate {bufferSize} bytes for process buffer");
        }
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Failed to initialize ProcessService");
        throw;
    }
}
```

**Logging Format**:
- **Level**: Fatal (for critical failures)
- **Message**: Describe the critical failure
- **Exception**: Always include the exception object
- **Action**: Rethrow to propagate to caller

---

### Pattern 4: Validation Failures (Log and Return Default)

**When to use**: Input validation or data integrity checks

**Implementation**:
```csharp
private string GetProcessName(IntPtr buffer, int bufferSize)
{
    // Validate input
    if (buffer == IntPtr.Zero)
    {
        Log.Warning("Null buffer pointer for process name");
        return "Unknown";
    }

    if (bufferSize <= 0)
    {
        Log.Warning("Invalid buffer size for process name: {Size}", bufferSize);
        return "Unknown";
    }

    try
    {
        return Marshal.PtrToStringUni(buffer) ?? "Unknown";
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to marshal process name from buffer");
        return "Unknown";
    }
}
```

**Logging Format**:
- **Level**: Warning (for validation failures)
- **Message**: Describe the validation failure
- **Context**: Include the invalid value
- **Return**: Safe default value

---

### Pattern 5: Resource Cleanup (Try-Finally)

**When to use**: Operations that acquire resources

**Implementation**:
```csharp
public void QueryProcessData(int pid)
{
    IntPtr hProcess = IntPtr.Zero;
    try
    {
        hProcess = SystemPrimitives.OpenProcess(
            SystemPrimitives.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        
        if (hProcess == IntPtr.Zero)
        {
            Log.Warning("Failed to open process {Pid}", pid);
            return;
        }

        // Use the handle
        QueryData(hProcess);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error querying process {Pid}", pid);
    }
    finally
    {
        if (hProcess != IntPtr.Zero)
        {
            SystemPrimitives.CloseHandle(hProcess);
        }
    }
}
```

**Best Practices**:
- Always use try-finally for resource cleanup
- Check for null/zero handles before closing
- Log errors before cleanup
- Never throw from finally block

---

### Pattern 6: Async Error Handling

**When to use**: Asynchronous operations

**Implementation**:
```csharp
public async Task<Result<ProcessTree>> GetProcessTreeAsync()
{
    try
    {
        var snapshot = await Task.Run(() => UpdateProcessSnapshot());
        
        if (snapshot == null)
        {
            return new Result<ProcessTree>.Failure(
                new InvalidOperationException("Snapshot is null"),
                "UpdateProcessSnapshot()");
        }

        return new Result<ProcessTree>.Success(snapshot);
    }
    catch (OperationCanceledException ex)
    {
        Log.Information("Process tree query cancelled");
        return new Result<ProcessTree>.Failure(ex, "GetProcessTreeAsync()");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to get process tree");
        return new Result<ProcessTree>.Failure(ex, "GetProcessTreeAsync()");
    }
}
```

**Best Practices**:
- Catch `OperationCanceledException` separately
- Log cancellations as Information (not Error)
- Use Result<T> for expected failures
- Log exceptions for unexpected failures

---

## Logging Guidelines

### Log Levels

| Level | Usage | Example |
|-------|-------|---------|
| **Fatal** | Application cannot continue | Failed to allocate memory for core buffer |
| **Error** | Unexpected error, operation failed | Failed to query process information |
| **Warning** | Expected failure or degraded functionality | PDH initialization failed, disk I/O monitoring disabled |
| **Information** | Significant events | PDH initialized successfully, process exitd |
| **Debug** | Detailed diagnostic information | Buffer resized from 1MB to 2MB |
| **Verbose** | Very detailed diagnostic information | Processing PID 1234, CPU: 5.2% |

### Log Message Format

```csharp
// Good: Includes context and values
Log.Warning("PdhAddEnglishCounter (PhysicalDisk) failed with status 0x{Status:X8}. Trying LogicalDisk.", status);

// Bad: Too vague
Log.Warning("PDH failed");

// Good: Includes operation and identifiers
Log.Error(ex, "Failed to get command line for PID {Pid}", pid);

// Bad: Missing context
Log.Error(ex, "Error");
```

### Structured Logging

Always use named parameters for structured logging:

```csharp
// Good: Structured logging
Log.Warning("Process {ProcessName} (PID {Pid}) exceeded CPU threshold {Threshold}%",
    process.Name, process.Pid, threshold);

// Bad: String interpolation
Log.Warning($"Process {process.Name} (PID {process.Pid}) exceeded CPU threshold {threshold}%");
```

---

## Method Documentation

### Document Error Handling in XML Comments

```csharp
/// <summary>
/// Gets the command line for a process.
/// </summary>
/// <param name="pid">The process ID.</param>
/// <returns>
/// A Result containing the command line on success, or a Failure with the error on failure.
/// Returns "Unknown" if the command line cannot be retrieved.
/// </returns>
/// <remarks>
/// This method handles access denied errors gracefully and returns a default value
/// rather than throwing an exception. Expected failures are logged as warnings.
/// </remarks>
public Result<string> GetCommandLine(int pid)
{
    // Implementation
}

/// <summary>
/// Initializes the PDH (Performance Data Helper) for disk I/O monitoring.
/// </summary>
/// <remarks>
/// This method logs detailed information about initialization success or failure.
/// If PDH initialization fails, disk I/O monitoring is disabled but the application
/// continues to function normally.
/// 
/// Initialization attempts:
/// 1. Open PDH query
/// 2. Add PhysicalDisk counter (primary)
/// 3. Fallback to LogicalDisk counter if PhysicalDisk fails
/// 4. Collect initial data
/// 
/// All failures are logged with status codes for troubleshooting.
/// </remarks>
private void InitializePdh()
{
    // Implementation
}
```

---

## Error Handling Checklist

Before committing code, verify:

- [ ] All try-catch blocks log the exception
- [ ] Log messages include sufficient context (identifiers, values)
- [ ] Expected failures use Result<T> pattern
- [ ] Unexpected exceptions are logged and handled gracefully
- [ ] Resources are cleaned up in finally blocks
- [ ] Critical failures are logged as Fatal
- [ ] Expected failures are logged as Warning
- [ ] Async operations handle OperationCanceledException
- [ ] XML comments document error handling behavior
- [ ] No silent failures (all errors are logged)

---

## Common Mistakes to Avoid

### ❌ Silent Failures
```csharp
// BAD: No logging
try { ... }
catch { }

// GOOD: Log the error
try { ... }
catch (Exception ex)
{
    Log.Warning(ex, "Operation failed");
}
```

### ❌ Vague Error Messages
```csharp
// BAD: No context
Log.Error("Error");

// GOOD: Include context
Log.Error(ex, "Failed to query process {Pid}", pid);
```

### ❌ Throwing from Finally
```csharp
// BAD: Can mask original exception
finally
{
    throw new Exception("Cleanup failed");
}

// GOOD: Log and continue
finally
{
    try { Cleanup(); }
    catch (Exception ex) { Log.Warning(ex, "Cleanup failed"); }
}
```

### ❌ Catching Too Broadly
```csharp
// BAD: Catches everything including StackOverflowException
try { ... }
catch (Exception ex) { }

// GOOD: Catch specific exceptions
try { ... }
catch (Win32Exception ex) { }
catch (OutOfMemoryException ex) { }
```

### ❌ Not Cleaning Up Resources
```csharp
// BAD: Handle not closed on exception
IntPtr handle = OpenProcess(...);
DoSomething(handle);
CloseHandle(handle);

// GOOD: Always close in finally
IntPtr handle = IntPtr.Zero;
try
{
    handle = OpenProcess(...);
    DoSomething(handle);
}
finally
{
    if (handle != IntPtr.Zero)
        CloseHandle(handle);
}
```

---

## References

- [Microsoft: Exception Handling Best Practices](https://docs.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions)
- [Serilog: Structured Logging](https://serilog.net/)
- [Railway-Oriented Programming](https://fsharpforfunandprofit.com/posts/recipe-part2/)

---

## Questions?

Contact the development team or refer to the coding standards documentation.

