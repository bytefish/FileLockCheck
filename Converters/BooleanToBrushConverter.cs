using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FileLockCheck.Converters;

/// <summary>
/// Converts a boolean value to a Brush for UI elements, allowing for visual indication of lock status.
/// </summary>
public class BooleanToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));

    public Brush FalseBrush { get; set; } = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is bool b && b) ? TrueBrush : FalseBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}