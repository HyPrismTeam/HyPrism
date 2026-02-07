using System;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Windows.Input;

namespace HyPrism.UI.Components.Cards.NewsCard;

public partial class NewsCard : UserControl
{
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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // Dispose the loaded bitmap to prevent native SkiaSharp memory leaks.
        // Without RAM cache, each Image gets a unique Bitmap instance that must
        // be explicitly disposed — GC does not track native allocations effectively.
        if (NewsImage is not null)
        {
            var bitmap = NewsImage.Source;
            ImageLoader.SetSource(NewsImage, null);
            NewsImage.Source = null;
            (bitmap as IDisposable)?.Dispose();
        }
    }
}