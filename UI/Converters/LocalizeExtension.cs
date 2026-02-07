using System;
using System.Reactive.Linq;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using HyPrism.Services.Core;

namespace HyPrism.UI.Converters;

/// <summary>
/// Reactive markup extension for localization in AXAML.
/// Usage: Text="{loc:Localize settings.title}"
/// Automatically updates when language changes via ReactiveUI WhenAnyValue.
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
        
        // Use GetObservable which creates a reactive stream via WhenAnyValue
        var observable = LocalizationService.Current?.GetObservable(Key);
        
        if (observable is null)
            return new BindingNotification(new InvalidOperationException("LocalizationService not initialized"), BindingErrorType.Error);
        
        // Return IObservable directly — Avalonia supports IObservable<T> as binding source
        // via its built-in observable-to-binding pipeline. No intermediate ReplaySubject needed.
        return observable;
    }
}
