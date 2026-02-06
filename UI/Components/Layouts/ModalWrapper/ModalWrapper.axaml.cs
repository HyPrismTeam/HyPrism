using Avalonia;
using Avalonia.Controls;

namespace HyPrism.UI.Components.Layouts.ModalWrapper;

public partial class ModalWrapper : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ModalWrapper, bool>(nameof(IsOpen));

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public ModalWrapper()
    {
        InitializeComponent();
    }
}
