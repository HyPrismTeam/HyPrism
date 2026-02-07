using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace HyPrism.UI.Converters;

public class VersionDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int version)
        {
            return version == 0 ? "latest" : $"v{version}";
        }
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
