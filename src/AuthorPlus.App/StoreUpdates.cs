using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.Services.Store;

namespace AuthorPlus.App;

/// <summary>
/// Help › Check for a new version (Ron, 2026-10-30, for every Store program; ported from Accessibility+). Asks the Microsoft Store whether a newer version
/// is published; if so, asks the person, then lets the Store download and install it (Windows shows its own progress). A copy
/// not installed from the Store (a hand-test build) cannot ask, and says so, offering the Store page instead.
/// </summary>
public static class StoreUpdates
{
    public const string ProgramName = "Author+";
    public const string StorePage = "https://apps.microsoft.com/detail/9MZCBQVSSDVG";   // Store ID from Partner Center, 2026-11-05

    /// <summary>The version running now, as people read it (1.0.1).</summary>
    public static string CurrentVersion
    {
        get
        {
            var v = typeof(StoreUpdates).Assembly.GetName().Version;
            return v == null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static async Task CheckAsync(Window owner)
    {
        const string title = ProgramName + " — Check for a new version";
        if (!IsFromStore())
        {
            if (MessageBox.Show(owner,
                    $"This copy of {ProgramName} ({CurrentVersion}) was not installed from the Microsoft Store, so it cannot check for a " +
                    "new version itself.\n\nOpen the program's page on the Microsoft Store to see the latest version?",
                    title, MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                OpenStorePage(owner);
            return;
        }

        IReadOnlyList<StorePackageUpdate> updates;
        StoreContext store;
        // "Asking the Microsoft Store…" while it runs (USABILITY-CHECKLIST 6.5): the busy pointer and the owner's title.
        string ownerTitle = owner.Title;
        owner.Title = "Asking the Microsoft Store… — " + ownerTitle;
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        try
        {
            store = StoreContext.GetDefault();
            // A desktop program must tell the Store which window its own dialogs belong to.
            WinRT.Interop.InitializeWithWindow.Initialize(store, new WindowInteropHelper(owner).Handle);
            updates = await store.GetAppAndOptionalStorePackageUpdatesAsync();
        }
        catch (Exception ex)
        {
            owner.Title = ownerTitle;
            System.Windows.Input.Mouse.OverrideCursor = null;
            if (MessageBox.Show(owner,
                    $"The Microsoft Store could not be asked about a new version. Check that you are connected to the internet.\n\n" +
                    $"Open {ProgramName}'s page on the Microsoft Store instead?\n\nDetails for support: {ex.Message}",
                    title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                OpenStorePage(owner);
            return;
        }

        owner.Title = ownerTitle;
        System.Windows.Input.Mouse.OverrideCursor = null;
        if (updates.Count == 0)
        {
            MessageBox.Show(owner, $"You have the latest version of {ProgramName} ({CurrentVersion}).", title,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(owner,
                $"A new version of {ProgramName} is on the Microsoft Store (you have {CurrentVersion}).\n\n" +
                $"Download and install it now? Save your book first (File › Save); {ProgramName} also saves it a few seconds after you stop typing. " +
                $"Windows closes {ProgramName} to install it; open it again from the Start menu. " +
                "(No: the Store usually installs it later on its own.)",
                title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var result = await store.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
            string message = result.OverallState switch
            {
                StorePackageUpdateState.Completed => $"The new version is installed. If {ProgramName} is still open, close it and open it again from the Start menu to use it.",
                StorePackageUpdateState.Canceled => "The update was cancelled. You can check again at any time, or the Store will install it later.",
                _ => $"The update did not finish. The Microsoft Store will try again later, or open " +
                     $"{ProgramName}'s page on the Store and press Update.",
            };
            MessageBox.Show(owner, message, title, MessageBoxButton.OK,
                result.OverallState == StorePackageUpdateState.Completed ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"The update could not be started. Open {ProgramName}'s page on the Microsoft Store " +
                $"and press Update instead.\n\nDetails for support: {ex.Message}", title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void OpenStorePage(Window owner)
    {
        try { Process.Start(new ProcessStartInfo(StorePage) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(owner, $"Windows could not open the page.\n\n{StorePage}", ProgramName + " — Check for a new version",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>True when the program runs from its Store package (it has a package identity).</summary>
    public static bool IsFromStore()
    {
        int length = 0;
        // APPMODEL_ERROR_NO_PACKAGE (15700) means it was not installed as a package.
        return GetCurrentPackageFullName(ref length, null) != 15700;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
