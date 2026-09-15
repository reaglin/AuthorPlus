using System.IO;
using System.Windows;
using System.Windows.Controls;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services.Versioning;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace AuthorPlus.App;

/// <summary>
/// One chapter's own history: every version in which its text changed, with the two things worth
/// doing to an old draft — read it against today's, or take it back.
///
/// This is the view a novelist actually wants from version history. "The book as it was in
/// August" is rarely the question; "what did this chapter say before I rewrote the ending" always is.
/// </summary>
public sealed class ChapterHistoryWindow : Window
{
    private sealed record Row(BookVersion Version, string When, string Message, string By);

    private readonly BookRepository _repository;
    private readonly Chapter _chapter;
    private readonly string _path;
    private readonly DataGrid _versions;

    public ChapterHistoryWindow(BookRepository repository, Book book, Chapter chapter,
                                Action<Chapter, BookVersion, string> restore)
    {
        _repository = repository;
        _chapter = chapter;
        _path = ChangeNames.ChapterTextPath(chapter);

        var label = ChangeNames.ChapterLabel(book, chapter);
        Title = "History — " + chapter.Title;
        Width = 720; Height = 500; MinWidth = 520; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(16) };

        var heading = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var versions = _repository.HistoryOf(_path);
        var note = new TextBlock
        {
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            Text = versions.Count == 0
                ? "This chapter's text has not changed in any saved version yet."
                : $"The text of this chapter changed in {versions.Count} version{(versions.Count == 1 ? "" : "s")}."
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var compare = new Button { Content = "Read it against the chapter as it is now…", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.SemiBold };
        var back = new Button { Content = "Put the chapter back to this version…", Padding = new Thickness(12, 4, 12, 4) };
        buttons.Children.Add(compare);
        buttons.Children.Add(back);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _versions = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            AlternatingRowBackground = Palette.RowAltBg, HorizontalGridLinesBrush = Palette.Line,
            BorderBrush = Palette.Line, BorderThickness = new Thickness(1),
            ItemsSource = versions.Select(v => new Row(v, v.WhenText, v.Message, v.AuthorName)).ToList()
        };
        _versions.Columns.Add(Column("When", nameof(Row.When), 130));
        _versions.Columns.Add(Column("Version", nameof(Row.Message), 0));
        _versions.Columns.Add(Column("Saved by", nameof(Row.By), 110));
        _versions.MouseDoubleClick += (_, _) => Compare();
        _versions.SelectedIndex = versions.Count > 1 ? 1 : 0;      // the one before today's, usually what is wanted
        root.Children.Add(_versions);

        compare.Click += (_, _) => Compare();
        back.Click += (_, _) =>
        {
            if (Selected is not { } version) return;
            if (_repository.ReadTextAt(version.Sha, _path) is not { } text)
            {
                MessageBox.Show(this, "That version has no text for this chapter.", "Chapter History", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            restore(_chapter, version, text);
            Close();
        };

        Content = root;
    }

    private BookVersion? Selected => (_versions.SelectedItem as Row)?.Version;

    private void Compare()
    {
        if (Selected is not { } version) return;
        var then = _repository.ReadTextAt(version.Sha, _path);
        var full = Path.Combine(_repository.Folder, _path);
        var now = File.Exists(full) ? File.ReadAllText(full) : null;

        new CompareWindow(_chapter.Title, $"as it was on {version.WhenText}", "as it is now", then, now) { Owner = this }.Show();
    }

    private static DataGridTextColumn Column(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new System.Windows.Data.Binding(property),
        Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star)
    };
}
