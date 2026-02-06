using System;
using System.Reactive;
using ReactiveUI;

namespace HyPrism.UI.Views.DeleteProfileConfirmationView;

/// <summary>
/// ViewModel for the delete profile confirmation modal dialog.
/// </summary>
public class DeleteProfileConfirmationViewModel : ReactiveObject
{
    private double _overlayOpacity;
    private double _dialogOpacity;
    private string _profileName = string.Empty;

    public DeleteProfileConfirmationViewModel()
    {
        CancelCommand = ReactiveCommand.Create(OnCancel);
        ConfirmCommand = ReactiveCommand.Create(OnConfirm);
    }

    /// <summary>
    /// Opacity of the background overlay (0-1).
    /// </summary>
    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set => this.RaiseAndSetIfChanged(ref _overlayOpacity, value);
    }

    /// <summary>
    /// Opacity of the dialog box (0-1).
    /// </summary>
    public double DialogOpacity
    {
        get => _dialogOpacity;
        set => this.RaiseAndSetIfChanged(ref _dialogOpacity, value);
    }

    /// <summary>
    /// Name of the profile being deleted.
    /// </summary>
    public string ProfileName
    {
        get => _profileName;
        set => this.RaiseAndSetIfChanged(ref _profileName, value);
    }

    /// <summary>
    /// Command to cancel/close the dialog.
    /// </summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    /// <summary>
    /// Command to confirm deletion.
    /// </summary>
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

    /// <summary>
    /// Event raised when user cancels the dialog.
    /// </summary>
    public event EventHandler? Cancelled;

    /// <summary>
    /// Event raised when user confirms deletion.
    /// </summary>
    public event EventHandler? Confirmed;

    /// <summary>
    /// Shows the dialog with animation.
    /// </summary>
    public void Show(string profileName)
    {
        ProfileName = profileName;
        OverlayOpacity = 1;
        DialogOpacity = 1;
    }

    /// <summary>
    /// Hides the dialog with animation.
    /// </summary>
    public void Hide()
    {
        OverlayOpacity = 0;
        DialogOpacity = 0;
    }

    private void OnCancel()
    {
        Hide();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnConfirm()
    {
        Confirmed?.Invoke(this, EventArgs.Empty);
    }
}
