using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Threading;
using HyPrism.Services.Core;
using AvaTransitions = Avalonia.Animation.Transitions;

namespace HyPrism.UI.Components.Buttons.IconButton;

public class IconButton : Button
{
    protected override Type StyleKeyOverride => typeof(IconButton);
    private IDisposable? _resourceSubscription;
    private IDisposable? _brushSubscription;
    
    private Control? _baseIconWrapper;
    private Control? _hoverOverlay;
    private DoubleTransition? _baseOpacityTransition;
    
    public static readonly StyledProperty<IBrush?> ButtonBackgroundProperty =
        AvaloniaProperty.Register<IconButton, IBrush?>(nameof(ButtonBackground), Brushes.Transparent);

    public IBrush? ButtonBackground
    {
        get => GetValue(ButtonBackgroundProperty);
        set => SetValue(ButtonBackgroundProperty, value);
    }
    
    public static readonly StyledProperty<string?> IconPathProperty =
        AvaloniaProperty.Register<IconButton, string?>(nameof(IconPath));

    public string? IconPath
    {
        get => GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<IconButton, double>(nameof(IconSize), 24.0);

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly StyledProperty<string> HoverCssProperty =
        AvaloniaProperty.Register<IconButton, string>(nameof(HoverCss), "* { stroke: #FFA845; fill: none; }");

    public string HoverCss
    {
        get => GetValue(HoverCssProperty);
        private set => SetValue(HoverCssProperty, value);
    }

    public IconButton()
    {
        // Initialize with accent color as soon as possible
        UpdateHoverCss();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ForegroundProperty)
        {
            UpdateHoverCss();
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        
        _baseIconWrapper = e.NameScope.Find<Control>("PART_BaseIconWrapper");
        _hoverOverlay = e.NameScope.Find<Control>("PART_HoverOverlay");
        
        // Setup Overlay Transitions (Always 0.2s)
        if (_hoverOverlay != null)
        {
            _hoverOverlay.Transitions = new AvaTransitions
            {
                new DoubleTransition 
                { 
                    Property = Visual.OpacityProperty, 
                    Duration = TimeSpan.FromSeconds(0.2),
                    Easing = new LinearEasing()
                }
            };
        }
        
        // Setup Base Icon Transitions (Default/Exit is Fast 0.05s)
        if (_baseIconWrapper != null)
        {
            _baseOpacityTransition = new DoubleTransition 
            { 
                Property = Visual.OpacityProperty, 
                Duration = TimeSpan.FromSeconds(0.05),
                Easing = new LinearEasing()
            };
            
            _baseIconWrapper.Transitions = new AvaTransitions { _baseOpacityTransition };
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        
        // Enter: Slow Fade Out for Base (0.2s), Show Overlay
        if (_baseIconWrapper != null && _baseOpacityTransition != null)
        {
            _baseOpacityTransition.Duration = TimeSpan.FromSeconds(0.2);
            _baseIconWrapper.Opacity = 0;
        }
        
        if (_hoverOverlay != null)
        {
            _hoverOverlay.Opacity = 1;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        
        // Exit: Fast recovery for Base (0.05s), Hide Overlay
        if (_baseIconWrapper != null && _baseOpacityTransition != null)
        {
            _baseOpacityTransition.Duration = TimeSpan.FromSeconds(0.05);
            _baseIconWrapper.Opacity = 1;
        }
        
        if (_hoverOverlay != null)
        {
            _hoverOverlay.Opacity = 0;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        
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
        HoverCss = $"* {{ stroke: {hexColor}; fill: none; }}";
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _brushSubscription?.Dispose();
        _resourceSubscription?.Dispose();
        _brushSubscription = null;
        _resourceSubscription = null;
    }

    private void UpdateHoverCss()
    {
        // Fallback / Initial manual update if needed (though observables should cover it)
        if (Application.Current?.TryGetResource("SystemAccentBrush", null, out var resource) == true 
            && resource is ISolidColorBrush accentBrush)
        {
            UpdateCssFromColor(accentBrush.Color);
        }
        else
        {
            HoverCss = "* { stroke: #FFA845; fill: none; }";
        }
    }
}
