using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Serilog;

namespace SystemProcesses.Desktop.Services;

/// <summary>
/// Caches extracted icons to prevent GDI+ handle leaks and reduce IO/CPU usage.
/// </summary>
public static class IconCache
{
    private static readonly Dictionary<string, ImageSource> cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock locker = new();

    public static ImageSource? GetIcon(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath)) return null;

        lock (locker)
        {
            if (cache.TryGetValue(processPath, out var cachedIcon))
            {
                return cachedIcon;
            }
        }

        // Extract outside lock to avoid contention on slow IO
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(processPath);
            if (icon == null) return null;

            var imageSource = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            imageSource.Freeze(); // Essential for cross-thread access

            lock (locker)
            {
                // Double-check locking
                if (!cache.TryGetValue(processPath, out ImageSource? value))
                {
                    value = imageSource;
                    cache[processPath] = value;
                }
                return value;
            }
        }
        catch (Exception ex)
        {
            // Icon extraction failed (access denied, etc.)
            // Cache null or a default icon to prevent retrying endlessly?
            Log.Warning(ex, "Icon extraction failed");
            return null;
        }
    }
}