using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace StudentDesktop.Converters;

// Strikethrough a completed to-do's title instead of relying on the checkbox alone.
public class BoolToStrikethroughConverter : IValueConverter
{
    public static readonly BoolToStrikethroughConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TextDecorations.Strikethrough : null!;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
