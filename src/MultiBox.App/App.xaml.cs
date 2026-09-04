using System.Windows;

namespace MultiBox.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Velopack hooks must run first: they handle install/update/uninstall
        // lifecycle events and exit early during them.
        Velopack.VelopackApp.Build().Run();
        UpdateChecker.Start();

        base.OnStartup(e);

        // A crash in a background timer should tell the user what happened rather than
        // vanishing the window mid-fight.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "MultiBox error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
