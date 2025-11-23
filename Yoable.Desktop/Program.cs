using Avalonia;
using System;
using System.Runtime.InteropServices;

namespace Yoable.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        // Platform-specific setup
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Mac OS specific setup if needed
            builder = builder.With(new MacOSPlatformOptions
            {
                DisableDefaultApplicationMenuItems = false,
            });
        }

        return builder;
    }
}
