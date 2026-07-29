using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace StudentDesktop.Converters;

// Todo.Priority (0-3: None/Low/Medium/High) -> a left-accent-strip color for the todo card.
// Separate from KindToBrushConverter (different domain: calendar-grid-cell kind vs.
// todo priority) but the same "static hardcoded palette, not DynamicResource" convention —
// matches KindToBrushConverter's own approach for the same reason: these are semantic
// accent colors meant to stay recognizable regardless of light/dark theme.
public class PriorityToBrushConverter : IValueConverter
{
    public static readonly PriorityToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => (value as int?) switch
    {
        1 => new SolidColorBrush(Color.Parse("#1E8449")), // Low
        2 => new SolidColorBrush(Color.Parse("#B9770E")), // Medium
        3 => new SolidColorBrush(Color.Parse("#C0392B")), // High
        _ => Brushes.Transparent, // None
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
