using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StudentDesktop.Converters;

// Renders one star glyph in a 5-star rating row: filled if this star's position (the
// ConverterParameter, "1".."5") is at or below the bound rating, outline otherwise.
public class RatingStarConverter : IValueConverter
{
    public static readonly RatingStarConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var rating = value is int i ? i : 0;
        var position = parameter is string s && int.TryParse(s, out var p) ? p : 0;
        return rating >= position ? "★" : "☆";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
