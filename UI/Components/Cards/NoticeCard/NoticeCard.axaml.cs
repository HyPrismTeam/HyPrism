using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HyPrism.UI.Components.Cards.NoticeCard;

public partial class NoticeCard : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<NoticeCard, string>(nameof(Text), "");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<string> IconPathProperty =
        AvaloniaProperty.Register<NoticeCard, string>(nameof(IconPath), "");

    public string IconPath
    {
        get => GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    public static readonly StyledProperty<string> ThemeColorProperty =
        AvaloniaProperty.Register<NoticeCard, string>(nameof(ThemeColor), "#F59E0B");

    public string ThemeColor
    {
        get => GetValue(ThemeColorProperty);
        set => SetValue(ThemeColorProperty, value);
    }

    // Direct property for Binding the CSS
    public static readonly DirectProperty<NoticeCard, string> SvgCssProperty =
        AvaloniaProperty.RegisterDirect<NoticeCard, string>(
            nameof(SvgCss),
            o => o.SvgCss);

    private string _svgCss = "* { stroke: #F59E0B; }";
    public string SvgCss
    {
        get => _svgCss;
        private set => SetAndRaise(SvgCssProperty, ref _svgCss, value);
    }

    public NoticeCard()
    {
        InitializeComponent();
        UpdateCss();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeColorProperty)
        {
            UpdateCss();
        }
    }

    private void UpdateCss()
    {
        // Assuming icons are stroke-based. Use 'fill' if icons are shapes.
        // Most UI icons in this project seem to use stroke.
        SvgCss = $"* {{ stroke: {ThemeColor}; }}";
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
