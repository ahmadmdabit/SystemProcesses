using System;
using System.Runtime.InteropServices;

namespace SystemProcesses.Desktop.Services;

/// <summary>
/// Provides direct access to NtQuerySystemInformation for bulk process
/// data retrieval without the overhead of System.Diagnostics.Process.
/// </summary>
internal static partial class NativeMethods
{
    // NTSTATUS constants
    public const int StatusSuccess = 0x00000000;

    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    // SystemInformationClass
    public const int SystemProcessInformationValue = 5;

    // ProcessAccessFlags
    public const uint ProcessQueryLimitedInformation = 0x1000;

    // ProcessInformationClass
    public const int ProcessCommandLineInformation = 60;

    // Using LibraryImport for .NET 9+ optimization (Source Generated P/Invoke)
    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(
        int SystemInformationClass,
        IntPtr SystemInformation,
        int SystemInformationLength,
        out int ReturnLength);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(
        IntPtr ProcessHandle,
        int ProcessInformationClass,
        IntPtr ProcessInformation,
        int ProcessInformationLength,
        out int ReturnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr hObject);

    // Service Enumeration
    public const int ScManagerConnect = 0x0001;

    public const int ScManagerEnumerateService = 0x0004;
    public const int ScEnumProcessInfo = 0;
    public const int ServiceWIN32 = 0x00000030;
    public const int ServiceStateAll = 0x00000003;

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr OpenSCManagerW(
        string? lpMachineName,
        string? lpDatabaseName,
        uint dwDesiredAccess);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseServiceHandle(IntPtr hSCObject);

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

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    /// <summary>
    /// Retrieves the window handle to the active window attached to the calling thread's message queue.
    /// </summary>
    /// <returns>The handle to the active window or NULL.</returns>
    [LibraryImport("user32.dll", EntryPoint = "GetActiveWindow")]
    internal static partial IntPtr GetActiveWindow();

    // ...... PDH (Performance Data Helper) for Disk % ......
    public const uint PdhFmtDouble = 0x00000200;

    // FIX: Added EntryPoint = "PdhOpenQueryW"
    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int PdhOpenQuery(IntPtr szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    // FIX: Added EntryPoint = "PdhAddEnglishCounterW" (This was the specific crash in your logs)
    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    // FIX: Added EntryPoint = "PdhCollectQueryData" (No string suffix needed, but good practice to be explicit if unsure, though this one usually has no suffix)
    [LibraryImport("pdh.dll", EntryPoint = "PdhCollectQueryData", SetLastError = true)]
    public static partial int PdhCollectQueryData(IntPtr hQuery);

    // FIX: Added EntryPoint = "PdhGetFormattedCounterValue"
    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterValue", SetLastError = true)]
    public static partial int PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, IntPtr lpdwType, out PdhFmtCountervalue pValue);

    // FIX: Added EntryPoint = "PdhCloseQuery"
    [LibraryImport("pdh.dll", EntryPoint = "PdhCloseQuery", SetLastError = true)]
    public static partial int PdhCloseQuery(IntPtr hQuery);

    [StructLayout(LayoutKind.Explicit)]
    public struct PdhFmtCountervalue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double doubleValue;
        [FieldOffset(8)] public long longValue;
    }

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

        public static MemoryStatusEx Default => new() { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    // Defined as a struct for pointer arithmetic, fields aligned manually if needed.
    // On x64, alignment is usually 8 bytes.
    [StructLayout(LayoutKind.Sequential)]
    public struct SystemProcessInformation
    {
        public uint NextEntryOffset;
        public uint NumberOfThreads;
        public long WorkingSetPrivateSize; // Reserved1[0]
        public uint HardFaultCount;        // Reserved1[1]
        public uint NumberOfThreadsHighWatermark; // Reserved1[2]
        public ulong CycleTime;            // Reserved1[3]
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct EnumServiceStatusProcess
    {
        public IntPtr lpServiceName;
        public IntPtr lpDisplayName;
        public ServiceStatusProcess ServiceStatusProcess;
    }

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
}