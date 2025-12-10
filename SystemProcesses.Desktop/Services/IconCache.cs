using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SystemProcesses.Desktop.Services;

/// <summary>
/// Caches extracted icons to prevent GDI+ handle leaks and reduce IO/CPU usage.
/// </summary>
public static class IconCache
{
    private static readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    public static ImageSource? GetIcon(string? processPath)
    {
        if (string.IsNullOrEmpty(processPath)) return null;

        lock (_lock)
        {
            if (_cache.TryGetValue(processPath, out var cachedIcon))
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

            lock (_lock)
            {
                // Double-check locking
                if (!_cache.ContainsKey(processPath))
                {
                    _cache[processPath] = imageSource;
                }
                return _cache[processPath];
            }
        }
        catch
        {
            // Icon extraction failed (access denied, etc.)
            // Cache null or a default icon to prevent retrying endlessly? 
            // For now, we just return null and let it retry later or stay empty.
            return null;
        }
    }
}