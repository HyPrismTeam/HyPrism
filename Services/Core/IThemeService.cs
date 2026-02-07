namespace HyPrism.Services.Core;

public interface IThemeService
{
    void ApplyAccentColor(string hexColor);
    void Initialize(string initialColor);
}
