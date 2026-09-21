using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ApiScout.UI;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class TextToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class EmptyTextToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is not true;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is not true;
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class StarConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? "★" : "☆";
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Key badge text → theme brush.</summary>
public class BadgeBrushConverter : IValueConverter
{
    protected static string Key(object value) => (value as string) switch
    {
        "Demo key" or "Sample key" or "Example endpoint" => "VioletBrush",
        "Open" or "Key optional" or "Sign-up link" or "Full free access" => "UpBrush",
        "Free key" or "Auth scheme" or "Free tier (limited)" or "Pricing" or "Pricing page" => "AccentBrush",
        "Key needed" or "OAuth" or "Free tier / limits" or "Demo / trial only" => "WarnBrush",
        _ => "MutedBrush",
    };

    public virtual object Convert(object value, Type t, object p, CultureInfo c) =>
        Application.Current.Resources[Key(value)] as Brush ?? Brushes.Gray;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class BadgeSoftBrushConverter : BadgeBrushConverter
{
    public override object Convert(object value, Type t, object p, CultureInfo c)
    {
        var solid = (Application.Current.Resources[Key(value)] as SolidColorBrush)?.Color ?? Colors.Gray;
        return new SolidColorBrush(Color.FromArgb(0x2E, solid.R, solid.G, solid.B));
    }
}

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        Application.Current.Resources[(value as string) switch
        {
            "Online" => "UpBrush",
            "Restricted" or "Timeout" => "WarnBrush",
            "Down" => "DownBrush",
            _ => "MutedBrush",
        }] as Brush ?? Brushes.Gray;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
