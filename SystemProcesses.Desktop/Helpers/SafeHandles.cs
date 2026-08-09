using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

using SystemProcesses.Desktop.Services;

namespace SystemProcesses.Desktop.Helpers;

/// <summary>
/// Safe wrapper for Windows process handles.
/// Ensures handles are properly closed even if exceptions occur.
/// </summary>
public sealed class SafeProcessHandle : SafeHandle
{
    /// <summary>
    /// Initializes a new instance of SafeProcessHandle.
    /// </summary>
    public SafeProcessHandle() : base(IntPtr.Zero, true)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the handle is invalid.
    /// </summary>
    public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

    /// <summary>
    /// Releases the handle.
    /// </summary>
    /// <returns>True if the handle was successfully released; otherwise, false.</returns>
    protected override bool ReleaseHandle()
    {
        if (IsInvalid)
            return true;

        return SystemPrimitives.CloseHandle(handle);
    }

    /// <summary>
    /// Opens a process with the specified access rights.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    /// <param name="access">The desired access rights.</param>
    /// <returns>A SafeProcessHandle wrapping the process handle.</returns>
    /// <exception cref="Win32Exception">Thrown if the process cannot be opened.</exception>
    public static SafeProcessHandle Open(int pid, uint access)
    {
        var handle = SystemPrimitives.OpenProcess(access, false, pid);

        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(
                $"Failed to open process {pid} with access 0x{access:X8}");
        }

        var safeHandle = new SafeProcessHandle();
        safeHandle.SetHandle(handle);
        return safeHandle;
    }

    /// <summary>
    /// Attempts to open a process, returning a result instead of throwing.
    /// </summary>
    /// <param name="pid">The process ID.</param>
    /// <param name="access">The desired access rights.</param>
    /// <returns>A Result containing the SafeProcessHandle on success, or a Failure with error details.</returns>
    public static Result<SafeProcessHandle> TryOpen(int pid, uint access)
    {
        var rawHandle = SystemPrimitives.OpenProcess(access, false, pid);

        if (rawHandle == IntPtr.Zero)
        {
            return new Result<SafeProcessHandle>.Failure(
                new UnauthorizedAccessException("OpenProcess failed"),
                $"Failed to open process {pid} with access 0x{access:X8} (access denied or process exited)");
        }

        var handle = new SafeProcessHandle();
        handle.SetHandle(rawHandle);
        return new Result<SafeProcessHandle>.Success(handle);
    }
}

/// <summary>
/// Safe wrapper for Windows service handles.
/// Ensures handles are properly closed even if exceptions occur.
/// </summary>
public sealed class SafeServiceHandle : SafeHandle
{
    /// <summary>
    /// Initializes a new instance of SafeServiceHandle.
    /// </summary>
    public SafeServiceHandle() : base(IntPtr.Zero, true)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the handle is invalid.
    /// </summary>
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>
    /// Releases the handle.
    /// </summary>
    /// <returns>True if the handle was successfully released; otherwise, false.</returns>
    protected override bool ReleaseHandle()
    {
        if (IsInvalid)
            return true;

        return SystemPrimitives.CloseServiceHandle(handle);
    }

    /// <summary>
    /// Opens the Service Control Manager.
    /// </summary>
    /// <param name="machineName">The machine name (null for local machine).</param>
    /// <param name="access">The desired access rights.</param>
    /// <returns>A SafeServiceHandle wrapping the SCM handle.</returns>
    /// <exception cref="Win32Exception">Thrown if the SCM cannot be opened.</exception>
    public static SafeServiceHandle OpenScm(string? machineName, uint access)
    {
        var handle = SystemPrimitives.OpenSCManagerW(machineName, null, access);

        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(
                $"Failed to open Service Control Manager on {machineName ?? "local machine"}");
        }

        var safeHandle = new SafeServiceHandle();
        safeHandle.SetHandle(handle);
        return safeHandle;
    }

    /// <summary>
    /// Attempts to open the Service Control Manager, returning a result instead of throwing.
    /// </summary>
    /// <param name="machineName">The machine name (null for local machine).</param>
    /// <param name="access">The desired access rights.</param>
    /// <returns>A Result containing the SafeServiceHandle on success, or a Failure with error details.</returns>
    public static Result<SafeServiceHandle> TryOpenScm(string? machineName, uint access)
    {
        var rawHandle = SystemPrimitives.OpenSCManagerW(machineName, null, access);

        if (rawHandle == IntPtr.Zero)
        {
            return new Result<SafeServiceHandle>.Failure(
                new UnauthorizedAccessException("OpenSCManagerW failed"),
                $"Failed to open Service Control Manager on {machineName ?? "local machine"} (access denied or unavailable)");
        }

        var handle = new SafeServiceHandle();
        handle.SetHandle(rawHandle);
        return new Result<SafeServiceHandle>.Success(handle);
    }
}

/// <summary>
/// Safe wrapper for Windows PDH query handles.
/// Ensures PDH queries are properly closed even if exceptions occur.
/// </summary>
public sealed class SafePdhQueryHandle : SafeHandle
{
    /// <summary>
    /// Initializes a new instance of SafePdhQueryHandle.
    /// </summary>
    public SafePdhQueryHandle() : base(IntPtr.Zero, true)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the handle is invalid.
    /// </summary>
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>
    /// Releases the handle.
    /// </summary>
    /// <returns>True if the handle was successfully released; otherwise, false.</returns>
    protected override bool ReleaseHandle()
    {
        if (IsInvalid)
            return true;

        int status = SystemPrimitives.PdhCloseQuery(handle);
        return status == 0;
    }

    /// <summary>
    /// Opens a PDH query.
    /// </summary>
    /// <returns>A SafePdhQueryHandle wrapping the PDH query handle.</returns>
    /// <exception cref="Win32Exception">Thrown if the PDH query cannot be opened.</exception>
    public static SafePdhQueryHandle Open()
    {
        int status = SystemPrimitives.PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out IntPtr query);

        if (status != 0)
        {
            throw new Win32Exception(
                $"Failed to open PDH query with status 0x{status:X8}");
        }

        var safeHandle = new SafePdhQueryHandle();
        safeHandle.SetHandle(query);
        return safeHandle;
    }

    /// <summary>
    /// Attempts to open a PDH query, returning a result instead of throwing.
    /// </summary>
    /// <returns>A Result containing the SafePdhQueryHandle on success, or a Failure with error details.</returns>
    public static Result<SafePdhQueryHandle> TryOpen()
    {
        int status = SystemPrimitives.PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out IntPtr query);

        if (status != 0)
        {
            return new Result<SafePdhQueryHandle>.Failure(
                new InvalidOperationException($"PdhOpenQuery failed with status 0x{status:X8}"),
                $"Failed to open PDH query: status 0x{status:X8}");
        }

        var handle = new SafePdhQueryHandle();
        handle.SetHandle(query);
        return new Result<SafePdhQueryHandle>.Success(handle);
    }
}

/// <summary>
/// Safe wrapper for memory allocated via Marshal.AllocHGlobal.
/// Ensures memory is properly freed even if exceptions occur.
/// </summary>
public sealed class SafeHGlobalHandle : SafeHandle
{
    private int size;

    /// <summary>
    /// Initializes a new instance of SafeHGlobalHandle.
    /// </summary>
    /// <param name="size">The size of the allocated memory in bytes.</param>
    public SafeHGlobalHandle(int size) : base(IntPtr.Zero, true)
    {
        this.size = size;
    }

    /// <summary>
    /// Gets a value indicating whether the handle is invalid.
    /// </summary>
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>
    /// Gets the size of the allocated memory.
    /// </summary>
    public int Size => size;

    /// <summary>
    /// Releases the handle.
    /// </summary>
    /// <returns>True if the handle was successfully released; otherwise, false.</returns>
    protected override bool ReleaseHandle()
    {
        if (IsInvalid)
            return true;

        try
        {
            Marshal.FreeHGlobal(handle);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Allocates unmanaged memory.
    /// </summary>
    /// <param name="size">The number of bytes to allocate.</param>
    /// <returns>A SafeHGlobalHandle wrapping the allocated memory.</returns>
    /// <exception cref="OutOfMemoryException">Thrown if memory cannot be allocated.</exception>
    public static SafeHGlobalHandle Allocate(int size)
    {
        if (size <= 0)
            throw new ArgumentException("Size must be greater than zero", nameof(size));

        IntPtr ptr = Marshal.AllocHGlobal(size);

        if (ptr == IntPtr.Zero)
            throw new OutOfMemoryException($"Failed to allocate {size} bytes");

        var handle = new SafeHGlobalHandle(size);
        handle.SetHandle(ptr);
        return handle;
    }

    /// <summary>
    /// Attempts to allocate unmanaged memory, returning a result instead of throwing.
    /// </summary>
    /// <param name="size">The number of bytes to allocate.</param>
    /// <returns>A Result containing the SafeHGlobalHandle on success, or a Failure with error details.</returns>
    public static Result<SafeHGlobalHandle> TryAllocate(int size)
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

    /// <summary>
    /// Resizes the allocated memory.
    /// </summary>
    /// <param name="newSize">The new size in bytes.</param>
    /// <exception cref="OutOfMemoryException">Thrown if memory cannot be reallocated.</exception>
    public void Resize(int newSize)
    {
        if (newSize <= 0)
            throw new ArgumentException("Size must be greater than zero", nameof(newSize));

        if (IsInvalid)
            throw new ObjectDisposedException("SafeHGlobalHandle");

        IntPtr newPtr = Marshal.ReAllocHGlobal(handle, new IntPtr(newSize));

        if (newPtr == IntPtr.Zero)
            throw new OutOfMemoryException($"Failed to reallocate to {newSize} bytes");

        SetHandle(newPtr);
        size = newSize;
    }
}
