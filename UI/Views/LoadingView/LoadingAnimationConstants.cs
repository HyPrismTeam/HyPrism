namespace HyPrism.UI.Views.LoadingView;

/// <summary>
/// Animation timing constants for LoadingView to keep XAML and code in sync
/// </summary>
public static class LoadingAnimationConstants
{
    // Entrance animations
    public const double LogoFadeDuration = 0.6;
    public const double ContentFadeDuration = 0.6;
    public const double SpinnerFadeDuration = 1.2;
    
    // Exit animations
    public const double ExitAnimationDuration = 0.8;
    
    // Delays
    public const int InitialDelay = 300;
    public const int LogoFadeDelay = 600;
    public const int MinimumVisibleTime = 2000;
    public const int SpinnerFadeWaitTime = 1200;
    public const int PreExitDelay = 500;
    public const int ExitAnimationWaitTime = 900;
    
    // Helper method to get duration as TimeSpan string for XAML
    public static string GetDurationString(double seconds) 
        => $"0:0:{seconds:F1}";
}
