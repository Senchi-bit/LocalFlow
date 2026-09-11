using System.Windows;

namespace LocalFlow;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args is ["--self-test"])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                await LoopbackSelfTest.RunAsync();
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Shutdown(1);
            }

            return;
        }

        new MainWindow().Show();
    }
}
