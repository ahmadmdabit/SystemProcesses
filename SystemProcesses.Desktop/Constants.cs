using System;

namespace SystemProcesses.Desktop;

/// <summary>
/// Application-wide constants for buffer management, timeouts, thresholds, and configuration.
/// </summary>
/// <remarks>
/// This class consolidates all magic numbers used throughout the application into named constants
/// with clear documentation. This improves maintainability and makes the intent of numeric values explicit.
/// </remarks>
public static class AppConstants
{
    // ============================================================================
    // BUFFER MANAGEMENT
    // ============================================================================

    /// <summary>
    /// Initial buffer size for NtQuerySystemInformation (1 MB).
    /// Sufficient for most systems with 300-500 processes.
    /// </summary>
    public const int InitialBufferSize = 1024 * 1024;

    /// <summary>
    /// Maximum buffer size for NtQuerySystemInformation (100 MB).
    /// Prevents unbounded memory allocation on systems with many processes.
    /// </summary>
    public const int MaxBufferSize = 100 * 1024 * 1024;

    /// <summary>
    /// Buffer padding size when resizing (1 MB).
    /// Reduces reallocation frequency by allocating extra space.
    /// </summary>
    public const int BufferPaddingSize = 1024 * 1024;

    // ============================================================================
    // COLLECTION CAPACITIES (Dictionary/List/HashSet initialization)
    // ============================================================================

    /// <summary>
    /// Initial capacity for active processes dictionary.
    /// Typical systems have 200-500 processes.
    /// </summary>
    public const int InitialActiveProcessesCapacity = 1024;

    /// <summary>
    /// Initial capacity for process history dictionary.
    /// Tracks CPU/IO stats for each process.
    /// </summary>
    public const int InitialPrevStatsCapacity = 1024;

    /// <summary>
    /// Initial capacity for root process nodes list.
    /// Most systems have 10-50 root processes.
    /// </summary>
    public const int InitialRootNodesCapacity = 64;

    /// <summary>
    /// Initial capacity for current PIDs buffer (HashSet).
    /// Used during differential update algorithm.
    /// </summary>
    public const int InitialPidsBufferCapacity = 1024;

    /// <summary>
    /// Initial capacity for stopped PIDs buffer (List).
    /// Tracks processes that exited since last refresh.
    /// </summary>
    public const int InitialStoppedPidsCapacity = 64;

    // ============================================================================
    // PROCESS TRACKING
    // ============================================================================

    /// <summary>
    /// Number of top CPU-consuming processes to track.
    /// Displayed in system tray tooltip.
    /// </summary>
    public const int TopProcessesCount = 5;

    /// <summary>
    /// Maximum drive letters (A-Z).
    /// Used for drive enumeration and storage stats.
    /// </summary>
    public const int MaxDriveLetters = 26;

    /// <summary>
    /// System Idle Process PID (always 0 on Windows).
    /// Excluded from normal process statistics.
    /// </summary>
    public const int SystemIdleProcessPid = 0;

    /// <summary>
    /// System Process PID (always 4 on Windows).
    /// Kernel process, requires special handling.
    /// </summary>
    public const int SystemProcessPid = 4;

    // ============================================================================
    // TIMEOUTS & DELAYS (milliseconds)
    // ============================================================================

    /// <summary>
    /// Graceful shutdown timeout (3 seconds).
    /// Time to wait for process to close after CloseMainWindow().
    /// If exceeded, user is prompted to force stop.
    /// </summary>
    public const int GracefulShutdownTimeoutMs = 3000;

    /// <summary>
    /// Maximum attempts for graceful tree shutdown.
    /// Retries closing processes up to 3 times before prompting force stop.
    /// </summary>
    public const int GracefulTreeShutdownMaxAttempts = 3;

    /// <summary>
    /// Base delay between graceful tree shutdown attempts (1 second).
    /// Multiplied by attempt number for exponential backoff.
    /// </summary>
    public const int GracefulTreeShutdownDelayMs = 1000;

    /// <summary>
    /// Default refresh interval (1 second).
    /// Time between process tree updates.
    /// User can configure: 1s, 2s, 5s, 10s, 20s, or disabled.
    /// </summary>
    public const int DefaultRefreshIntervalMs = 1000;

    // ============================================================================
    // UI & DISPLAY
    // ============================================================================

    /// <summary>
    /// CPU tray icon cache size (0-100%).
    /// Pre-loads 101 icons (one for each percentage point).
    /// Enables zero-allocation icon updates.
    /// </summary>
    public const int CpuIconCacheSize = 101;

    /// <summary>
    /// Maximum CPU percentage for icon clamping.
    /// Ensures icon index stays within 0-100 range.
    /// </summary>
    public const int CpuPercentageMaxClamp = 100;

    /// <summary>
    /// Percentage scale factor for calculations.
    /// Used when converting ratios to percentages (multiply by 100).
    /// </summary>
    public const int PercentageScaleFactor = 100;

    // ============================================================================
    // IMAGE LOADING & CACHING
    // ============================================================================

    /// <summary>
    /// Default maximum image size (50 MB).
    /// Prevents loading extremely large images into memory.
    /// </summary>
    public const int DefaultMaxImageBytes = 50 * 1024 * 1024;

    /// <summary>
    /// Default maximum cache entries for images.
    /// Limits in-memory image cache to prevent unbounded growth.
    /// </summary>
    public const int DefaultMaxCacheEntries = 1024;

    /// <summary>
    /// File stream buffer size (80 KB).
    /// Used when reading image files from disk.
    /// </summary>
    public const int FileStreamBufferSize = 81920;

    /// <summary>
    /// HTTP stream initial buffer size (80 KB).
    /// Used when downloading images from network.
    /// </summary>
    public const int HttpStreamBufferSize = 81920;

    /// <summary>
    /// Icon decode pixel width (32 pixels).
    /// Size for thumbnail icon generation.
    /// </summary>
    public const int IconDecodePixelWidth = 32;

    /// <summary>
    /// Icon decode pixel height (32 pixels).
    /// Size for thumbnail icon generation.
    /// </summary>
    public const int IconDecodePixelHeight = 32;

    // ============================================================================
    // STRING BUILDER POOLING
    // ============================================================================

    /// <summary>
    /// Default StringBuilder capacity (256 characters).
    /// Initial size for pooled StringBuilders.
    /// </summary>
    public const int DefaultStringBuilderCapacity = 256;

    /// <summary>
    /// Maximum retained builders per thread bucket.
    /// Prevents unbounded pool growth.
    /// </summary>
    public const int MaxRetainedBuilders = 32;

    /// <summary>
    /// Maximum StringBuilder capacity (64 KB = 65,536 characters).
    /// Builders exceeding this size are not returned to pool.
    /// </summary>
    public const int MaxStringBuilderCapacity = 1 << 16; // 65,536

    // ============================================================================
    // STRING ENCODING
    // ============================================================================

    /// <summary>
    /// UTF-16 bytes per character (2 bytes).
    /// Used when converting between byte length and character count.
    /// Critical for Marshal.PtrToStringUni validation.
    /// </summary>
    public const int Utf16BytesPerChar = 2;

    // ============================================================================
    // DISK I/O MONITORING
    // ============================================================================

    /// <summary>
    /// Maximum disk idle percentage clamp (100%).
    /// Ensures disk active percentage stays within 0-100 range.
    /// </summary>
    public const int DiskIdleClampMax = 100;

    /// <summary>
    /// Minimum disk idle percentage clamp (0%).
    /// Ensures disk active percentage stays within 0-100 range.
    /// </summary>
    public const int DiskIdleClampMin = 0;

    // ============================================================================
    // PERFORMANCE COUNTERS
    // ============================================================================

    /// <summary>
    /// .NET ticks per second (10,000,000).
    /// Used for converting .NET ticks to seconds.
    /// .NET uses 100-nanosecond resolution.
    /// </summary>
    public const long TicksPerSecond = 10_000_000;
}
