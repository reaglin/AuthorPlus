using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Versioning;

namespace AuthorPlus.App;

/// <summary>
/// Versions and the GitHub backup, kept apart from the rest of the main window because they are
/// about the book folder rather than about the book.
///
/// The rule everything here follows: the author writes, and the versions look after themselves.
/// A version is offered rather than demanded, it is saved when the book is put down, and when
/// the book is connected to GitHub every version goes there without another button being pressed.
/// Anything that takes time — the first version of a long book, a push — runs off the UI thread
/// behind the status bar, never behind a modal box.
/// </summary>
public partial class MainWindow
{
    private BookRepository? _repository;                      // null when this book keeps no versions
    private readonly GitHubAccount _github = GitHubAccount.Load();
    private readonly VersionPreferences _versionPrefs = VersionPreferences.Load();
    private VersionsWindow? _versionsWindow;
    private bool _versioningBusy;

    /// <summary>The book's author writes the versions; a book with no author named falls back to this computer's user.</summary>
    private VersionAuthor VersionedBy => VersionAuthor.For(_book?.Author);

    private bool IsVersioned => _repository is not null;

    // ── Book lifecycle ────────────────────────────────────────────────────────

    /// <summary>Called when a book is opened: pick up its repository, if it has one.</summary>
    private void OpenVersioning()
    {
        _repository?.Dispose();
        _repository = _book is null ? null : BookRepository.TryOpen(_book.FolderPath);
        _versionsWindow?.Refresh();
        UpdateVersionMenus();
    }

    /// <summary>
    /// Called when the book is put down (closed, or another opened). A day's writing becomes one
    /// version without the author remembering to ask for it — and goes to GitHub if the book is
    /// connected. Quiet: a failure here is a status line, never a box in the way of closing.
    /// </summary>
    private void AutoVersionOnClose()
    {
        if (_book is null || _repository is null) return;
        if (!_versionPrefs.AutoVersion(_book.FolderPath)) return;

        try
        {
            var pending = _repository.PendingChanges(_book);
            if (pending.Count == 0) return;
            _repository.SaveVersion(ChangeNames.Suggest(pending), VersionedBy);
            if (_repository.HasRemote && _github.Credentials is { } credentials)
                _repository.Push(credentials);
        }
        catch (BookVersioningException)
        {
            // Closing a book must never fail because GitHub was unreachable.
        }
    }

    // ── Menu ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// What is live in the Versions menu. The three that matter — save a version, the history, and
    /// the GitHub backup — stay live for any open book, because each of them offers to start
    /// keeping versions if the book keeps none. A greyed-out line that does not say why is how a
    /// feature goes unnoticed; the ones that really do need something first say so in their tooltip.
    /// </summary>
    private void UpdateVersionMenus()
    {
        var hasBook = _book != null;
        MnuVersions.IsEnabled = hasBook;
        MnuSaveVersion.IsEnabled = hasBook;
        MnuHistory.IsEnabled = hasBook;
        MnuGitHub.IsEnabled = hasBook;

        MnuChapterHistory.IsEnabled = IsVersioned;
        MnuChapterHistory.ToolTip = IsVersioned
            ? "The drafts of the selected chapter: read an old one against today's, or take it back"
            : "This book is not keeping versions yet — Back Up to GitHub or Save a Version starts that.";

        var connected = IsVersioned && HasRemote;
        MnuBackUpNow.IsEnabled = connected;
        MnuGetChanges.IsEnabled = connected;
        var notConnected = IsVersioned
            ? "This book is not connected to GitHub yet — use Back Up to GitHub…"
            : "This book is not keeping versions yet — Back Up to GitHub… sets both up.";
        MnuBackUpNow.ToolTip = connected ? "Send everything saved since the last backup" : notConnected;
        MnuGetChanges.ToolTip = connected ? "Bring in what was written on another computer" : notConnected;

        MnuStartVersioning.Visibility = IsVersioned ? Visibility.Collapsed : Visibility.Visible;
        MnuAutoVersion.IsEnabled = IsVersioned;
        MnuAutoVersion.IsChecked = _book != null && _versionPrefs.AutoVersion(_book.FolderPath);
        ShowVersionStatus();
    }

    /// <summary>Whether this book has somewhere on GitHub to go, asked safely.</summary>
    private bool HasRemote
    {
        get
        {
            try { return _repository?.HasRemote == true; }
            catch (BookVersioningException) { return false; }
        }
    }

    /// <summary>The status-bar line: how many versions, and whether anything is not in one yet.</summary>
    private void ShowVersionStatus()
    {
        if (_book is null) { TxtVersions.Text = ""; return; }
        if (_versioningBusy) return;        // the repository is being written to on another thread
        if (_repository is null)
        {
            TxtVersions.Text = "Versions: off";
            TxtVersions.ToolTip = "This book keeps no version history. Versions › Start Keeping Versions…";
            return;
        }

        try
        {
            var count = _repository.History().Count;
            var pending = _repository.PendingChanges(_book).Count;
            TxtVersions.Text = pending == 0
                ? $"{count} version{(count == 1 ? "" : "s")}"
                : $"{count} version{(count == 1 ? "" : "s")} · {pending} not saved";
            TxtVersions.ToolTip = (_repository.RemoteUrl is { } url ? "Backed up to " + url + "\n" : "") + "Click to see the versions of this book";
        }
        catch (BookVersioningException ex)
        {
            TxtVersions.Text = "Versions: unavailable";
            TxtVersions.ToolTip = ex.Message;
        }
    }

    private void StatusVersions_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        Versions_Click(sender, new RoutedEventArgs());

    private async void StartVersioning_Click(object sender, RoutedEventArgs e) => await EnsureVersioned(ask: true);

    private async void SaveVersion_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureVersioned(ask: true)) return;
        await SaveVersionInteractive();
    }

    private void Versions_Click(object sender, RoutedEventArgs e)
    {
        if (_book is null || _repository is null)
        {
            _ = EnsureVersioned(ask: true);
            return;
        }
        if (_versionsWindow is { IsLoaded: true })
        {
            _versionsWindow.Activate();
            _versionsWindow.Refresh();
            return;
        }
        _versionsWindow = new VersionsWindow(
            () => _repository,
            () => _book,
            SaveVersionInteractive,
            RestoreVersion,
            () => GitHub_Click(this, new RoutedEventArgs()))
        { Owner = this };
        _versionsWindow.Closed += (_, _) => _versionsWindow = null;
        _versionsWindow.Show();
    }

    private void ChapterHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_book is null || _repository is null) { _ = EnsureVersioned(ask: true); return; }
        if (_current is not Chapter chapter)
        {
            MessageBox.Show(this, "Choose a chapter in the tree first — this shows the versions of one chapter.",
                "Chapter History", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        FlushToDisk();
        new ChapterHistoryWindow(_repository, _book, chapter, RestoreChapterText) { Owner = this }.Show();
    }

    private async void GitHub_Click(object sender, RoutedEventArgs e)
    {
        if (_book is null) return;
        if (!await EnsureVersioned(ask: true, forGitHub: true)) return;

        var window = new GitHubWindow(_github, _repository!, _book, VersionedBy,
            saveVersionFirst: async () => await SaveVersionSilently(),
            reloadBook: ReloadFromDisk) { Owner = this };
        window.ShowDialog();
        if (window.Changed) _versionPrefs.SetBackedUp(_book.FolderPath);
        _versionsWindow?.Refresh();
        UpdateVersionMenus();
    }

    private async void BackUpNow_Click(object sender, RoutedEventArgs e)
    {
        if (_book is null || _repository is null || !HasRemote || !_github.IsSignedIn) { GitHub_Click(sender, e); return; }

        await SaveVersionSilently();
        await RunVersioning("Backing up to GitHub…", () => _repository.Push(_github.Credentials!),
            outcome =>
            {
                _versionPrefs.SetBackedUp(_book!.FolderPath);
                UpdateStatus(outcome.Message + $" ({DateTime.Now:HH:mm})");
            });
    }

    private async void GetChanges_Click(object sender, RoutedEventArgs e)
    {
        if (_book is null || _repository is null || !HasRemote || !_github.IsSignedIn) { GitHub_Click(sender, e); return; }

        await SaveVersionSilently();
        await RunVersioning("Looking for changes on GitHub…", () => _repository.Pull(_github.Credentials!, VersionedBy),
            outcome =>
            {
                UpdateStatus(outcome.Message);
                if (outcome.Changed) ReloadFromDisk();
                if (outcome.Conflict) GitHub_Click(this, new RoutedEventArgs());   // the window asks which copy to keep
            });
    }

    private void AutoVersion_Click(object sender, RoutedEventArgs e)
    {
        if (_book is null) return;
        _versionPrefs.SetAutoVersion(_book.FolderPath, MnuAutoVersion.IsChecked);
    }

    // ── The work ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Makes sure the book has a repository, offering to start one the first time. The offer says
    /// what it will do in the book's own words; it does not mention git, because nothing the
    /// author does afterwards requires knowing what git is.
    /// </summary>
    private async Task<bool> EnsureVersioned(bool ask, bool forGitHub = false)
    {
        if (_book is null) return false;
        if (_repository is not null) return true;

        if (ask)
        {
            // Asked from the GitHub menu item, the question is about GitHub: keeping versions is
            // the step on the way, not a separate thing the author has to know to turn on first.
            var (title, question) = forGitHub
                ? ("Back Up to GitHub",
                   $"Back “{_book.Title}” up to GitHub?\n\n" +
                   "What GitHub holds is the book's versions, so AuthorPlus will start keeping versions of this book " +
                   "first — it remembers the book as it stands at any moment you choose, nothing is ever deleted, and " +
                   "the book folder stays exactly as it is.\n\n" +
                   "Then you sign in to GitHub with a token — the next window shows you how to make one — and name " +
                   "the repository, which is private unless you say otherwise.")
                : ("Keep Versions of This Book",
                   $"Start keeping versions of “{_book.Title}”?\n\n" +
                   "AuthorPlus will remember the book as it stands at any moment you choose, so you can read an old " +
                   "draft of a chapter, compare it with today's, or put the whole book back as it was. " +
                   "Nothing is ever deleted, and the book folder stays exactly as it is.\n\n" +
                   "You can also keep a copy on GitHub afterwards, so that losing this computer costs you nothing.");

            var answer = MessageBox.Show(this, question, title, MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return false;
        }

        FlushToDisk();
        var folder = _book.FolderPath;
        var author = VersionedBy;
        try
        {
            UpdateStatus("Reading the book into its first version…");
            Cursor = System.Windows.Input.Cursors.Wait;
            _repository = await Task.Run(() => BookRepository.Initialize(folder, author));
            _versionPrefs.SetAutoVersion(folder, true);
            UpdateStatus("Versions are being kept for this book.");
        }
        catch (BookVersioningException ex)
        {
            MessageBox.Show(this, ex.Message, "Versions", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            Cursor = null;
            UpdateVersionMenus();
        }
        return true;
    }

    /// <summary>Save a version, with the dialog that shows what changed and offers a name for it.</summary>
    private async Task SaveVersionInteractive()
    {
        if (_book is null || _repository is null) return;
        FlushToDisk();

        var pending = _repository.PendingChanges(_book);
        if (pending.Count == 0)
        {
            UpdateStatus("Nothing has changed since the last version.");
            return;
        }

        var dialog = new SaveVersionWindow(pending, _repository.HasRemote && _github.IsSignedIn) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var message = dialog.Message;
        await RunVersioning("Saving a version…", () => _repository.SaveVersion(message, VersionedBy),
            version => UpdateStatus(version is null ? "Nothing has changed since the last version." : $"Version saved: {version.Message}"));

        if (_repository.HasRemote && _github.Credentials is { } credentials)
            await RunVersioning("Backing up to GitHub…", () => _repository.Push(credentials),
                outcome => { _versionPrefs.SetBackedUp(_book.FolderPath); UpdateStatus(outcome.Message); });
    }

    /// <summary>Saves a version without asking, naming it from what changed — used before a backup.</summary>
    private async Task SaveVersionSilently()
    {
        if (_book is null || _repository is null) return;
        FlushToDisk();
        var pending = _repository.PendingChanges(_book);
        if (pending.Count == 0) return;
        var message = ChangeNames.Suggest(pending);
        await RunVersioning("Saving a version…", () => _repository.SaveVersion(message, VersionedBy),
            version => { if (version is not null) UpdateStatus($"Version saved: {version.Message}"); });
    }

    /// <summary>Puts the whole book back to a version, after saying plainly what that means.</summary>
    private async Task RestoreVersion(BookVersion version)
    {
        if (_book is null || _repository is null) return;

        var answer = MessageBox.Show(this,
            $"Put the whole book back as it was on {version.WhenText}?\n\n“{version.Message}”\n\n" +
            "Everything written since then is kept — it stays in the history as its own version, and you can " +
            "come forward again the same way.",
            "Put the Book Back", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        FlushToDisk();
        var sha = version.Sha;
        await RunVersioning("Putting the book back…", () => _repository.Restore(sha, VersionedBy),
            _ =>
            {
                ReloadFromDisk();
                UpdateStatus($"The book is as it was on {version.WhenText}.");
            });
    }

    /// <summary>Puts one chapter's text back to an older version, leaving the rest of the book alone.</summary>
    private void RestoreChapterText(Chapter chapter, BookVersion version, string text)
    {
        if (_book is null) return;
        var answer = MessageBox.Show(this,
            $"Put the text of “{chapter.Title}” back as it was on {version.WhenText}?\n\n" +
            "Only this chapter changes. The version you are reading now stays in the history.",
            "Put the Chapter Back", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        _store.SaveChapterBody(_book, chapter, text, WordCounter.Count(TextDiff.Plain(text)));
        _store.Save(_book);
        ReloadFromDisk();
        UpdateStatus($"“{chapter.Title}” is as it was on {version.WhenText}.");
    }

    // ── Plumbing ──────────────────────────────────────────────────────────────

    /// <summary>Everything in the editor onto the disk, because git versions files and not windows.</summary>
    private void FlushToDisk()
    {
        CommitCurrent();
        if (_dirty) SaveAll(silent: true);
    }

    /// <summary>Reads the book folder again — after a restore or a fetch, the files on disk are the truth.</summary>
    private void ReloadFromDisk()
    {
        if (_book is null) return;
        var folder = _book.FolderPath;
        _dirty = false;
        TryOpenFolder(folder);
    }

    /// <summary>
    /// One git job on a background thread, with the status bar saying what is happening and a wait
    /// cursor. Two at once are refused rather than queued — the second would be working on a folder
    /// the first is still rewriting.
    /// </summary>
    private async Task RunVersioning<T>(string what, Func<T> work, Action<T> done)
    {
        if (_versioningBusy) { UpdateStatus("One moment — the last version job is still running."); return; }
        _versioningBusy = true;
        UpdateStatus(what);
        Cursor = System.Windows.Input.Cursors.AppStarting;
        try
        {
            var result = await Task.Run(work);
            done(result);
        }
        catch (BookVersioningException ex)
        {
            UpdateStatus(ex.Message);
            MessageBox.Show(this, ex.Message, "Versions", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _versioningBusy = false;
            Cursor = null;
            _versionsWindow?.Refresh();
            UpdateVersionMenus();
        }
    }
}

/// <summary>
/// What this computer remembers about versioning each book: whether to save a version when the
/// book is put down, and when it last went to GitHub. Per computer rather than per book, in
/// <c>%AppData%\AuthorPlus\versioning.json</c>, because it is about this machine's habits and has
/// no business inside the manuscript (where it would be versioned and pushed along with it).
/// </summary>
internal sealed class VersionPreferences
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuthorPlus", "versioning.json");

    public Dictionary<string, BookPreference> Books { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static VersionPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<VersionPreferences>(File.ReadAllText(FilePath)) ?? new VersionPreferences();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; their loss costs a tick box.
        }
        return new VersionPreferences();
    }

    public bool AutoVersion(string folder) => !Books.TryGetValue(folder, out var p) || p.AutoVersionOnClose;

    public void SetAutoVersion(string folder, bool on)
    {
        Of(folder).AutoVersionOnClose = on;
        Save();
    }

    public void SetBackedUp(string folder)
    {
        Of(folder).LastBackupUtc = DateTime.UtcNow;
        Save();
    }

    private BookPreference Of(string folder)
    {
        if (!Books.TryGetValue(folder, out var preference)) Books[folder] = preference = new BookPreference();
        return preference;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth telling the author about.
        }
    }

    internal sealed class BookPreference
    {
        public bool      AutoVersionOnClose { get; set; } = true;
        public DateTime? LastBackupUtc      { get; set; }
    }
}
