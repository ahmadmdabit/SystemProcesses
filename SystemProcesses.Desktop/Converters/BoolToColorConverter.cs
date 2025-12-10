using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SystemProcesses.Desktop.Converters;

[ValueConversion(typeof(bool), typeof(Brush))]
public class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = Brushes.Gray;
    public Brush FalseBrush { get; set; } = Brushes.Black;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? TrueBrush : FalseBrush;
        }

        return FalseBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}