using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using AvaTransitions = Avalonia.Animation.Transitions;

namespace HyPrism.UI.Behaviors;

/// <summary>
/// A utility class that provides asymmetric fade animation on hover.
/// The enter animation is slower for a smooth appearance, while the exit is faster
/// for snappy feedback.
/// </summary>
/// <example>
/// <![CDATA[
/// // Usage in code-behind or ViewModel:
/// var fadeBehavior = new AsymmetricFadeBehavior(
///     baseControl: myBaseIcon,
///     overlayControl: myHoverOverlay,
///     enterDuration: TimeSpan.FromSeconds(0.2),
///     exitDuration: TimeSpan.FromSeconds(0.05)
/// );
/// fadeBehavior.Attach(myButton);
/// ]]>
/// </example>
public class AsymmetricFadeBehavior : IDisposable
{
    private readonly Control? _baseControl;
    private readonly Control? _overlayControl;
    private readonly TimeSpan _enterDuration;
    private readonly TimeSpan _exitDuration;
    private readonly double _hoverOpacity;
    private readonly double _baseOpacity;
    
    private DoubleTransition? _overlayTransition;
    private DoubleTransition? _baseTransition;
    private Control? _attachedControl;
    
    /// <summary>
    /// Creates a new asymmetric fade behavior.
    /// </summary>
    /// <param name="overlayControl">The control to fade IN on hover (typically the accent-colored overlay).</param>
    /// <param name="baseControl">Optional: The control to fade OUT on hover (typically the base icon).</param>
    /// <param name="enterDuration">Duration of fade-in animation.</param>
    /// <param name="exitDuration">Duration of fade-out animation (should be faster for snappy feel).</param>
    /// <param name="hoverOpacity">Target opacity for overlay when hovered. Default: 1.0</param>
    /// <param name="baseOpacity">Target opacity for overlay when not hovered. Default: 0.0</param>
    public AsymmetricFadeBehavior(
        Control? overlayControl = null,
        Control? baseControl = null,
        TimeSpan? enterDuration = null,
        TimeSpan? exitDuration = null,
        double hoverOpacity = 1.0,
        double baseOpacity = 0.0)
    {
        _overlayControl = overlayControl;
        _baseControl = baseControl;
        _enterDuration = enterDuration ?? TimeSpan.FromSeconds(0.2);
        _exitDuration = exitDuration ?? TimeSpan.FromSeconds(0.05);
        _hoverOpacity = hoverOpacity;
        _baseOpacity = baseOpacity;
    }
    
    /// <summary>
    /// Attaches the behavior to a control. The behavior will respond to pointer enter/exit events.
    /// </summary>
    public void Attach(Control control)
    {
        _attachedControl = control;
        control.PointerEntered += OnPointerEntered;
        control.PointerExited += OnPointerExited;
        
        EnsureTransitions();
    }
    
    /// <summary>
    /// Detaches the behavior from the control.
    /// </summary>
    public void Detach()
    {
        if (_attachedControl != null)
        {
            _attachedControl.PointerEntered -= OnPointerEntered;
            _attachedControl.PointerExited -= OnPointerExited;
            _attachedControl = null;
        }
    }
    
    private void EnsureTransitions()
    {
        if (_overlayControl != null && _overlayTransition == null)
        {
            _overlayTransition = new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = _enterDuration,
                Easing = new LinearEasing()
            };
            _overlayControl.Transitions ??= new AvaTransitions();
            _overlayControl.Transitions.Add(_overlayTransition);
        }

        if (_baseControl != null && _baseTransition == null)
        {
            _baseTransition = new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = _exitDuration,
                Easing = new LinearEasing()
            };
            _baseControl.Transitions ??= new AvaTransitions();
            _baseControl.Transitions.Add(_baseTransition);
        }
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        // Slow enter for overlay (appears smoothly)
        if (_overlayTransition != null)
        {
            _overlayTransition.Duration = _enterDuration;
        }
        
        if (_overlayControl != null)
        {
            _overlayControl.Opacity = _hoverOpacity;
        }

        // Slow fade-out for base layer
        if (_baseTransition != null)
        {
            _baseTransition.Duration = _enterDuration;
        }
        
        if (_baseControl != null)
        {
            _baseControl.Opacity = _baseOpacity;
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        // Fast exit for overlay (disappears snappily)
        if (_overlayTransition != null)
        {
            _overlayTransition.Duration = _exitDuration;
        }
        
        if (_overlayControl != null)
        {
            _overlayControl.Opacity = _baseOpacity;
        }

        // Fast recovery for base layer
        if (_baseTransition != null)
        {
            _baseTransition.Duration = _exitDuration;
        }
        
        if (_baseControl != null)
        {
            _baseControl.Opacity = 1.0;
        }
    }
    
    public void Dispose()
    {
        Detach();
    }
}
