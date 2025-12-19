using System;
using System.Globalization;
using System.Windows.Data;

namespace SystemProcesses.Desktop.Converters;

/// <summary>
/// Converts numeric percentage values (0-100) to formatted percentage strings.
/// Uses static string caching for common integer percentages to achieve zero-allocation.
/// </summary>
[ValueConversion(typeof(double), typeof(string))]
[ValueConversion(typeof(int), typeof(string))]
public sealed class CpuPercentageConverter : IValueConverter
{
    // Static cache for integer percentages 0-100 (most common case)
    private static readonly string[] IntegerPercentageCache = new string[101];

    // Static cache for common decimal percentages (0.0, 0.5, 1.0, 1.5, ... 100.0)
    private static readonly string[] DecimalPercentageCache = new string[201];

    static CpuPercentageConverter()
    {
        // Pre-compute integer percentages (0% to 100%)
        for (int i = 0; i <= 100; i++)
        {
            IntegerPercentageCache[i] = $"{i}%";
        }

        // Pre-compute decimal percentages with .0 and .5 increments
        for (int i = 0; i <= 200; i++)
        {
            double value = i * 0.5;
            DecimalPercentageCache[i] = $"{value:F1}%";
        }
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Handle null
        if (value == null)
            return "0%";

        double percentage;

        // Convert input to double
        if (value is double d)
        {
            percentage = d;
        }
        else if (value is int i)
        {
            percentage = i;
        }
        else if (value is float f)
        {
            percentage = f;
        }
        else if (value is decimal dec)
        {
            percentage = (double)dec;
        }
        else
        {
            // Try to convert unknown types
            try
            {
                percentage = System.Convert.ToDouble(value);
            }
            catch
            {
                return "0%";
            }
        }

        // Clamp to valid range
        if (percentage < 0) percentage = 0;
        if (percentage > 100) percentage = 100;

        // Check if it's an integer value
        int intValue = (int)percentage;
        if (Math.Abs(percentage - intValue) < 0.01)
        {
            // Use integer cache (zero allocation)
            return IntegerPercentageCache[intValue];
        }

        // Check if it's a half-value (0.5, 1.5, etc.)
        double doubleValue = Math.Round(percentage * 2.0) / 2.0; // Round to nearest 0.5
        int cacheIndex = (int)(doubleValue * 2.0);

        if (cacheIndex >= 0 && cacheIndex < DecimalPercentageCache.Length)
        {
            // Use decimal cache (zero allocation)
            return DecimalPercentageCache[cacheIndex];
        }

        // Fallback for unusual decimal values (rare, minimal allocation)
        return $"{percentage:F1}%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException("CpuPercentageConverter does not support ConvertBack");
    }
}
