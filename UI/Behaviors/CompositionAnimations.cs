using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using System;

namespace HyPrism.UI.Behaviors;

/// <summary>
/// Provides Composition API implicit animations via attached properties.
/// These animations run on the render thread, keeping the UI thread free.
///
/// Usage in XAML:
/// <![CDATA[
/// <Border behaviors:CompositionAnimations.ImplicitOffset="True"
///         behaviors:CompositionAnimations.ImplicitOpacity="True"
///         behaviors:CompositionAnimations.Duration="0.3" />
/// ]]>
///
/// Or from code-behind:
/// <![CDATA[
///     CompositionAnimations.ApplyImplicitAnimations(control, offset: true, opacity: true, scale: true);
/// ]]>
/// </summary>
public static class CompositionAnimations
{
    #region Attached Properties

    public static readonly AttachedProperty<bool> ImplicitOffsetProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("ImplicitOffset", typeof(CompositionAnimations));

    public static readonly AttachedProperty<bool> ImplicitOpacityProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("ImplicitOpacity", typeof(CompositionAnimations));

    public static readonly AttachedProperty<bool> ImplicitScaleProperty =
        AvaloniaProperty.RegisterAttached<Visual, bool>("ImplicitScale", typeof(CompositionAnimations));

    public static readonly AttachedProperty<double> DurationProperty =
        AvaloniaProperty.RegisterAttached<Visual, double>("Duration", typeof(CompositionAnimations), 0.3);

    public static bool GetImplicitOffset(Visual element) => element.GetValue(ImplicitOffsetProperty);
    public static void SetImplicitOffset(Visual element, bool value) => element.SetValue(ImplicitOffsetProperty, value);

    public static bool GetImplicitOpacity(Visual element) => element.GetValue(ImplicitOpacityProperty);
    public static void SetImplicitOpacity(Visual element, bool value) => element.SetValue(ImplicitOpacityProperty, value);

    public static bool GetImplicitScale(Visual element) => element.GetValue(ImplicitScaleProperty);
    public static void SetImplicitScale(Visual element, bool value) => element.SetValue(ImplicitScaleProperty, value);

    public static double GetDuration(Visual element) => element.GetValue(DurationProperty);
    public static void SetDuration(Visual element, double value) => element.SetValue(DurationProperty, value);

    #endregion

    static CompositionAnimations()
    {
        ImplicitOffsetProperty.Changed.AddClassHandler<Visual>(OnAnimationPropertyChanged);
        ImplicitOpacityProperty.Changed.AddClassHandler<Visual>(OnAnimationPropertyChanged);
        ImplicitScaleProperty.Changed.AddClassHandler<Visual>(OnAnimationPropertyChanged);
    }

    private static void OnAnimationPropertyChanged(Visual visual, AvaloniaPropertyChangedEventArgs args)
    {
        if (visual is not Control control)
            return;

        // Defer until control is attached to visual tree
        if (control.IsLoaded)
            ApplyFromAttachedProperties(control);
        else
            control.Loaded += OnControlLoaded;
    }

    private static void OnControlLoaded(object? sender, EventArgs e)
    {
        if (sender is not Control control)
            return;

        control.Loaded -= OnControlLoaded;
        ApplyFromAttachedProperties(control);
    }

    private static void ApplyFromAttachedProperties(Control control)
    {
        var offset = GetImplicitOffset(control);
        var opacity = GetImplicitOpacity(control);
        var scale = GetImplicitScale(control);

        if (offset || opacity || scale)
        {
            var duration = GetDuration(control);
            ApplyImplicitAnimations(control, offset, opacity, scale, TimeSpan.FromSeconds(duration));
        }
    }

    /// <summary>
    /// Applies implicit Composition animations to a control.
    /// These animate automatically when the corresponding property changes.
    /// Runs entirely on the render thread — zero UI thread overhead.
    /// </summary>
    public static void ApplyImplicitAnimations(
        Visual visual,
        bool offset = false,
        bool opacity = false,
        bool scale = false,
        TimeSpan? duration = null)
    {
        var compositionVisual = ElementComposition.GetElementVisual(visual);
        if (compositionVisual is null)
            return;

        var compositor = compositionVisual.Compositor;
        var dur = duration ?? TimeSpan.FromSeconds(0.3);
        var collection = compositor.CreateImplicitAnimationCollection();

        if (offset)
        {
            var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
            offsetAnimation.Target = "Offset";
            offsetAnimation.InsertExpressionKeyFrame(1.0f, "this.FinalValue");
            offsetAnimation.Duration = dur;
            collection["Offset"] = offsetAnimation;
        }

        if (opacity)
        {
            var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
            opacityAnimation.Target = "Opacity";
            opacityAnimation.InsertExpressionKeyFrame(1.0f, "this.FinalValue");
            opacityAnimation.Duration = dur;
            collection["Opacity"] = opacityAnimation;
        }

        if (scale)
        {
            var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
            scaleAnimation.Target = "Scale";
            scaleAnimation.InsertExpressionKeyFrame(1.0f, "this.FinalValue");
            scaleAnimation.Duration = dur;
            collection["Scale"] = scaleAnimation;
        }

        compositionVisual.ImplicitAnimations = collection;
    }

    /// <summary>
    /// Plays an explicit slide-in animation on a control.
    /// Useful for entry animations of overlays and panels.
    /// </summary>
    public static void PlaySlideIn(Visual visual, float offsetX = 0, float offsetY = -20, TimeSpan? duration = null)
    {
        var compositionVisual = ElementComposition.GetElementVisual(visual);
        if (compositionVisual is null)
            return;

        var compositor = compositionVisual.Compositor;
        var animation = compositor.CreateVector3KeyFrameAnimation();

        animation.InsertKeyFrame(0f, new System.Numerics.Vector3(
            (float)(compositionVisual.Offset.X + offsetX),
            (float)(compositionVisual.Offset.Y + offsetY),
            (float)compositionVisual.Offset.Z));
        animation.InsertKeyFrame(1f, new System.Numerics.Vector3(
            (float)compositionVisual.Offset.X,
            (float)compositionVisual.Offset.Y,
            (float)compositionVisual.Offset.Z));
        animation.Duration = duration ?? TimeSpan.FromMilliseconds(300);

        compositionVisual.StartAnimation("Offset", animation);
    }

    /// <summary>
    /// Plays an explicit scale-in animation (zoom from slightly smaller).
    /// Great for modal/dialog appear animations.
    /// </summary>
    public static void PlayScaleIn(Visual visual, float fromScale = 0.95f, TimeSpan? duration = null)
    {
        var compositionVisual = ElementComposition.GetElementVisual(visual);
        if (compositionVisual is null)
            return;

        var compositor = compositionVisual.Compositor;
        var animation = compositor.CreateVector3KeyFrameAnimation();

        animation.InsertKeyFrame(0f, new System.Numerics.Vector3(fromScale, fromScale, 1f));
        animation.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f));
        animation.Duration = duration ?? TimeSpan.FromMilliseconds(250);

        compositionVisual.StartAnimation("Scale", animation);
    }
}
