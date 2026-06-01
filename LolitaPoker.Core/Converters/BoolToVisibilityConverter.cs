// -----------------------------------------------------------------------
// BoolToVisibilityConverter.cs - 布尔值转可见性
// -----------------------------------------------------------------------

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LolitaPoker.Core.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;

        // 支持反向转换 (参数为 "Inverse")
        if (parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            flag = !flag;

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is Visibility v && v == Visibility.Visible;
        if (parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag;
    }
}
