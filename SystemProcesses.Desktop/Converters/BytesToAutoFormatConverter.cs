using System;
using System.Globalization;
using System.Windows.Data;

namespace SystemProcesses.Desktop.Converters
{
    public class BytesToAutoFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                if (bytes >= 1_073_741_824)
                    return $"{bytes / 1_073_741_824.0:F2} GB";
                if (bytes >= 1_048_576)
                    return $"{bytes / 1_048_576.0:F2} MB";
                return $"{bytes / 1024.0:F2} KB";
            }

            return "0 KB";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
