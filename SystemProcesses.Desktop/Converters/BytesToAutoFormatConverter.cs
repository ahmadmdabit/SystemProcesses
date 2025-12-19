using System;
using System.Globalization;
using System.Windows.Data;

using SystemProcesses.Desktop.Helpers;

namespace SystemProcesses.Desktop.Converters;

/// <summary>
/// Converts byte values to human-readable format (B, KB, MB, GB, TB).
/// Uses StringBuilderPool for zero-allocation formatting.
/// </summary>
[ValueConversion(typeof(long), typeof(string))]
public sealed class BytesToAutoFormatConverter : IValueConverter
{
    private const long KB = 1024;
    private const long MB = KB * 1024;
    private const long GB = MB * 1024;
    private const long TB = GB * 1024;

    // Cached unit strings to avoid repeated allocations
    private const string UnitBytes = "B";
    private const string UnitKB = "KB";
    private const string UnitMB = "MB";
    private const string UnitGB = "GB";
    private const string UnitTB = "TB";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return "0B";

        long bytes;

        // Handle different numeric types
        if (value is long l)
        {
            bytes = l;
        }
        else if (value is int i)
        {
            bytes = i;
        }
        else if (value is ulong ul)
        {
            bytes = (long)ul;
        }
        else
        {
            // Try to convert unknown types
            try
            {
                bytes = System.Convert.ToInt64(value);
            }
            catch
            {
                return "0B";
            }
        }

        // Handle negative values
        if (bytes < 0)
            return "0 B";

        // Use StringBuilderPool for zero-allocation formatting
        using (var psb = StringBuilderPool.Rent())
        {
            if (bytes >= TB)
            {
                double valueTB = bytes / (double)TB;
                psb.Builder.Append(valueTB.ToString("F2", culture));
                psb.Builder.Append(UnitTB);
            }
            else if (bytes >= GB)
            {
                double valueGB = bytes / (double)GB;
                psb.Builder.Append(valueGB.ToString("F2", culture));
                psb.Builder.Append(UnitGB);
            }
            else if (bytes >= MB)
            {
                double valueMB = bytes / (double)MB;
                psb.Builder.Append(valueMB.ToString("F2", culture));
                psb.Builder.Append(UnitMB);
            }
            else if (bytes >= KB)
            {
                double valueKB = bytes / (double)KB;
                psb.Builder.Append(valueKB.ToString("F2", culture));
                psb.Builder.Append(UnitKB);
            }
            else
            {
                psb.Builder.Append(bytes);
                psb.Builder.Append(UnitBytes);
            }

            return psb.Build();
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException("BytesToAutoFormatConverter does not support ConvertBack");
    }
}
