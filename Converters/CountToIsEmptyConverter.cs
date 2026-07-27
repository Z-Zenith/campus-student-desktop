using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StudentDesktop.Converters;

// Drives empty-state placeholders: true when a bound collection's Count is 0.
public class CountToIsEmptyConverter : IValueConverter
{
    public static readonly CountToIsEmptyConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
