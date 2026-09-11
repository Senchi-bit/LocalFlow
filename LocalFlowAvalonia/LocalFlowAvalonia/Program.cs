using Avalonia;
using System;

namespace LocalFlowAvalonia;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            try
            {
                LoopbackSelfTest.RunAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }

            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
