using System;
using System.Reactive;
using ReactiveUI;
using HyPrism.Services;
using HyPrism.Services.Core;

namespace HyPrism.UI.Components.Dashboard.Header;

public class HeaderViewModel : ReactiveObject
{
    private readonly ConfigService _configService;
    
    // Commands exposed to UI
    public ReactiveCommand<Unit, Unit> ToggleProfileEditorCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleSettingsCommand { get; }
    
    // Properties
    private string _nick;
    public string Nick
    {
        get => _nick;
        set => this.RaiseAndSetIfChanged(ref _nick, value);
    }
    
    public IObservable<string> AppVersion { get; }

    public HeaderViewModel(
        ConfigService configService,
        Action toggleProfileEditor, 
        Action toggleSettings,
        LocalizationService localizationService)
    {
        _configService = configService;
        _nick = _configService.Configuration.Nick;
        
        // Localization
        var loc = localizationService;
        AppVersion = loc.GetObservable("app.version");
        
        // Commands
        ToggleProfileEditorCommand = ReactiveCommand.Create(toggleProfileEditor);
        ToggleSettingsCommand = ReactiveCommand.Create(toggleSettings);
    }

    public void RefreshNick()
    {
        Nick = _configService.Configuration.Nick;
    }
}
