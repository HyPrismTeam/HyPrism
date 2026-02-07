using Avalonia;
using Avalonia.Controls;

namespace HyPrism.UI.Components.Layouts.ModalWrapper;

/// <summary>
/// A ContentControl that defers materialization of its ContentTemplate
/// until IsActive is first set to true. After first activation, the content
/// stays alive and is never recreated — only shown/hidden.
/// 
/// Usage in XAML:
/// <![CDATA[
/// <layouts:LazyContentControl IsActive="{Binding IsOverlayOpen}" DataContext="{Binding SomeViewModel}">
///     <layouts:LazyContentControl.ContentTemplate>
///         <DataTemplate>
///             <local:HeavyView />
///         </DataTemplate>
///     </layouts:LazyContentControl.ContentTemplate>
/// </layouts:LazyContentControl>
/// ]]>
/// </summary>
public class LazyContentControl : ContentControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LazyContentControl, bool>(nameof(IsActive));

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private bool _hasBeenActivated;

    static LazyContentControl()
    {
        IsActiveProperty.Changed.AddClassHandler<LazyContentControl>(
            (control, _) => control.OnIsActiveChanged());
    }

    private void OnIsActiveChanged()
    {
        if (IsActive && !_hasBeenActivated)
        {
            _hasBeenActivated = true;
            // Setting Content triggers ContentTemplate materialization
            Content = DataContext;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // Keep Content in sync after first activation
        if (_hasBeenActivated)
            Content = DataContext;
    }
}
