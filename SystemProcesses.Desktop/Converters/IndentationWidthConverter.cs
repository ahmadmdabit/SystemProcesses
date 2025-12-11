using System;
using System.Globalization;
using System.Windows.Data;

namespace SystemProcesses.Desktop.Converters;

public class IndentationWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Value[0]: The full TreeViewRowWidth (double)
        // Value[1]: The Depth of the item (int)
        // Parameter: Indentation size (double, default 19.0 for standard WPF TreeView)

        if (values.Length == 2 && values[0] is double fullWidth && values[1] is int depth)
        {
            double indentSize = 19.0;
            if (parameter is string paramStr && double.TryParse(paramStr, out double p))
            {
                indentSize = p;
            }

            // Calculate available width
            double adjustedWidth = fullWidth - (depth * indentSize);
            return Math.Max(0, adjustedWidth);
        }

        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}