using System.Text;

namespace HyPrism.Services.Core.Infrastructure;

/// <summary>
/// Redirects Console.Out / Console.Error to the HyPrism structured Logger
/// so that Sciter's own diagnostic output (version banners, CSS warnings, JS
/// exceptions) is captured in the log file instead of going to raw stdout.
///
/// Logger.WriteToConsole internally uses the saved _originalOut reference
/// (set via Logger.CaptureOriginalConsole), so our own terminal output is
/// unaffected after Console.SetOut points here.
/// </summary>
internal sealed class SciterStdoutRedirector : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    // Sciter sometimes writes partial lines via Write(string).
    // Buffer them per-call and flush on newline / WriteLine.
    private readonly System.Text.StringBuilder _buf = new();

    public override void Write(char value)
    {
        if (value == '\n')
            Flush();
        else if (value != '\r')
            _buf.Append(value);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (value.Contains('\n'))
        {
            var parts = value.Split('\n');
            for (var i = 0; i < parts.Length - 1; i++)
            {
                _buf.Append(parts[i].TrimEnd('\r'));
                Flush();
            }
            _buf.Append(parts[^1]);
        }
        else
        {
            _buf.Append(value);
        }
    }

    public override void WriteLine(string? value)
    {
        _buf.Append(value?.TrimEnd('\r', '\n') ?? string.Empty);
        Flush();
    }

    public override void Flush()
    {
        var line = _buf.ToString().Trim();
        _buf.Clear();
        if (!string.IsNullOrEmpty(line))
            Logger.Debug("Sciter", line);
    }
}
