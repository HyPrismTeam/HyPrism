using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace HyPrism.UI.Components.Navigation.SidebarItem;

public partial class SidebarItem : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SidebarItem, string>(nameof(Text));

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    
    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<SidebarItem, object?>(nameof(Icon));

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<SidebarItem, bool>(nameof(IsSelected));

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<SidebarItem, ICommand?>(nameof(Command));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
    
    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<SidebarItem, object?>(nameof(CommandParameter));

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public SidebarItem()
    {
        InitializeComponent();
    }
}
