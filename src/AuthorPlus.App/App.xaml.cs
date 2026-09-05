using System.IO;
using System.Windows;
using AuthorPlus.AI;

namespace AuthorPlus.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ActivityLog.Initialize(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AuthorPlus", "logs"));
        ActivityLog.Info("App", $"AuthorPlus {typeof(App).Assembly.GetName().Version} starting");

        DispatcherUnhandledException += (_, args) =>
        {
            ActivityLog.Error("App", "Unhandled UI exception", args.Exception);
            MessageBox.Show(
                $"Something went wrong:\n\n{args.Exception.Message}\n\nYour book files are saved item by item, so nothing already saved is lost.",
                "AuthorPlus", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
