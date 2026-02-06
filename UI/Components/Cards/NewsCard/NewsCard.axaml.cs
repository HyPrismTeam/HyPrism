using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Windows.Input;

namespace HyPrism.UI.Components.Cards.NewsCard;

public partial class NewsCard : UserControl
{
    // We expect the DataContext to be NewsItemResponse
    
    // Command to execute when card is clicked (passed via binding usually, but we can expose it if needed)
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<NewsCard, ICommand?>(nameof(Command));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
    
    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<NewsCard, object?>(nameof(CommandParameter));

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public NewsCard()
    {
        InitializeComponent();
    }
}