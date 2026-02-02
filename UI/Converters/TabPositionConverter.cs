using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace HyPrism.UI.Converters;

/// <summary>
/// Converts tab filter name to Canvas.Left position for animated tab indicator
/// </summary>
public class TabPositionConverter : IValueConverter
{
    // Container width: 380px - (2 * 4px padding) = 372px
    // Each tab: 372px / 3 = 124px
    // Indicator width should be same as tab: 124px
    private const double ContainerPadding = 4.0;
    private const double TabWidth = 124.0;
    
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string filter)
            return ContainerPadding;
        
        return filter.ToLower() switch
        {
            "all" => ContainerPadding,
            "hytale" => ContainerPadding + TabWidth,
            "hyprism" => ContainerPadding + (TabWidth * 2),
            _ => ContainerPadding
        };
    }
    
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
