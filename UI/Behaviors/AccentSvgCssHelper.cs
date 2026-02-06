using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls;

namespace HyPrism.UI.Behaviors;

/// <summary>
/// A helper class that reactively generates CSS strings for SVG icons based on the 
/// system accent color. Useful for SVG icons that need to match the current theme.
/// </summary>
/// <example>
/// // In your control:
/// private readonly AccentSvgCssHelper _cssHelper = new();
/// 
/// protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
/// {
///     base.OnAttachedToVisualTree(e);
///     _cssHelper.Attach(this, css => MySvg.Css = css);
/// }
/// 
/// protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
/// {
///     base.OnDetachedFromVisualTree(e);
///     _cssHelper.Detach();
/// }
/// </example>
public class AccentSvgCssHelper : IDisposable
{
    private IDisposable? _resourceSubscription;
    private IDisposable? _brushSubscription;
    private Action<string>? _onCssChanged;
    
    /// <summary>
    /// The current CSS string based on the accent color.
    /// Format: "* { stroke: #RRGGBB; fill: none; }"
    /// </summary>
    public string CurrentCss { get; private set; } = "* { stroke: #FFA845; fill: none; }";
    
    /// <summary>
    /// CSS template. Use {color} as placeholder for the hex color.
    /// Default: "* { stroke: {color}; fill: none; }"
    /// </summary>
    public string CssTemplate { get; set; } = "* {{ stroke: {0}; fill: none; }}";
    
    /// <summary>
    /// Attaches to the visual tree and starts listening for accent color changes.
    /// </summary>
    /// <param name="visual">The visual to attach to (used to access Application resources).</param>
    /// <param name="onCssChanged">Callback invoked whenever the CSS changes.</param>
    public void Attach(Visual visual, Action<string> onCssChanged)
    {
        _onCssChanged = onCssChanged;
        
        // Initial update
        UpdateFromApplication();
        
        if (Application.Current != null)
        {
            // Subscribe to the resource itself (in case the brush instance is replaced)
            _resourceSubscription = Application.Current.GetResourceObservable("SystemAccentBrush")
                .Subscribe(obj =>
                {
                    if (obj is SolidColorBrush brush)
                    {
                        SubscribeToBrush(brush);
                    }
                });
        }
    }
    
    /// <summary>
    /// Detaches from all subscriptions. Call this in OnDetachedFromVisualTree.
    /// </summary>
    public void Detach()
    {
        _brushSubscription?.Dispose();
        _resourceSubscription?.Dispose();
        _brushSubscription = null;
        _resourceSubscription = null;
        _onCssChanged = null;
    }
    
    private void SubscribeToBrush(SolidColorBrush brush)
    {
        // Clean up previous brush subscription
        _brushSubscription?.Dispose();
        
        // Subscribe to Color changes on the specific brush instance
        // This handles the smooth animation updates
        _brushSubscription = brush.GetObservable(SolidColorBrush.ColorProperty)
            .Subscribe(color =>
            {
                Dispatcher.UIThread.InvokeAsync(() => UpdateCssFromColor(color));
            });
    }
    
    private void UpdateCssFromColor(Color c)
    {
        var hexColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        CurrentCss = string.Format(CssTemplate, hexColor);
        _onCssChanged?.Invoke(CurrentCss);
    }
    
    private void UpdateFromApplication()
    {
        if (Application.Current?.TryGetResource("SystemAccentBrush", null, out var resource) == true
            && resource is ISolidColorBrush accentBrush)
        {
            UpdateCssFromColor(accentBrush.Color);
        }
    }
    
    public void Dispose()
    {
        Detach();
    }
}

/// <summary>
/// Static utility methods for generating SVG CSS strings.
/// </summary>
public static class SvgCssUtility
{
    /// <summary>
    /// Generates a stroke-only CSS string for the given color.
    /// </summary>
    public static string StrokeCss(Color color)
    {
        return $"* {{ stroke: #{color.R:X2}{color.G:X2}{color.B:X2}; fill: none; }}";
    }
    
    /// <summary>
    /// Generates a stroke-only CSS string for the given hex color.
    /// </summary>
    public static string StrokeCss(string hexColor)
    {
        return $"* {{ stroke: {hexColor}; fill: none; }}";
    }
    
    /// <summary>
    /// Generates a fill-only CSS string for the given color.
    /// </summary>
    public static string FillCss(Color color)
    {
        return $"* {{ fill: #{color.R:X2}{color.G:X2}{color.B:X2}; stroke: none; }}";
    }
    
    /// <summary>
    /// Generates a fill-only CSS string for the given hex color.
    /// </summary>
    public static string FillCss(string hexColor)
    {
        return $"* {{ fill: {hexColor}; stroke: none; }}";
    }
    
    /// <summary>
    /// Gets the current accent color CSS from the application resources.
    /// </summary>
    public static string GetAccentStrokeCss()
    {
        if (Application.Current?.TryGetResource("SystemAccentBrush", null, out var resource) == true
            && resource is ISolidColorBrush brush)
        {
            return StrokeCss(brush.Color);
        }
        return "* { stroke: #FFA845; fill: none; }";
    }
    
    /// <summary>
    /// Gets the current accent color CSS for fill from the application resources.
    /// </summary>
    public static string GetAccentFillCss()
    {
        if (Application.Current?.TryGetResource("SystemAccentBrush", null, out var resource) == true
            && resource is ISolidColorBrush brush)
        {
            return FillCss(brush.Color);
        }
        return "* { fill: #FFA845; stroke: none; }";
    }
}
