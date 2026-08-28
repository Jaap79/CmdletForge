using System.Windows;
using System.Windows.Threading;
using CmdletForge.Services;

namespace CmdletForge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        AppLog.Initialize();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Onverwerkte AppDomain-fout", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Onverwerkte taakfout", args.Exception);
            args.SetObserved();
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            window.OpenFromCommandLine(e.Args[0]);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Onverwerkte UI-fout", e.Exception);
        MessageBox.Show(
            $"Cmdlet Forge liep tegen een onverwachte fout aan.\n\n{e.Exception.Message}\n\nDetails staan in het applicatielogboek.",
            "Cmdlet Forge - fout",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
