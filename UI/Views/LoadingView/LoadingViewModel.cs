using System;
using System.Threading.Tasks;
using ReactiveUI;

namespace HyPrism.UI.Views.LoadingView;

public class LoadingViewModel : ReactiveObject
{
    private bool _isLoading = true;
    private string _loadingText = "Loading";
    private double _logoOpacity = 0;
    private double _contentOpacity = 0;
    private double _spinnerOpacity = 0;
    
    // Animation durations for XAML binding
    public string LogoFadeDuration => LoadingAnimationConstants.GetDurationString(LoadingAnimationConstants.LogoFadeDuration);
    public string ContentFadeDuration => LoadingAnimationConstants.GetDurationString(LoadingAnimationConstants.ContentFadeDuration);
    public string SpinnerFadeDuration => LoadingAnimationConstants.GetDurationString(LoadingAnimationConstants.SpinnerFadeDuration);
    public string ExitAnimationDuration => LoadingAnimationConstants.GetDurationString(LoadingAnimationConstants.ExitAnimationDuration);
    
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    // Alias for IsLoading or visibility control
    public bool IsVisible
    {
        get => _isLoading;
        set => IsLoading = value;
    }

    private string _currentState = "";
    public string CurrentState
    {
        get => _currentState;
        set => this.RaiseAndSetIfChanged(ref _currentState, value);
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set => this.RaiseAndSetIfChanged(ref _progress, value);
    }
    
    public void Update(string state, string message, double progress)
    {
        CurrentState = state;
        LoadingText = message;
        Progress = progress;
    }
    
    public string LoadingText
    {
        get => _loadingText;
        set => this.RaiseAndSetIfChanged(ref _loadingText, value);
    }
    
    public double LogoOpacity
    {
        get => _logoOpacity;
        set => this.RaiseAndSetIfChanged(ref _logoOpacity, value);
    }
    
    public double ContentOpacity
    {
        get => _contentOpacity;
        set => this.RaiseAndSetIfChanged(ref _contentOpacity, value);
    }
    
    public double SpinnerOpacity
    {
        get => _spinnerOpacity;
        set => this.RaiseAndSetIfChanged(ref _spinnerOpacity, value);
    }
    
    public LoadingViewModel()
    {
        _ = StartEntranceAnimationAsync();
    }
    
    private async Task StartEntranceAnimationAsync()
    {
        await Task.Delay(LoadingAnimationConstants.InitialDelay);
        
        LogoOpacity = 1;
        await Task.Delay(LoadingAnimationConstants.LogoFadeDelay);
        
        ContentOpacity = 1;
        SpinnerOpacity = 1;
        
        await Task.Delay(LoadingAnimationConstants.MinimumVisibleTime);
    }
    
    public async Task CompleteLoadingAsync()
    {
        SpinnerOpacity = 0;
        await Task.Delay(LoadingAnimationConstants.SpinnerFadeWaitTime);
        
        await Task.Delay(LoadingAnimationConstants.PreExitDelay);
        
        SpinnerOpacity = 0;
        await Task.Delay(LoadingAnimationConstants.SpinnerFadeWaitTime);
        
        LogoOpacity = 0;
        ContentOpacity = 0;
        await Task.Delay(LoadingAnimationConstants.LogoFadeDelay);
        
        await Task.Delay(LoadingAnimationConstants.PreExitDelay);
        
        IsLoading = false;
    }
}