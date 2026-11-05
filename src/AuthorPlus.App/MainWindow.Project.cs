using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AuthorPlus.Core.Services.Transfer;
using Microsoft.Win32;

namespace AuthorPlus.App;

/// <summary>
/// File › Export Project and File › Import Project: the whole book — with its saved versions — as one
/// <c>.authorplus</c> file, to carry to another computer and back. The work is in <see cref="ProjectPackage"/>;
/// this is the asking and the telling. While either runs, the window is locked so nothing typed can be lost.
/// </summary>
public partial class MainWindow
{
    private const string ExportTitle = "Author+ — Export Project", ImportTitle = "Author+ — Import Project";
    private readonly TransferLog _transfers = new();

    private static string ProjectFilter => $"Author+ project (*{ProjectPackage.Extension})|*{ProjectPackage.Extension}|All files (*.*)|*.*";

    private async void ExportProject_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null)
        {
            MessageBox.Show(this, "Open the book you want to take to another computer first (File › Open Book or Open Recent), then choose Export Project again.",
                ExportTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SaveAll();
        if (_dirty) return;                       // the save failed and said so; do not export an old copy

        var dlg = new SaveFileDialog
        {
            Title = "Export the whole book as one project file",
            Filter = ProjectFilter, AddExtension = true, DefaultExt = ProjectPackage.Extension,
            FileName = ProjectPackage.SuggestedFileName(_book.Title, DateTime.Now, Environment.MachineName),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dlg.ShowDialog(this) != true) return;

        var folder = _book.FolderPath;
        ProjectExportResult result;
        try
        {
            using (Busy($"Exporting “{_book.Title}” to {Path.GetFileName(dlg.FileName)}… please wait"))
                result = await Task.Run(() => ProjectPackage.Export(folder, dlg.FileName, AboutWindow.Version));
        }
        catch (Exception ex)
        {
            UpdateStatus("Export Project failed — your book is unchanged");
            MessageBox.Show(this, "The project file could not be written. Your book is unchanged. Try another folder, or check there is room on the drive." +
                $"\n\nDetails for support: {ex.Message}", ExportTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _transfers.Record(folder, ProjectPackage.ReadManifest(result.File).ContentHash);   // where the two copies part
        UpdateStatus($"Exported to {result.File}");
        var history = _repository != null ? " and every saved version" : "";
        var r = MessageBox.Show(this,
            $"“{_book.Title}”{history} is in one file:\n{result.File}\n({result.FileCount:N0} files, {result.Bytes / 1048576.0:N1} MB)\n\n" +
            "To work on the other computer: copy this file there (a USB stick, OneDrive or email), open Author+ on it and choose " +
            "File › Import Project. When you come back, export it there and import it here the same way.\n\n" +
            "Write on one computer at a time. If you write on both before bringing the file back, Author+ notices and keeps both copies, so nothing is lost.\n\n" +
            "Show the file in its folder?",
            ExportTitle, MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (r == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{result.File}\"") { UseShellExecute = true });
    }

    private async void ImportProject_Click(object sender, RoutedEventArgs e)
    {
        // Put the open book down first (saving if asked), so what is compared below is what is on disk.
        if (!ConfirmDiscardOrSave()) return;
        _dirty = false;

        var pick = new OpenFileDialog
        {
            Title = "Choose the project file made with Export Project",
            Filter = ProjectFilter,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (pick.ShowDialog(this) != true) return;

        ProjectManifest manifest;
        IReadOnlyList<ExistingBook> copies;
        try
        {
            manifest = ProjectPackage.ReadManifest(pick.FileName);
            copies = ProjectPackage.FindAll(manifest, _store.Root);
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show(this, ex.Message, ImportTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var replacedFolder = Path.Combine(Path.GetDirectoryName(_store.Root.TrimEnd(Path.DirectorySeparatorChar)) ?? _store.Root, ProjectPackage.ReplacedFolderName);
        var ask = new ImportProjectWindow(manifest, copies, c => ProjectPackage.Compare(manifest, c, _transfers.LastHash(c.Folder)),
                                          Path.GetFileName(pick.FileName), replacedFolder, _book?.FolderPath) { Owner = this };
        if (ask.ShowDialog() != true) return;
        var mode = ask.Mode;
        var target = ask.Target;

        bool replacingOpenBook = mode == ProjectImportMode.Replace && _book != null && target != null &&
            string.Equals(Path.GetFullPath(_book.FolderPath), Path.GetFullPath(target.Folder), StringComparison.OrdinalIgnoreCase);
        if (replacingOpenBook) { _repository?.Dispose(); _repository = null; }   // let go of the folder so it can move

        ProjectImportResult result;
        try
        {
            var root = _store.Root;
            using (Busy($"Importing “{manifest.Title}”… please wait"))
                result = await Task.Run(() => ProjectPackage.Import(pick.FileName, root, mode, target));
        }
        catch (Exception ex)
        {
            if (replacingOpenBook) OpenVersioning();
            UpdateStatus("Import Project failed — nothing was changed");
            MessageBox.Show(this, $"The book could not be imported. Nothing on this computer was changed.\n\n" +
                (ex is InvalidDataException ? ex.Message : $"Details for support: {ex.Message}"),
                ImportTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _transfers.Record(result.Folder, manifest.ContentHash);
        try { OpenBook(_store.Load(result.Folder)); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The book was imported to\n{result.Folder}\nbut could not be opened. Try File › Open Book.\n\nDetails for support: {ex.Message}",
                ImportTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        UpdateStatus($"Imported “{manifest.Title}” into the folder “{Path.GetFileName(result.Folder)}”");

        var text = $"“{manifest.Title}” is imported and open.\n\nIt is kept in the folder:\n{result.Folder}";
        if (result.PreviousCopyFolder != null)
            text += $"\n\nThe copy that was on this computer was not deleted. It is in:\n{result.PreviousCopyFolder}\n(Open it with File › Open Book if you need anything from it.)";
        if (manifest.IncludesHistory)
            text += "\n\nIts saved versions came with it — Versions › Version History shows them.";
        MessageBox.Show(this, text, ImportTitle, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Locks the window (menus, tree, editor, autosave) with a wait pointer and a status line until disposed.</summary>
    private IDisposable Busy(string status)
    {
        UpdateStatus(status);
        IsEnabled = false;
        _autosave.Stop();
        Mouse.OverrideCursor = Cursors.Wait;
        return new Unlock(this);
    }

    private sealed class Unlock(MainWindow w) : IDisposable
    {
        public void Dispose()
        {
            Mouse.OverrideCursor = null;
            w.IsEnabled = true;
            w._autosave.Start();
        }
    }
}
