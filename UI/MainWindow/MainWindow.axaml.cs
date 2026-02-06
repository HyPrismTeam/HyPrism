using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HyPrism.UI.MainWindow;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        try
        {
            var assets = AssetLoader.Open(new Uri("avares://HyPrism/Assets/logo.png"));
            Icon = new WindowIcon(assets);
        }
        catch
        {
            // Fallback for default icon if loading fails
        }
    }
}
