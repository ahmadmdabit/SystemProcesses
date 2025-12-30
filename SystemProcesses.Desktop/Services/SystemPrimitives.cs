using System;
using System.Runtime.InteropServices;

namespace SystemProcesses.Desktop.Services;

/// <summary>
/// Provides direct access to NtQuerySystemInformation for bulk process
/// data retrieval without the overhead of System.Diagnostics.Process.
/// </summary>
internal static partial class SystemPrimitives
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

    /// <summary>
    /// Determines whether the specified window handle identifies an existing window.
    /// </summary>
    /// <param name="hWnd">Handle to the window to be tested.</param>
    /// <returns>
    /// If the window handle identifies an existing window, the return value is true.
    /// If the window handle does not identify an existing window, the return value is false.
    /// </returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(IntPtr hWnd);

    #endregion Methods

    #endregion Window Management (user32.dll)

    #region GDI

    /// <summary>
    /// Creates a handle to a region with rounded corners, defined by the specified rectangle and ellipse dimensions.Create a region with rounded corners
    /// </summary>
    /// <remarks>The caller is responsible for releasing the region handle by calling DeleteObject when it is
    /// no longer needed. The coordinates are specified in logical units; the region's shape is determined by the
    /// rectangle and the size of the ellipse used for the corners.</remarks>
    /// <param name="x1">The x-coordinate of the upper-left corner of the rectangle that defines the region.</param>
    /// <param name="y1">The y-coordinate of the upper-left corner of the rectangle that defines the region.</param>
    /// <param name="x2">The x-coordinate of the lower-right corner of the rectangle that defines the region.</param>
    /// <param name="y2">The y-coordinate of the lower-right corner of the rectangle that defines the region.</param>
    /// <param name="widthEllipse">The width, in logical units, of the ellipse used to create the rounded corners.</param>
    /// <param name="heightEllipse">The height, in logical units, of the ellipse used to create the rounded corners.</param>
    /// <returns>A handle to the created region. If the function fails, the return value is IntPtr.Zero.</returns>
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr CreateRoundRectRgn(
        int x1, int y1, int x2, int y2,
        int widthEllipse, int heightEllipse);

    /// <summary>
    /// Sets the window region of a specified window, enabling non-rectangular window shapes.Apply a region to a window
    /// </summary>
    /// <remarks>The window region determines the area within the window where the system permits drawing and
    /// user interaction. After a region is set, the system owns the region handle and the application should not use or
    /// delete it. If the window has a class style of CS_OWNDC or CS_CLASSDC, the device context is not updated to
    /// reflect the new region until the window is redrawn.</remarks>
    /// <param name="hWnd">A handle to the window whose window region is to be set.</param>
    /// <param name="hRgn">A handle to a region that defines the new window region. The region is assumed by the system and should not be
    /// deleted after the call.</param>
    /// <param name="redraw">Specifies whether the operating system redraws the window after setting the region. Pass 1 to redraw the window;
    /// pass 0 to not redraw.</param>
    /// <returns>A nonzero value if the function succeeds; otherwise, zero.</returns>
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, int redraw);

    /// <summary>
    /// Deletes a logical pen, brush, font, bitmap, region, or palette, freeing all system resources associated with the object.
    /// </summary>
    /// <param name="hObject">A handle to a logical pen, brush, font, bitmap, region, or palette.</param>
    /// <returns>If the function succeeds, the return value is nonzero. If the function fails, the return value is zero.</returns>
    /// <remarks>
    /// Do not delete objects that are currently selected into a device context.
    /// After an object is deleted, the specified handle is no longer valid.
    /// </remarks>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr hObject);

    #endregion GDI

    #region Application Desktop Toolbar (AppBar)

    #region Constants

    /// <summary>AppBar message: Registers a new appbar and specifies the message identifier that the system should use to send notification messages to the appbar.</summary>
    public const int AbmNew = 0x00000000;

    /// <summary>AppBar message: Unregisters an appbar, removing the bar from the system's internal list.</summary>
    public const int AbmRemove = 0x00000001;

    /// <summary>AppBar message: Requests a size and screen position for an appbar.</summary>
    public const int AbmQueryPos = 0x00000002;

    /// <summary>AppBar message: Sets the size and screen position of an appbar.</summary>
    public const int AbmSetPos = 0x00000003;

    /// <summary>AppBar edge: Left edge of the screen.</summary>
    public const int AbeLeft = 0;

    /// <summary>AppBar edge: Top edge of the screen.</summary>
    public const int AbeTop = 1;

    /// <summary>AppBar edge: Right edge of the screen.</summary>
    public const int AbeRight = 2;

    /// <summary>AppBar edge: Bottom edge of the screen.</summary>
    public const int AbeBottom = 3;

    #endregion Constants

    #region Structures

    /// <summary>
    /// Contains information about a system appbar message.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AppBarData
    {
        /// <summary>Size of the structure, in bytes.</summary>
        public int cbSize;

        /// <summary>Handle to the appbar window.</summary>
        public IntPtr hWnd;

        /// <summary>Application-defined message identifier.</summary>
        public uint uCallbackMessage;

        /// <summary>Edge of the screen to which the appbar is anchored (ABE_* constant).</summary>
        public int uEdge;

        /// <summary>Rectangle that contains the appbar dimensions.</summary>
        public Rect rc;

        /// <summary>Message-specific value.</summary>
        public int lParam;
    }

    /// <summary>
    /// Defines the coordinates of the upper-left and lower-right corners of a rectangle.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        /// <summary>X-coordinate of the upper-left corner.</summary>
        public int left;

        /// <summary>Y-coordinate of the upper-left corner.</summary>
        public int top;

        /// <summary>X-coordinate of the lower-right corner.</summary>
        public int right;

        /// <summary>Y-coordinate of the lower-right corner.</summary>
        public int bottom;
    }

    #endregion Structures

    #region Window Positioning (user32.dll)

    /// <summary>Special window handle: Places the window above all non-topmost windows, even above the taskbar.</summary>
    public static readonly IntPtr HwndTopMost = new IntPtr(-1);

    /// <summary>Special window handle: Places the window above all non-topmost windows but below topmost windows.</summary>
    public static readonly IntPtr HwndNoTopMost = new IntPtr(-2);

    /// <summary>SetWindowPos flag: Retains the current size (ignores cx and cy parameters).</summary>
    public const uint SwpNoSize = 0x0001;

    /// <summary>SetWindowPos flag: Retains the current position (ignores x and y parameters).</summary>
    public const uint SpwNoMove = 0x0002;

    /// <summary>SetWindowPos flag: Does not activate the window.</summary>
    public const uint SwpNoActivate = 0x0010;

    /// <summary>SetWindowPos flag: Displays the window.</summary>
    public const uint SwpShowWindow = 0x0040;

    /// <summary>
    /// Changes the size, position, and Z order of a child, pop-up, or top-level window.
    /// </summary>
    /// <param name="hWnd">Handle to the window.</param>
    /// <param name="hWndInsertAfter">Handle to the window to precede the positioned window in the Z order (can be HwndTopMost).</param>
    /// <param name="X">New position of the left side of the window, in client coordinates.</param>
    /// <param name="Y">New position of the top of the window, in client coordinates.</param>
    /// <param name="cx">New width of the window, in pixels.</param>
    /// <param name="cy">New height of the window, in pixels.</param>
    /// <param name="uFlags">Window sizing and positioning flags (SWP_* constants).</param>
    /// <returns>If the function succeeds, the return value is nonzero.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>SetWindowPos flag: Does not change the owner window's position in the Z order.</summary>
    public const uint SwpNoZOrder = 0x0004;

    /// <summary>Window message: Sent to a window whose size, position, or place in the Z order is about to change.</summary>
    public const int WmWindowPosChanging = 0x0046;

    /// <summary>Window message: Sent to a window after its position has changed.</summary>
    public const int WmWindowPosChanged = 0x0047;

    /// <summary>Window message: Sent when a window is being activated or deactivated.</summary>
    public const int WmActivate = 0x0006;

    /// <summary>Window message: Sent when an application is activated or deactivated.</summary>
    public const int WmActivateApp = 0x001C;

    /// <summary>Window message: Sent to all top-level windows when the display resolution changes.</summary>
    public const int WmDisplayChange = 0x007E;

    /// <summary>Window message: Sent to all top-level windows when system settings change.</summary>
    public const int WmSettingChange = 0x001A;

    /// <summary>Window message: Sent to all top-level windows when DWM composition is enabled or disabled.</summary>
    public const int WmDwmComPositionChanged = 0x031E;

    /// <summary>Window message: Sent to a window after non-client area is activated.</summary>
    public const int WmNcActivate  = 0x0086;

    /// <summary>
    /// Contains information about the size and position of a window.
    /// Used with WM_WINDOWPOSCHANGING and WM_WINDOWPOSCHANGED messages.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WindowPos
    {
        /// <summary>Handle to the window.</summary>
        public IntPtr hwnd;

        /// <summary>Window handle to precede this window in the Z order.</summary>
        public IntPtr hwndInsertAfter;

        /// <summary>Position of the left edge of the window.</summary>
        public int x;

        /// <summary>Position of the top edge of the window.</summary>
        public int y;

        /// <summary>Window width, in pixels.</summary>
        public int cx;

        /// <summary>Window height, in pixels.</summary>
        public int cy;

        /// <summary>Window positioning flags (SWP_* constants).</summary>
        public uint flags;
    }

    #endregion Window Positioning (user32.dll)

    #endregion Application Desktop Toolbar (AppBar)

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

    #region Disk Information (kernel32.dll)

    #region Constants

    /// <summary>
    /// The drive has fixed media; for example, a hard drive or flash drive.
    /// </summary>
    public const uint DriveFixed = 3;

    #endregion Constants

    #region Methods

    /// <summary>
    /// Retrieves a bitmask representing the currently available disk drives.
    /// </summary>
    /// <returns>
    /// If the function succeeds, the return value is a bitmask representing the currently available disk drives.
    /// Bit position 0 (the least-significant bit) is drive A, bit position 1 is drive B, and so on.
    /// </returns>
    [LibraryImport("kernel32.dll")]
    public static partial uint GetLogicalDrives();

    /// <summary>
    /// Determines whether a disk drive is a removable, fixed, CD-ROM, RAM disk, or network drive.
    /// </summary>
    /// <param name="lpRootPathName">
    /// The root directory for the drive. A trailing backslash is required.
    /// If this parameter is NULL, the function uses the root of the current directory.
    /// </param>
    /// <returns>The return value specifies the type of drive, such as DRIVE_FIXED (3).</returns>
    [LibraryImport("kernel32.dll")]
    public static unsafe partial uint GetDriveTypeW(char* lpRootPathName);

    /// <summary>
    /// Retrieves information about the amount of space that is available on a disk volume,
    /// which is the total amount of space, the total amount of free space, and the total
    /// amount of free space available to the user that is associated with the calling thread.
    /// </summary>
    /// <param name="lpDirectoryName">
    /// A directory on the disk. If this parameter is NULL, the function uses the root of the current disk.
    /// </param>
    /// <param name="lpFreeBytesAvailable">
    /// A pointer to a variable that receives the total number of free bytes on a disk that are available
    /// to the user who is associated with the calling thread.
    /// </param>
    /// <param name="lpTotalNumberOfBytes">
    /// A pointer to a variable that receives the total number of bytes on a disk that are available
    /// to the user who is associated with the calling thread.
    /// </param>
    /// <param name="lpTotalNumberOfFreeBytes">
    /// A pointer to a variable that receives the total number of free bytes on a disk.
    /// </param>
    /// <returns>If the function succeeds, the return value is nonzero (true).</returns>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool GetDiskFreeSpaceExW(
        char* lpDirectoryName,
        out ulong lpFreeBytesAvailable,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    #endregion Methods

    #endregion Disk Information (kernel32.dll)
}
