using System;
using System.Globalization;
using System.Windows.Data;

namespace SystemProcesses.Desktop.Converters
{
    public class CpuPercentageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double percentage)
            {
                return $"{percentage:F2}%";
            }

            return "0.00%";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
