using System;
using System.Runtime.InteropServices;

namespace SystemProcesses.Desktop.Services;

/// <summary>
/// Provides direct access to NtQuerySystemInformation for bulk process
/// data retrieval without the overhead of System.Diagnostics.Process.
/// </summary>
internal static partial class NativeMethods
{
    #region Process Information (ntdll.dll, kernel32.dll)

    #region Constants

    /// <summary>NTSTATUS success code.</summary>
    public const int StatusSuccess = 0x00000000;

    /// <summary>NTSTATUS indicating buffer size was insufficient.</summary>
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    /// <summary>SystemInformationClass value for process information.</summary>
    public const int SystemProcessInformationValue = 5;

    /// <summary>Process access right for querying limited information.</summary>
    public const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>ProcessInformationClass value for command line.</summary>
    public const int ProcessCommandLineInformation = 60;

    #endregion Constants

    #region Methods

    /// <summary>
    /// Retrieves system information including process data.
    /// </summary>
    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(
        int SystemInformationClass,
        IntPtr SystemInformation,
        int SystemInformationLength,
        out int ReturnLength);

    /// <summary>
    /// Retrieves information about a specified process.
    /// </summary>
    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(
        IntPtr ProcessHandle,
        int ProcessInformationClass,
        IntPtr ProcessInformation,
        int ProcessInformationLength,
        out int ReturnLength);

    /// <summary>
    /// Opens an existing local process object.
    /// </summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int dwProcessId);

    /// <summary>
    /// Closes an open object handle.
    /// </summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr hObject);

    #endregion Methods

    #region Structures

    /// <summary>
    /// Represents a counted Unicode string used by native APIs.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    /// <summary>
    /// Contains information about a system process returned by NtQuerySystemInformation.
    /// </summary>
    /// <remarks>
    /// On x64, alignment is typically 8 bytes. Fields are aligned manually as needed.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct SystemProcessInformation
    {
        public uint NextEntryOffset;
        public uint NumberOfThreads;
        public long WorkingSetPrivateSize;
        public uint HardFaultCount;
        public uint NumberOfThreadsHighWatermark;
        public ulong CycleTime;
        public long CreateTime;
        public long UserTime;
        public long KernelTime;
        public UnicodeString ImageName;
        public int BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
        public uint HandleCount;
        public uint SessionId;
        public UIntPtr PageDirectoryBase;
        public UIntPtr PeakVirtualSize;
        public UIntPtr VirtualSize;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivatePageCount;
        public long ReadOperationCount;
        public long WriteOperationCount;
        public long OtherOperationCount;
        public long ReadTransferCount;
        public long WriteTransferCount;
        public long OtherTransferCount;
    }

    #endregion Structures

    #endregion Process Information (ntdll.dll, kernel32.dll)

    #region Service Enumeration (advapi32.dll)

    #region Constants

    /// <summary>Required to connect to the service control manager.</summary>
    public const int ScManagerConnect = 0x0001;

    /// <summary>Required to enumerate services.</summary>
    public const int ScManagerEnumerateService = 0x0004;

    /// <summary>Info level for process information.</summary>
    public const int ScEnumProcessInfo = 0;

    /// <summary>Service type flag for Win32 services.</summary>
    public const int ServiceWIN32 = 0x00000030;

    /// <summary>Enumerates services in all states.</summary>
    public const int ServiceStateAll = 0x00000003;

    #endregion Constants

    #region Methods

    /// <summary>
    /// Establishes a connection to the service control manager.
    /// </summary>
    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr OpenSCManagerW(
        string? lpMachineName,
        string? lpDatabaseName,
        uint dwDesiredAccess);

    /// <summary>
    /// Closes a handle to a service control manager or service object.
    /// </summary>
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseServiceHandle(IntPtr hSCObject);

    /// <summary>
    /// Enumerates services in the specified service control manager database.
    /// </summary>
    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumServicesStatusExW(
        IntPtr hSCManager,
        int InfoLevel,
        int dwServiceType,
        int dwServiceState,
        IntPtr lpServices,
        int cbBufSize,
        out int pcbBytesNeeded,
        out int lpServicesReturned,
        ref int lpResumeHandle,
        string? pszGroupName);

    #endregion Methods

    #region Structures

    /// <summary>
    /// Contains the name and status of a service.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct EnumServiceStatusProcess
    {
        public IntPtr lpServiceName;
        public IntPtr lpDisplayName;
        public ServiceStatusProcess ServiceStatusProcess;
    }

    /// <summary>
    /// Contains status information for a service.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ServiceStatusProcess
    {
        public int dwServiceType;
        public int dwCurrentState;
        public int dwControlsAccepted;
        public int dwWin32ExitCode;
        public int dwServiceSpecificExitCode;
        public int dwCheckPoint;
        public int dwWaitHint;
        public int dwProcessId;
        public int dwServiceFlags;
    }

    #endregion Structures

    #endregion Service Enumeration (advapi32.dll)

    #region Memory Information (kernel32.dll)

    #region Methods

    /// <summary>
    /// Retrieves information about the system's current usage of both physical and virtual memory.
    /// </summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    #endregion Methods

    #region Structures

    /// <summary>
    /// Contains information about the current state of both physical and virtual memory.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
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

        /// <summary>
        /// Creates a properly initialized instance with dwLength set.
        /// </summary>
        public static MemoryStatusEx Default => new() { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
    }

    #endregion Structures

    #endregion Memory Information (kernel32.dll)

    #region Window Management (user32.dll)

    #region Methods

    /// <summary>
    /// Retrieves the window handle to the active window attached to the calling thread's message queue.
    /// </summary>
    /// <returns>The handle to the active window or NULL if no window is active.</returns>
    [LibraryImport("user32.dll", EntryPoint = "GetActiveWindow")]
    internal static partial IntPtr GetActiveWindow();

    #endregion Methods

    #endregion Window Management (user32.dll)

    #region Performance Data Helper (pdh.dll)

    #region Constants

    /// <summary>Format flag to return counter value as a double.</summary>
    public const uint PdhFmtDouble = 0x00000200;

    #endregion Constants

    #region Methods

    /// <summary>
    /// Creates a new query that is used to manage the collection of performance data.
    /// </summary>
    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int PdhOpenQuery(
        IntPtr szDataSource,
        IntPtr dwUserData,
        out IntPtr phQuery);

    /// <summary>
    /// Adds the specified language-neutral counter to the query.
    /// </summary>
    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int PdhAddEnglishCounter(
        IntPtr hQuery,
        string szFullCounterPath,
        IntPtr dwUserData,
        out IntPtr phCounter);

    /// <summary>
    /// Collects the current raw data value for all counters in the specified query.
    /// </summary>
    [LibraryImport("pdh.dll", EntryPoint = "PdhCollectQueryData", SetLastError = true)]
    public static partial int PdhCollectQueryData(IntPtr hQuery);

    /// <summary>
    /// Computes a displayable value for the specified counter.
    /// </summary>
    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterValue", SetLastError = true)]
    public static partial int PdhGetFormattedCounterValue(
        IntPtr hCounter,
        uint dwFormat,
        IntPtr lpdwType,
        out PdhFmtCountervalue pValue);

    /// <summary>
    /// Closes all counters contained in the specified query and frees resources.
    /// </summary>
    [LibraryImport("pdh.dll", EntryPoint = "PdhCloseQuery", SetLastError = true)]
    public static partial int PdhCloseQuery(IntPtr hQuery);

    #endregion Methods

    #region Structures

    /// <summary>
    /// Contains the counter value returned by PdhGetFormattedCounterValue.
    /// </summary>
    /// <remarks>
    /// Uses explicit layout as doubleValue and longValue share the same memory location (union).
    /// </remarks>
    [StructLayout(LayoutKind.Explicit)]
    public struct PdhFmtCountervalue
    {
        [FieldOffset(0)]
        public uint CStatus;

        [FieldOffset(8)]
        public double doubleValue;

        [FieldOffset(8)]
        public long longValue;
    }

    #endregion Structures

    #endregion Performance Data Helper (pdh.dll)
}