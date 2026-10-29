using Avalonia;

namespace Polyglotta.Tray;

/// <summary>
/// Entry point of the background agent (ADR-0007). A <c>WinExe</c>: it starts without
/// a console and without a window, and only the tray icon shows that it is there.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
