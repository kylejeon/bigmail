using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace JlkMailer.App;

/// <summary>CurrentIndex 와 ConverterParameter 가 같을 때만 보이게 한다. 마법사 화면 전환용.</summary>
public sealed class IndexToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int index && int.TryParse(parameter?.ToString(), out var target) && index == target
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>ConverterParameter 에 "invert" 를 주면 뒤집는다.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (string.Equals(parameter?.ToString(), "invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>ok / warn / crit / muted 문자열을 색으로. 설계 문서의 상태 팔레트를 따른다.</summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public static readonly SolidColorBrush Ok = new((Color)ColorConverter.ConvertFromString("#1B6E52"));
    public static readonly SolidColorBrush Warn = new((Color)ColorConverter.ConvertFromString("#9A6206"));
    public static readonly SolidColorBrush Crit = new((Color)ColorConverter.ConvertFromString("#A82A20"));
    public static readonly SolidColorBrush Muted = new((Color)ColorConverter.ConvertFromString("#5F7183"));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() switch
        {
            "ok" => Ok,
            "warn" => Warn,
            "crit" => Crit,
            _ => Muted,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
