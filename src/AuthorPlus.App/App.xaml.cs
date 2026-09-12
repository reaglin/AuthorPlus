using System.Windows;
using Eaglin.AiManager;

namespace AuthorPlus.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The AI Manager's shared log (Documents\AiManager\logs), tagged with this app's name.
        // MainWindow's AiHub.Open("AuthorPlus") initialises it the same way; doing it here too
        // means startup problems before the window exists are still recorded.
        ActivityLog.Initialize(AiPaths.DefaultRoot, "AuthorPlus");
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
