using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace HyPrism.UI.Components.Buttons.CloseButton;

public partial class CloseButton : UserControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<CloseButton, ICommand?>(nameof(Command));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
    
    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<CloseButton, object?>(nameof(CommandParameter));

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public CloseButton()
    {
        InitializeComponent();
    }
}
