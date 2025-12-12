using System;
using System.Globalization;
using System.Windows.Data;

namespace SystemProcesses.Desktop.Converters;

[ValueConversion(typeof(double), typeof(string))]
public class CpuPercentageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Note: Input is 0-100, not 0.0-1.0.
        // Standard "P" format expects 0.0-1.0, so we use "F2" + "%"
        if (value is double percentage)
        {
            return string.Format(culture, "{0:F2}%", percentage);
        }

        return "0.00%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}