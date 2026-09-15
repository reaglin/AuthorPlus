using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services.Versioning;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace AuthorPlus.App;

/// <summary>
/// Everything the author does with versions, in one non-modal window beside the book: the
/// versions saved so far, what each one changed, and the two things worth doing to one — put the
/// book back as it was, or read a chapter as it was against the chapter as it is.
///
/// The window owns no state of its own. It asks the main window for the repository and the book
/// each time it draws, and asks it to do anything that changes them, so a restore reloads the
/// tree and a backup updates the status bar without this window knowing how either works.
/// </summary>
public sealed class VersionsWindow : Window
{
    private sealed record VersionRow(BookVersion Version, string When, string Message, string By);
    private sealed record ChangeRow(BookChange Change, string What, string Description);

    private readonly Func<BookRepository?> _repository;
    private readonly Func<Book?> _book;
    private readonly Func<Task> _saveVersion;
    private readonly Func<BookVersion, Task> _restore;
    private readonly Action _openGitHub;

    private readonly DataGrid _versions = MakeGrid();
    private readonly DataGrid _changes = MakeGrid();
    private readonly TextBlock _summary = new() { Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _pending = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _restoreButton = new() { Content = "Put the book back to this version…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 8, 8, 0), IsEnabled = false };
    private readonly Button _compareButton = new() { Content = "Compare this chapter with now…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 8, 0, 0), IsEnabled = false };

    public VersionsWindow(Func<BookRepository?> repository, Func<Book?> book,
                          Func<Task> saveVersion, Func<BookVersion, Task> restore, Action openGitHub)
    {
        _repository = repository;
        _book = book;
        _saveVersion = saveVersion;
        _restore = restore;
        _openGitHub = openGitHub;

        Title = "Versions";
        Width = 1000; Height = 620; MinWidth = 720; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(16) };

        // ── What to do ────────────────────────────────────────────────────────
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var save = new Button { Content = "Save a version…", Padding = new Thickness(12, 4, 12, 4), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        save.Click += async (_, _) => { await _saveVersion(); Refresh(); };
        var github = new Button { Content = "Back up to GitHub…", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 12, 0) };
        github.Click += (_, _) => { _openGitHub(); Refresh(); };
        bar.Children.Add(save);
        bar.Children.Add(github);
        bar.Children.Add(_summary);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        var help = new TextBlock
        {
            Text = "A version is the whole book as it stood at one moment. Saving one costs nothing but the seconds it takes, " +
                   "and nothing is ever removed from this list — putting the book back to an older version is itself a new version.",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(help, Dock.Top);
        root.Children.Add(help);

        // ── Anything not yet in a version ─────────────────────────────────────
        DockPanel.SetDock(_pending, Dock.Bottom);
        root.Children.Add(_pending);

        // ── The versions, and what each one changed ───────────────────────────
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });

        _versions.Columns.Add(Column("When", nameof(VersionRow.When), 120));
        _versions.Columns.Add(Column("Version", nameof(VersionRow.Message), 0));
        _versions.Columns.Add(Column("Saved by", nameof(VersionRow.By), 95));
        _versions.SelectionChanged += (_, _) => ShowChanges();
        split.Children.Add(_versions);

        var right = new DockPanel { Margin = new Thickness(0) };
        Grid.SetColumn(right, 2);
        var changesLabel = new TextBlock { Text = "What this version changed", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(changesLabel, Dock.Top);
        right.Children.Add(changesLabel);

        var buttons = new WrapPanel();
        _restoreButton.Click += async (_, _) => { if (Selected is { } v) { await _restore(v); Refresh(); } };
        _compareButton.Click += (_, _) => Compare();
        buttons.Children.Add(_restoreButton);
        buttons.Children.Add(_compareButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        right.Children.Add(buttons);

        _changes.Columns.Add(Column("", nameof(ChangeRow.What), 80));
        _changes.Columns.Add(Column("Part of the book", nameof(ChangeRow.Description), 0));
        _changes.SelectionChanged += (_, _) => UpdateButtons();
        _changes.MouseDoubleClick += (_, _) => Compare();
        right.Children.Add(_changes);

        split.Children.Add(right);
        root.Children.Add(split);

        Content = root;
        Refresh();
    }

    private BookVersion? Selected => (_versions.SelectedItem as VersionRow)?.Version;

    /// <summary>Redraws from the repository — after a version is saved, a restore, or a backup.</summary>
    public void Refresh()
    {
        var repository = _repository();
        if (repository is null)
        {
            _summary.Text = "This book is not keeping versions yet.";
            _versions.ItemsSource = null;
            _changes.ItemsSource = null;
            _pending.Text = string.Empty;
            return;
        }

        var book = _book();
        var chosen = Selected?.Sha;
        var history = repository.History();
        _versions.ItemsSource = history.Select(v => new VersionRow(v, v.WhenText, v.Message, v.AuthorName)).ToList();
        _versions.SelectedIndex = Math.Max(0, history.ToList().FindIndex(v => v.Sha == chosen));

        _summary.Text = history.Count == 1 ? "1 version saved." : $"{history.Count} versions saved.";
        if (repository.RemoteUrl is { } url) _summary.Text += "  Backed up to " + Shorten(url);

        var pending = repository.PendingChanges(book);
        _pending.Text = pending.Count == 0
            ? "Everything written so far is in a version."
            : $"Not yet in a version: {ChangeNames.Suggest(pending)} ({pending.Count} file{(pending.Count == 1 ? "" : "s")}).";
        _pending.Foreground = pending.Count == 0 ? Brushes.Gray : Brushes.Black;
        ShowChanges();
    }

    private void ShowChanges()
    {
        var repository = _repository();
        if (repository is null || Selected is not { } version) { _changes.ItemsSource = null; UpdateButtons(); return; }

        Cursor = Cursors.Wait;
        try
        {
            _changes.ItemsSource = repository.ChangesIn(version.Sha, _book())
                .Select(c => new ChangeRow(c, c.KindWord, c.Description)).ToList();
        }
        catch (BookVersioningException ex)
        {
            _changes.ItemsSource = null;
            _summary.Text = ex.Message;
        }
        finally { Cursor = null; }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var isFirstShown = _versions.SelectedIndex == 0;
        _restoreButton.IsEnabled = Selected is not null && !isFirstShown;
        _restoreButton.ToolTip = isFirstShown ? "This is the book as it is now." : null;
        _compareButton.IsEnabled = (_changes.SelectedItem as ChangeRow)?.Change.IsChapterText == true;
    }

    /// <summary>Reads the chapter as it was at this version against the chapter as it is now.</summary>
    private void Compare()
    {
        if (_repository() is not { } repository || Selected is not { } version) return;
        if (_changes.SelectedItem is not ChangeRow row || !row.Change.IsChapterText) return;

        var path = row.Change.Path;
        var then = repository.ReadTextAt(version.Sha, path);
        var now = File.Exists(Path.Combine(repository.Folder, path))
            ? File.ReadAllText(Path.Combine(repository.Folder, path))
            : null;

        new CompareWindow(row.Description.Replace(" (the text)", "", StringComparison.Ordinal),
                          $"as it was on {version.WhenText}", "as it is now", then, now) { Owner = this }.Show();
    }

    private static string Shorten(string url) =>
        url.Replace("https://", "", StringComparison.OrdinalIgnoreCase).Replace(".git", "", StringComparison.OrdinalIgnoreCase);

    private static DataGrid MakeGrid() => new()
    {
        AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        SelectionMode = DataGridSelectionMode.Single, AlternatingRowBackground = Palette.RowAltBg,
        HorizontalGridLinesBrush = Palette.Line, BorderBrush = Palette.Line, BorderThickness = new Thickness(1)
    };

    private static DataGridTextColumn Column(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new System.Windows.Data.Binding(property),
        Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star)
    };
}
