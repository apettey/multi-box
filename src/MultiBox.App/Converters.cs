using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MultiBox.App;

/// <summary>Paints a lamp: red when an effect is active, green for the client-running dot.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush ActiveBrush { get; set; } = Brushes.Red;
    public Brush InactiveBrush { get; set; } = Brushes.Gray;
    public Brush GoodBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x57, 0xD9, 0x8A));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var active = value is true;
        if (parameter as string == "good")
            return active ? GoodBrush : InactiveBrush;
        return active ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>
/// Formats a per-second rate. Idle shows a dash rather than "0.0" so a glance separates
/// "nothing happening" from a real low number.
/// </summary>
public sealed class RateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double rate)
            return "-";
        if (rate < 0.05)
            return "-";
        return rate >= 1000 ? $"{rate / 1000:F1}k" : rate.ToString("F0", culture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
