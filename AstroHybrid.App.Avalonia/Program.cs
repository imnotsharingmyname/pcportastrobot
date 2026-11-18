using System;
using Avalonia;
using Avalonia.Media;

namespace AstroHybrid.App.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        // The UI font is Selawik (Segoe UI metrics). Inter stays embedded so the
        // symbols Selawik does not carry (★ U+2605, ✓ U+2713) still resolve.
        .With(new FontManagerOptions
        {
            FontFallbacks = new[]
            {
                new FontFallback { FontFamily = new FontFamily("Inter") }
            }
        })
        .LogToTrace();
}
