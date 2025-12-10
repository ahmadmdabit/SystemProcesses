using System;
using System.Globalization;
using System.Windows.Data;

namespace SystemProcesses.Desktop.Converters;

[ValueConversion(typeof(long), typeof(string))]
public class BytesToAutoFormatConverter : IValueConverter
{
    private const long KB = 1024;
    private const long MB = KB * 1024;
    private const long GB = MB * 1024;
    private const long TB = GB * 1024;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            if (bytes >= TB)
                return string.Format(culture, "{0:F2} TB", bytes / (double)TB);

            if (bytes >= GB)
                return string.Format(culture, "{0:F2} GB", bytes / (double)GB);

            if (bytes >= MB)
                return string.Format(culture, "{0:F2} MB", bytes / (double)MB);

            if (bytes >= KB)
                return string.Format(culture, "{0:F2} KB", bytes / (double)KB);

            return string.Format(culture, "{0} B", bytes);
        }

        return "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}