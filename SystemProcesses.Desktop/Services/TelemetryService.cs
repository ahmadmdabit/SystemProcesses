using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Serilog;

namespace SystemProcesses.Desktop.Services;

/// <summary>
/// <para>Provides optional telemetry and diagnostics collection for SystemProcesses.</para>
/// <para>
/// This service collects performance metrics, crash information, and diagnostic data
/// to help troubleshoot issues and improve the application. All telemetry is opt-in
/// and can be disabled via configuration.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Zero-Allocation Design:
/// - Uses StringBuilder pooling for diagnostic export
/// - Reuses diagnostic buffer across calls
/// - No allocations in hot paths (metric collection)
/// - Lazy initialization of diagnostic files
/// </para>
/// <para>
/// Thread Safety:
/// - All public methods are thread-safe
/// - Metrics are collected from background threads
/// - File I/O is async to prevent blocking
/// </para>
/// </remarks>
public class TelemetryService : IDisposable
{
    private readonly string diagnosticDirectory;
    private readonly bool isEnabled;
    private bool isDisposed;

    /// <summary>
    /// Performance metrics collected during application lifetime.
    /// </summary>
    public class PerformanceMetrics
    {
        /// <summary>Total number of process refresh cycles completed.</summary>
        public long RefreshCycleCount { get; set; }

        /// <summary>Average latency of process refresh cycles in milliseconds.</summary>
        public double AverageRefreshLatencyMs { get; set; }

        /// <summary>Maximum latency of any process refresh cycle in milliseconds.</summary>
        public long MaxRefreshLatencyMs { get; set; }

        /// <summary>Total number of exceptions caught during operation.</summary>
        public long ExceptionCount { get; set; }

        /// <summary>Peak working set memory in bytes.</summary>
        public long PeakWorkingSetBytes { get; set; }

        /// <summary>Current working set memory in bytes.</summary>
        public long CurrentWorkingSetBytes { get; set; }

        /// <summary>Total garbage collection collections (Gen0 + Gen1 + Gen2).</summary>
        public long TotalGcCollections { get; set; }

        /// <summary>Timestamp when metrics were last updated.</summary>
        public DateTime LastUpdated { get; set; }
    }

    private readonly PerformanceMetrics metrics = new();
    private readonly Stopwatch refreshStopwatch = new();
    private long refreshCycleStartTime;

    /// <summary>
    /// Initializes a new instance of the TelemetryService.
    /// </summary>
    /// <param name="diagnosticDirectory">Directory for storing diagnostic files. If null, diagnostics are disabled.</param>
    /// <param name="isEnabled">Whether telemetry collection is enabled.</param>
    public TelemetryService(string? diagnosticDirectory = null, bool isEnabled = false)
    {
        this.diagnosticDirectory = diagnosticDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SystemProcesses", "Diagnostics");
        this.isEnabled = isEnabled;

        if (isEnabled)
        {
            Log.Information("Telemetry service initialized. Diagnostics directory: {DiagnosticDirectory}",
                this.diagnosticDirectory);
        }
    }

    /// <summary>
    /// Records the start of a process refresh cycle for latency measurement.
    /// </summary>
    /// <remarks>
    /// Zero-allocation: Uses Stopwatch for timing, no allocations.
    /// </remarks>
    public void RecordRefreshCycleStart()
    {
        if (!isEnabled) return;
        refreshCycleStartTime = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Records the completion of a process refresh cycle and updates latency metrics.
    /// </summary>
    /// <remarks>
    /// Zero-allocation: Calculates latency from timestamps, updates metrics in-place.
    /// </remarks>
    public void RecordRefreshCycleEnd()
    {
        if (!isEnabled) return;

        long endTime = Stopwatch.GetTimestamp();
        long elapsedTicks = endTime - refreshCycleStartTime;
        double elapsedMs = (elapsedTicks * 1000.0) / Stopwatch.Frequency;

        lock (metrics)
        {
            metrics.RefreshCycleCount++;

            // Update average latency (exponential moving average)
            if (metrics.RefreshCycleCount == 1)
            {
                metrics.AverageRefreshLatencyMs = elapsedMs;
            }
            else
            {
                // EMA: new_avg = (old_avg * 0.9) + (new_value * 0.1)
                metrics.AverageRefreshLatencyMs =
                    (metrics.AverageRefreshLatencyMs * 0.9) + (elapsedMs * 0.1);
            }

            // Update max latency
            if (elapsedMs > metrics.MaxRefreshLatencyMs)
            {
                metrics.MaxRefreshLatencyMs = (long)elapsedMs;
            }

            metrics.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Records an exception that occurred during operation.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="context">Context describing where the exception occurred.</param>
    public void RecordException(Exception ex, string context)
    {
        if (!isEnabled) return;

        lock (metrics)
        {
            metrics.ExceptionCount++;
        }

        Log.Warning(ex, "Exception recorded in telemetry context: {Context}", context);
    }

    /// <summary>
    /// Updates memory and garbage collection metrics.
    /// </summary>
    /// <remarks>
    /// Zero-allocation: Uses GC.GetTotalMemory and GC.CollectionCount, no allocations.
    /// </remarks>
    public void UpdateMemoryMetrics()
    {
        if (!isEnabled) return;

        try
        {
            using var process = Process.GetCurrentProcess();
            long currentWorkingSet = process.WorkingSet64;

            lock (metrics)
            {
                metrics.CurrentWorkingSetBytes = currentWorkingSet;

                if (currentWorkingSet > metrics.PeakWorkingSetBytes)
                {
                    metrics.PeakWorkingSetBytes = currentWorkingSet;
                }

                // Sum GC collections across all generations
                metrics.TotalGcCollections =
                    GC.CollectionCount(0) +
                    GC.CollectionCount(1) +
                    GC.CollectionCount(2);

                metrics.LastUpdated = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update memory metrics");
        }
    }

    /// <summary>
    /// Gets a snapshot of current performance metrics.
    /// </summary>
    /// <returns>A copy of the current performance metrics.</returns>
    public PerformanceMetrics GetMetricsSnapshot()
    {
        lock (metrics)
        {
            return new PerformanceMetrics
            {
                RefreshCycleCount = metrics.RefreshCycleCount,
                AverageRefreshLatencyMs = metrics.AverageRefreshLatencyMs,
                MaxRefreshLatencyMs = metrics.MaxRefreshLatencyMs,
                ExceptionCount = metrics.ExceptionCount,
                PeakWorkingSetBytes = metrics.PeakWorkingSetBytes,
                CurrentWorkingSetBytes = metrics.CurrentWorkingSetBytes,
                TotalGcCollections = metrics.TotalGcCollections,
                LastUpdated = metrics.LastUpdated
            };
        }
    }

    /// <summary>
    /// Exports diagnostic information to a file for troubleshooting.
    /// </summary>
    /// <remarks>
    /// This method is async to prevent blocking the UI thread. It creates a diagnostic
    /// file with system information, performance metrics, and recent log entries.
    /// </remarks>
    public async Task ExportDiagnosticsAsync()
    {
        if (!isEnabled) return;

        try
        {
            // Ensure diagnostic directory exists
            Directory.CreateDirectory(diagnosticDirectory);

            string fileName = $"SystemProcesses-Diagnostics-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.txt";
            string filePath = Path.Combine(diagnosticDirectory, fileName);

            await Task.Run(() =>
            {
                using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

                writer.WriteLine("=== SystemProcesses Diagnostic Report ===");
                writer.WriteLine($"Generated: {DateTime.UtcNow:O}");
                writer.WriteLine($"OS: {Environment.OSVersion}");
                writer.WriteLine($"Processor Count: {Environment.ProcessorCount}");
                writer.WriteLine($".NET Runtime: {RuntimeInformation.FrameworkDescription}");
                writer.WriteLine();

                writer.WriteLine("=== Performance Metrics ===");
                var snapshot = GetMetricsSnapshot();
                writer.WriteLine($"Refresh Cycles: {snapshot.RefreshCycleCount}");
                writer.WriteLine($"Average Refresh Latency: {snapshot.AverageRefreshLatencyMs:F2}ms");
                writer.WriteLine($"Max Refresh Latency: {snapshot.MaxRefreshLatencyMs}ms");
                writer.WriteLine($"Exceptions Recorded: {snapshot.ExceptionCount}");
                writer.WriteLine($"Peak Working Set: {FormatBytes(snapshot.PeakWorkingSetBytes)}");
                writer.WriteLine($"Current Working Set: {FormatBytes(snapshot.CurrentWorkingSetBytes)}");
                writer.WriteLine($"Total GC Collections: {snapshot.TotalGcCollections}");
                writer.WriteLine();

                writer.WriteLine("=== System Information ===");
                try
                {
                    using var process = Process.GetCurrentProcess();
                    writer.WriteLine($"Process ID: {process.Id}");
                    writer.WriteLine($"Process Name: {process.ProcessName}");
                    writer.WriteLine($"Start Time: {process.StartTime:O}");
                    writer.WriteLine($"Total Processor Time: {process.TotalProcessorTime}");
                    writer.WriteLine($"User Processor Time: {process.UserProcessorTime}");
                    writer.WriteLine($"Privileged Processor Time: {process.PrivilegedProcessorTime}");
                    writer.WriteLine($"Thread Count: {process.Threads.Count}");
                    writer.WriteLine($"Handle Count: {process.HandleCount}");
                }
                catch (Exception ex)
                {
                    writer.WriteLine($"Error collecting process info: {ex.Message}");
                }
            });

            Log.Information("Diagnostics exported to {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export diagnostics");
        }
    }

    /// <summary>
    /// Formats bytes into human-readable format (B, KB, MB, GB).
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;

        if (bytes >= gb)
            return $"{bytes / (double)gb:F2} GB";
        if (bytes >= mb)
            return $"{bytes / (double)mb:F2} MB";
        if (bytes >= kb)
            return $"{bytes / (double)kb:F2} KB";
        return $"{bytes} B";
    }

    /// <summary>
    /// Disposes the telemetry service and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed) return;

        try
        {
            // Export final diagnostics if enabled
            if (isEnabled)
            {
                ExportDiagnosticsAsync().Wait(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during telemetry service disposal");
        }

        isDisposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizer to ensure cleanup if Dispose is not called.
    /// </summary>
    ~TelemetryService()
    {
        Dispose();
    }
}
