using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace HyPrism.UI.Components.Inputs.CheckboxInput;

public partial class CheckboxInput : UserControl
{
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<CheckboxInput, bool>(nameof(IsChecked), defaultBindingMode: BindingMode.TwoWay);

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }
    
    public CheckboxInput()
    {
        InitializeComponent();
    }
    
    private void OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        IsChecked = !IsChecked;
    }
}
