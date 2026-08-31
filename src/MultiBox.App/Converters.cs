using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MultiBox.App;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Visible when the value is false. The mirror of BoolToVisibilityConverter.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

/// <summary>
/// Height from width at 16:9 — the EVE client's own ratio, so the live thumbnail fills the
/// slot without letterboxing. WPF has no aspect-ratio primitive, so the slot binds its
/// Height to its own ActualWidth through this.
/// </summary>
public sealed class AspectRatioConverter : IValueConverter
{
    public double Ratio { get; set; } = 9.0 / 16.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double width && width > 0 ? width * Ratio : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
