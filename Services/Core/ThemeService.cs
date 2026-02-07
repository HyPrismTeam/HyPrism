using Avalonia;
using Avalonia.Media;
using ReactiveUI;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HyPrism.Services.Core;

public class ThemeService : ReactiveObject, IThemeService, IDisposable
{
    private CancellationTokenSource? _animationCts;

    public ThemeService()
    {
    }

    /// <summary>
    /// Applies the accent color from the configuration to the Application Resources with smooth transition.
    /// </summary>
    public void ApplyAccentColor(string hexColor)
    {
        if (Color.TryParse(hexColor, out Color newColor))
        {
            if (Application.Current != null)
            {
                // Cancel and dispose previous animation CTS to prevent memory leak
                _animationCts?.Cancel();
                _animationCts?.Dispose();
                _animationCts = new CancellationTokenSource();
                
                // Update the Color resource immediately (structural)
                Application.Current.Resources["SystemAccentColor"] = newColor;

                // Handle Brush animation
                if (Application.Current.Resources.TryGetResource("SystemAccentBrush", null, out var resource) && 
                    resource is SolidColorBrush brush)
                {
                    var oldColor = brush.Color;
                    if (oldColor != newColor)
                    {
                        // Run animation fire-and-forget
                        _ = AnimateColorAsync(brush, oldColor, newColor, _animationCts.Token);
                    }
                }
                else
                {
                    // If brush doesn't exist or isn't a SolidColorBrush, create/replace it
                    Application.Current.Resources["SystemAccentBrush"] = new SolidColorBrush(newColor);
                }
            }
        }
    }

    private async Task AnimateColorAsync(SolidColorBrush brush, Color start, Color end, CancellationToken token)
    {
        // Animation settings
        const int durationMs = 250;
        const int intervalMs = 16; // ~60 FPS
        
        var startTime = DateTime.Now;
        var duration = TimeSpan.FromMilliseconds(durationMs);

        try
        {
            while (true)
            {
                if (token.IsCancellationRequested) break;

                var elapsed = DateTime.Now - startTime;
                var t = Math.Clamp(elapsed.TotalMilliseconds / durationMs, 0, 1);

                // CubicEaseOut: 1 - (1 - t)^3
                var easeT = 1 - Math.Pow(1 - t, 3);
                
                // Interpolate
                var r = (byte)(start.R + (end.R - start.R) * easeT);
                var g = (byte)(start.G + (end.G - start.G) * easeT);
                var b = (byte)(start.B + (end.B - start.B) * easeT);
                var a = (byte)(start.A + (end.A - start.A) * easeT);

                brush.Color = Color.FromUInt32((uint)((a << 24) | (r << 16) | (g << 8) | b));

                if (t >= 1.0) break;
                
                await Task.Delay(intervalMs, token);
            }
        }
        catch (TaskCanceledException)
        {
            // Ignore cancellation
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                brush.Color = end;
            }
        }
    }

    /// <summary>
    /// Initialize with current config
    /// </summary>
    public void Initialize(string initialColor)
    {
        // For initialization, we set directly without animation
        if (Color.TryParse(initialColor, out Color color))
        {
            if (Application.Current != null)
            {
                Application.Current.Resources["SystemAccentColor"] = color;
                // Ensure we have a mutable instance
                Application.Current.Resources["SystemAccentBrush"] = new SolidColorBrush(color);
            }
        }
    }

    public void Dispose()
    {
        _animationCts?.Cancel();
        _animationCts?.Dispose();
        _animationCts = null;
    }
}
