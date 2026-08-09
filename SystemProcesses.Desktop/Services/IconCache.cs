using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Serilog;

using SystemProcesses.Desktop.Helpers;

namespace SystemProcesses.Desktop.Services;

/// <summary>
/// Caches extracted icons to prevent GDI+ handle leaks and reduce IO/CPU usage.
/// </summary>
public static class IconCache
{
    private static readonly Dictionary<string, Result<ImageSource>> cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock locker = new();

    /// <summary>
    /// Attempts to get or extract an icon for the specified process path.
    /// </summary>
    /// <param name="processPath">The full path to the executable file.</param>
    /// <returns>A Result containing the frozen ImageSource on success, or a Failure with error details.</returns>
    public static Result<ImageSource> GetIcon(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath))
            return new Result<ImageSource>.Failure(
                new ArgumentNullException(nameof(processPath)),
                "Process path is null or empty");

        lock (locker)
        {
            if (cache.TryGetValue(processPath, out var cachedResult))
            {
                return cachedResult;
            }
        }

        // Extract outside lock to avoid contention on slow IO
        var result = ExtractIconInternal(processPath);

        lock (locker)
        {
            // Double-check locking: cache the result (success or failure)
            if (!cache.TryGetValue(processPath, out _))
            {
                cache[processPath] = result;
            }
            return result;
        }
    }

    /// <summary>
    /// Internal method to extract icon from file system.
    /// </summary>
    private static Result<ImageSource> ExtractIconInternal(string processPath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(processPath);
            if (icon == null)
                return new Result<ImageSource>.Failure(
                    new FileNotFoundException("No icon associated with file"),
                    $"Icon.ExtractAssociatedIcon returned null for {processPath}");

            var imageSource = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            imageSource.Freeze(); // Essential for cross-thread access

            return new Result<ImageSource>.Success(imageSource);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to extract icon from process path: {ProcessPath}", processPath);
            return new Result<ImageSource>.Failure(ex, $"Icon extraction failed for {processPath}");
        }
    }
}