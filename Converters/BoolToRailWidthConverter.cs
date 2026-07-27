using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StudentDesktop.Converters;

// Left rail's collapsed (icon-only) vs. expanded (icon+label) width.
public class BoolToRailWidthConverter : IValueConverter
{
    public static readonly BoolToRailWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 200.0 : 56.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
