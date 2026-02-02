using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace HyPrism.UI.Converters;

/// <summary>
/// Calculates tab indicator width based on container width
/// </summary>
public class TabWidthConverter : IMultiValueConverter
{
    private const double Padding = 4;    // Padding of container
    
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 1 || values[0] is not double containerWidth)
            return 100.0; // Fallback width
        
        // Each tab takes 1/3 of the container width (minus padding)
        var availableWidth = containerWidth - (Padding * 2);
        var tabWidth = availableWidth / 3.0;
        
        return tabWidth;
    }
    
    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
