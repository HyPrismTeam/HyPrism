using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using HyPrism.Backend;

namespace HyPrism.UI.Converters;

/// <summary>
/// Markup extension for localization in AXAML.
/// Usage: Text="{loc:Localize settings.title}"
/// </summary>
public class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; }
    
    public LocalizeExtension()
    {
        Key = string.Empty;
    }
    
    public LocalizeExtension(string key)
    {
        Key = key;
    }
    
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return new BindingNotification(new InvalidOperationException("LocalizeExtension: Key is required"), BindingErrorType.Error);
        
        var binding = new ReflectionBindingExtension($"Localization[{Key}]")
        {
            Mode = BindingMode.OneWay
        };
        
        return binding.ProvideValue(serviceProvider);
    }
}
