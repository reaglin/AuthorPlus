﻿﻿using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using Section = AuthorPlus.Core.Models.Section;

namespace AuthorPlus.App;

/// <summary>
/// A checklist of candidates (characters, chapters, plotlines…) bound to an id list on the
/// model, optionally with one "single" choice (the point-of-view character) as radio buttons.
/// Edits write straight into the list and call <c>changed</c>.
/// </summary>
public sealed class LinkPicker : Expander
{
    private readonly List<Guid> _selected;
    private readonly IReadOnlyList<(Guid Id, string Label)> _candidates;
    private readonly string _title;

    public LinkPicker(string title, IEnumerable<(Guid Id, string Label)> candidates, List<Guid> selected, Action changed,
                      Guid? single = null, Action<Guid?>? singleChanged = null, string singleLabel = "POV", bool expanded = false)
    {
        _title = title;
        _selected = selected;
        _candidates = candidates.ToList();
        IsExpanded = expanded;
        Margin = new Thickness(0, 6, 0, 0);
        UpdateHeader();

        var panel = new WrapPanel { Margin = new Thickness(12, 4, 0, 4) };
        if (_candidates.Count == 0)
            panel.Children.Add(new TextBlock { Text = "(nothing to choose from yet)", Foreground = Brushes.Gray });
        foreach (var (id, label) in _candidates)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 16, 2) };
            var check = new CheckBox { Content = label, IsChecked = _selected.Contains(id), VerticalAlignment = VerticalAlignment.Center };
            check.Checked   += (_, _) => { if (!_selected.Contains(id)) _selected.Add(id); UpdateHeader(); changed(); };
            check.Unchecked += (_, _) => { _selected.Remove(id); UpdateHeader(); changed(); };
            row.Children.Add(check);
            if (singleChanged != null)
            {
                var radio = new RadioButton { Content = singleLabel, GroupName = "single-" + GetHashCode(), IsChecked = single == id, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Foreground = Brushes.Gray };
                radio.Checked += (_, _) => { if (!_selected.Contains(id)) { _selected.Add(id); check.IsChecked = true; } singleChanged(id); UpdateHeader(); changed(); };
                check.Unchecked += (_, _) => { if (radio.IsChecked == true) { radio.IsChecked = false; singleChanged(null); } };
                row.Children.Add(radio);
            }
            panel.Children.Add(row);
        }
        Content = panel;
    }

    private void UpdateHeader() => Header = $"{_title} ({_selected.Count(id => _candidates.Any(c => c.Id == id))} of {_candidates.Count})";
}

/// <summary>Shown when the "Timeline" group is selected: events in story order, and the same events in the order the chapters tell them.</summary>
public sealed class TimelineView : DockPanel
{
    private sealed record Row(TimelineEvent Event, int Order, string When, string Title, string Chapters, string Characters, string TellingOrder);

    public TimelineView(Book book, Action<object> navigate, Action changed)
    {
        Margin = new Thickness(0);
        var help = new TextBlock
        {
            Text = "Story order is the order of the events themselves; the right-hand list shows the order the chapters tell them in. A ⚠ marks an event told out of story order (a flashback, or a slip).",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        SetDock(help, Dock.Top);
        Children.Add(help);

        var chapterIndex = book.Chapters.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i);
        string ChapterLabel(Guid id) => book.Chapters.FirstOrDefault(c => c.Id == id) is { } c ? $"{(book.SectionOf(c) is { } s ? book.Sections.IndexOf(s) + 1 + "." : "")}{book.ChapterNumber(c)} {c.Title}" : "?";
        string Names(IEnumerable<Guid> ids) => string.Join(", ", ids.Select(id => book.Characters.FirstOrDefault(c => c.Id == id)?.Name).Where(n => n != null));
        int FirstChapter(TimelineEvent e) => e.ChapterIds.Where(chapterIndex.ContainsKey).Select(id => chapterIndex[id]).DefaultIfEmpty(int.MaxValue).Min();

        var ordered = book.Timeline.OrderBy(e => e.Order).ToList();
        var told = ordered.Where(e => FirstChapter(e) != int.MaxValue).OrderBy(FirstChapter).ToList();
        var toldRank = told.Select((e, i) => (e.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var storyRank = ordered.Select((e, i) => (e.Id, i)).ToDictionary(x => x.Id, x => x.i);

        Row Make(TimelineEvent e)
        {
            var outOfOrder = toldRank.TryGetValue(e.Id, out var tr) && told.Take(tr).Any(prev => storyRank[prev.Id] > storyRank[e.Id]);
            return new Row(e, e.Order, e.When, e.Title, string.Join(", ", e.ChapterIds.Select(ChapterLabel)), Names(e.CharacterIds),
                           toldRank.TryGetValue(e.Id, out var r2) ? (outOfOrder ? "⚠ " : "") + (r2 + 1).ToString() : "—");
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var story = MakeGrid();
        story.Columns.Add(Col("#", nameof(Row.Order), 36));
        story.Columns.Add(Col("When", nameof(Row.When), 110));
        story.Columns.Add(Col("Event", nameof(Row.Title), 0));
        story.Columns.Add(Col("Chapters", nameof(Row.Chapters), 0));
        story.Columns.Add(Col("Characters", nameof(Row.Characters), 0));
        story.Columns.Add(Col("Told", nameof(Row.TellingOrder), 50));
        story.ItemsSource = ordered.Select(Make).ToList();
        story.MouseDoubleClick += (_, _) => { if (story.SelectedItem is Row r) navigate(r.Event); };

        var left = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var up = new Button { Content = "Move up", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
        var down = new Button { Content = "Move down", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
        var open = new Button { Content = "Open", Padding = new Thickness(8, 2, 8, 2) };
        void Move(int delta)
        {
            if (story.SelectedItem is not Row r) return;
            int i = book.Timeline.IndexOf(r.Event), j = i + delta;
            if (i < 0 || j < 0 || j >= book.Timeline.Count) return;
            (book.Timeline[i], book.Timeline[j]) = (book.Timeline[j], book.Timeline[i]);
            for (int k = 0; k < book.Timeline.Count; k++) book.Timeline[k].Order = k + 1;
            changed();
            var keep = r.Event;
            ordered = book.Timeline.OrderBy(e => e.Order).ToList();
            storyRank = ordered.Select((e, idx) => (e.Id, idx)).ToDictionary(x => x.Id, x => x.idx);
            story.ItemsSource = ordered.Select(Make).ToList();
            story.SelectedItem = story.Items.OfType<Row>().FirstOrDefault(x => x.Event == keep);
        }
        up.Click += (_, _) => Move(-1);
        down.Click += (_, _) => Move(+1);
        open.Click += (_, _) => { if (story.SelectedItem is Row r) navigate(r.Event); };
        buttons.Children.Add(up); buttons.Children.Add(down); buttons.Children.Add(open);
        SetDock(buttons, Dock.Bottom);
        left.Children.Add(buttons);
        left.Children.Add(new TextBlock { Text = "Story order", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        SetDock(left.Children[1], Dock.Top);
        left.Children.Add(story);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = new DockPanel();
        right.Children.Add(new TextBlock { Text = "As the chapters tell it", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        SetDock(right.Children[0], Dock.Top);
        var telling = MakeGrid();
        telling.Columns.Add(Col("Told", nameof(Row.TellingOrder), 50));
        telling.Columns.Add(Col("Event", nameof(Row.Title), 0));
        telling.Columns.Add(Col("Story #", nameof(Row.Order), 60));
        telling.Columns.Add(Col("Chapter", nameof(Row.Chapters), 0));
        telling.ItemsSource = told.Select(Make).ToList();
        telling.MouseDoubleClick += (_, _) => { if (telling.SelectedItem is Row r) navigate(r.Event); };
        right.Children.Add(telling);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        Children.Add(grid);
    }

    internal static DataGrid MakeGrid() => new()
    {
        AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
        RowHeaderWidth = 0, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, SelectionMode = DataGridSelectionMode.Single, Background = Brushes.White
    };

    internal static DataGridTextColumn Col(string header, string path, double width) => new()
    {
        Header = header, Binding = new System.Windows.Data.Binding(path),
        Width = width > 0 ? new DataGridLength(width) : new DataGridLength(1, DataGridLengthUnitType.Star)
    };
}

/// <summary>Shown when the "Plotlines" group is selected: one row per plotline, one column per chapter; click a cell to toggle whether the plotline runs through that chapter.</summary>
public sealed class PlotlineBoard : DockPanel
{
    public PlotlineBoard(Book book, Action<object> navigate, Action changed)
    {
        var help = new TextBlock
        {
            Text = "What each thread does in each chapter:  ● green it is introduced  ·  ● blue it continues  ·  ● purple it is resolved  ·  ◆ it converges with another thread there. " +
                   "Click a cell to say which — that is where a thread's status comes from, so a thread is planned until it runs anywhere, active once it does, and resolved when a chapter resolves it. " +
                   "The tint behind a name is its kind: primary, secondary, chapter, subplot, extra. Click a name to open the thread. " +
                   "No plotlines yet? Right-click Plotlines › Find Plotlines (AI)… works from the chapter summaries.",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        SetDock(help, Dock.Top);
        Children.Add(help);

        if (book.Plotlines.Count == 0)
        {
            Children.Add(new TextBlock { Text = "No plotlines yet — right-click Plotlines in the tree to add one.", Foreground = Brushes.Gray });
            return;
        }

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in book.Chapters) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in book.Plotlines) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header: chapter numbers (tooltip = full title), section colour bands by alternating background.
        for (int c = 0; c < book.Chapters.Count; c++)
        {
            var ch = book.Chapters[c];
            var sec = book.SectionOf(ch);
            var hdr = new TextBlock
            {
                Text = book.ChapterNumber(ch).ToString(), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(0, 2, 0, 2),
                ToolTip = $"{(sec != null ? sec.Title + " — " : "")}{book.ChapterNumber(ch)}. {ch.Title}",
                Background = sec != null && book.Sections.IndexOf(sec) % 2 == 1 ? new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)) : Brushes.Transparent
            };
            Grid.SetRow(hdr, 0); Grid.SetColumn(hdr, c + 1);
            grid.Children.Add(hdr);
        }

        for (int r = 0; r < book.Plotlines.Count; r++)
        {
            var p = book.Plotlines[r];
            var rowBg = r % 2 == 1 ? Palette.RowAltBg : Brushes.Transparent;
            var name = new TextBlock
            {
                Padding = new Thickness(6, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                FontWeight = FontWeights.SemiBold, Background = Palette.KindBg(p.Kind), ToolTip = "Open this plotline"
            };
            var line = p;
            void PaintName()
            {
                name.Text = $"{line.Name}  ({line.Kind.ToString().ToLowerInvariant()}, {line.Status.ToString().ToLowerInvariant()})";
                name.Foreground = line.Status == PlotlineStatus.Resolved ? Brushes.Black : Brushes.Firebrick;
            }
            PaintName();
            name.MouseLeftButtonUp += (_, _) => navigate(p);
            Grid.SetRow(name, r + 1); Grid.SetColumn(name, 0);
            grid.Children.Add(name);

            for (int c = 0; c < book.Chapters.Count; c++)
            {
                var ch = book.Chapters[c];
                var cell = new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 0, 1, 1), Cursor = Cursors.Hand, Background = rowBg };
                var mark = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
                var thread = p; var chapter = ch;
                void Paint()
                {
                    var role = thread.RoleIn(chapter.Id);
                    bool conv = thread.Convergences.Any(x => x.ChapterId == chapter.Id);
                    mark.Text = role is null ? "" : conv ? "◆" : "●";
                    mark.Foreground = conv ? Brushes.DarkOrange : Palette.RoleInk(role ?? PlotlineRole.Continuing);
                    cell.ToolTip = $"{book.ChapterNumber(chapter)}. {chapter.Title} — " +
                                   (role is { } r3 ? Palette.RoleWord(r3).ToLowerInvariant() + " here" : "the thread does not run through this chapter") +
                                   "\nClick to change it.";
                }
                Paint();
                cell.Child = mark;

                // Clicking the dot says what the thread does here; the thread's status follows from it.
                cell.MouseLeftButtonUp += (_, _) =>
                {
                    var menu = new ContextMenu { PlacementTarget = cell, IsOpen = true };
                    var current = thread.RoleIn(chapter.Id);
                    void Choice(string header, PlotlineRole? role)
                    {
                        var mi = new MenuItem { Header = header, IsChecked = current == role, IsCheckable = false };
                        mi.Click += (_, _) => { book.SetRole(thread, chapter.Id, role); Paint(); PaintName(); changed(); };
                        menu.Items.Add(mi);
                    }
                    menu.Items.Add(new MenuItem { Header = $"Chapter {book.ChapterNumber(chapter)} — {chapter.Title}", IsEnabled = false });
                    menu.Items.Add(new Separator());
                    Choice("Introduced here", PlotlineRole.Introduced);
                    Choice("Continuing here", PlotlineRole.Continuing);
                    Choice("Resolved here", PlotlineRole.Resolved);
                    menu.Items.Add(new Separator());
                    Choice("Not in this chapter", null);
                };
                Grid.SetRow(cell, r + 1); Grid.SetColumn(cell, c + 1);
                grid.Children.Add(cell);
            }
        }

        Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
}

/// <summary>
/// Shown when the "Characters" group is selected, and the one place characters are linked to
/// chapters: the cast as a table, and under it the same chapter-by-chapter chart the Plotlines
/// page uses — one row per character, one column per chapter.
/// </summary>
public sealed class CharactersView : DockPanel
{
    private sealed record Row(Character Character, string Name, string Role, int Chapters, int Pov, string First, string Aliases);

    public CharactersView(Book book, Action<object> navigate, Action changed)
    {
        var help = new TextBlock { Text = "The cast above, who is in which chapter below. Double-click a row to open a character; everything about them is edited on their own page.", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        SetDock(help, Dock.Top);
        Children.Add(help);
        var grid = TimelineView.MakeGrid();
        grid.Columns.Add(TimelineView.Col("Name", nameof(Row.Name), 0));
        grid.Columns.Add(TimelineView.Col("Role", nameof(Row.Role), 120));
        grid.Columns.Add(TimelineView.Col("Also called", nameof(Row.Aliases), 0));
        grid.Columns.Add(TimelineView.Col("Chapters", nameof(Row.Chapters), 80));
        grid.Columns.Add(TimelineView.Col("POV", nameof(Row.Pov), 60));
        grid.Columns.Add(TimelineView.Col("First appears", nameof(Row.First), 0));
        grid.ItemsSource = book.Characters.Select(c =>
        {
            var chapters = book.Chapters.Where(ch => ch.CharacterIds.Contains(c.Id)).ToList();
            var first = chapters.FirstOrDefault();
            return new Row(c, c.Name, c.Role, chapters.Count, book.Chapters.Count(ch => ch.PovCharacterId == c.Id),
                first == null ? "—" : $"{(book.SectionOf(first) is { } s ? s.Title + ", " : "")}{book.ChapterNumber(first)}. {first.Title}",
                string.Join(", ", MentionFinder.NamesOf(c).Skip(1)));
        }).ToList();
        grid.MouseDoubleClick += (_, _) => { if (grid.SelectedItem is Row r) navigate(r.Character); };

        // The cast above, the chapter-by-chapter chart below, each scrolling on its own.
        var split = new Grid();
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 90 });
        split.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 120 });
        Grid.SetRow(grid, 0);
        split.Children.Add(grid);
        var splitter = new GridSplitter { Height = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
        Grid.SetRow(splitter, 1);
        split.Children.Add(splitter);
        var board = new CharacterBoard(book, navigate, changed);
        Grid.SetRow(board, 2);
        split.Children.Add(board);
        Children.Add(split);
    }
}

/// <summary>
/// Who is in each chapter: one row per character, one column per chapter, the same chart as the
/// Plotlines board. Click a cell to put a character in a chapter or take them out; double-click to
/// make them its point-of-view character. This is where those links are made, so the chapter
/// editor can stay the manuscript and nothing else.
/// </summary>
public sealed class CharacterBoard : DockPanel
{
    public CharacterBoard(Book book, Action<object> navigate, Action changed)
    {
        var help = new TextBlock
        {
            Text = "● the character is in the chapter · ★ the chapter is told from their point of view. Click a cell to put them in or take them out; double-click to make them its point of view. Click a name to open the character. A red name is in no chapter yet.",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8)
        };
        SetDock(help, Dock.Top);
        Children.Add(help);

        if (book.Characters.Count == 0)
        {
            Children.Add(new TextBlock { Text = "No characters yet — right-click Characters in the tree to add one, or run Analyze… (AI) on a chapter and keep the ones it finds.", Foreground = Brushes.Gray });
            return;
        }

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var _ in book.Chapters) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in book.Characters) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int c = 0; c < book.Chapters.Count; c++)
        {
            var ch = book.Chapters[c];
            var sec = book.SectionOf(ch);
            var hdr = new TextBlock
            {
                Text = book.ChapterNumber(ch).ToString(), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(0, 2, 0, 2),
                ToolTip = $"{(sec != null ? sec.Title + " — " : "")}{book.ChapterNumber(ch)}. {ch.Title}",
                Background = sec != null && book.Sections.IndexOf(sec) % 2 == 1 ? new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF7)) : Brushes.Transparent
            };
            Grid.SetRow(hdr, 0); Grid.SetColumn(hdr, c + 1);
            grid.Children.Add(hdr);
        }

        var repaint = new List<Action>();
        for (int r = 0; r < book.Characters.Count; r++)
        {
            var person = book.Characters[r];
            var rowBg = r % 2 == 1 ? Palette.RowAltBg : Brushes.Transparent;
            int appears = book.Chapters.Count(ch => ch.CharacterIds.Contains(person.Id));
            var name = new TextBlock
            {
                Text = person.Name + (person.Role.Length > 0 ? $"  ({person.Role})" : ""),
                Padding = new Thickness(6, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                FontWeight = FontWeights.SemiBold, Foreground = appears == 0 ? Brushes.Firebrick : Brushes.Black, Background = rowBg,
                ToolTip = appears == 0 ? "No chapter lists this character yet — click the cells to say where they are" : "Open this character"
            };
            name.MouseLeftButtonUp += (_, _) => navigate(person);
            Grid.SetRow(name, r + 1); Grid.SetColumn(name, 0);
            grid.Children.Add(name);

            for (int c = 0; c < book.Chapters.Count; c++)
            {
                var ch = book.Chapters[c];
                var cell = new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(0, 0, 1, 1), Cursor = Cursors.Hand, Background = rowBg };
                var mark = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
                var who = person; var chapter = ch;
                void Paint()
                {
                    bool present = chapter.CharacterIds.Contains(who.Id);
                    bool pov = chapter.PovCharacterId == who.Id;
                    mark.Text = pov ? "★" : present ? "●" : "";
                    mark.Foreground = pov ? Brushes.DarkGoldenrod : Brushes.SteelBlue;
                    cell.Background = pov ? Palette.PovBg : rowBg;
                }
                Paint();
                repaint.Add(Paint);
                cell.Child = mark;
                cell.ToolTip = $"{book.ChapterNumber(ch)}. {ch.Title} — {person.Name}";
                cell.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount >= 2)                                   // double-click: point of view
                    {
                        if (!chapter.CharacterIds.Contains(who.Id)) chapter.CharacterIds.Add(who.Id);
                        chapter.PovCharacterId = chapter.PovCharacterId == who.Id ? null : who.Id;
                    }
                    else if (chapter.CharacterIds.Contains(who.Id))          // single click: in or out
                    {
                        chapter.CharacterIds.Remove(who.Id);
                        if (chapter.PovCharacterId == who.Id) chapter.PovCharacterId = null;
                    }
                    else chapter.CharacterIds.Add(who.Id);

                    foreach (var paint in repaint) paint();                  // one POV per chapter: repaint the column too
                    changed();
                    e.Handled = true;
                };
                Grid.SetRow(cell, r + 1); Grid.SetColumn(cell, c + 1);
                grid.Children.Add(cell);
            }
        }

        Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
}

/// <summary>"Scan chapters for mentions": which chapters name this character, with a checkbox to link each.</summary>
public sealed class MentionsWindow : Window
{
    private sealed class Row
    {
        public required Chapter Chapter { get; init; }
        public required string Label { get; init; }
        public int Count { get; init; }
        public string Matched { get; init; } = "";
        public bool Link { get; set; }
        public bool AlreadyLinked { get; init; }
    }

    /// <summary>Chapters the user chose to link (already-linked ones excluded).</summary>
    public List<Chapter> ToLink { get; } = new();

    public MentionsWindow(Book book, Character character, Func<Chapter, string> textOf)
    {
        Title = $"Where \"{character.Name}\" is mentioned";
        Width = 720; Height = 560; MinWidth = 560; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var rows = new List<Row>();
        foreach (var ch in book.Chapters)
        {
            var m = MentionFinder.Find(textOf(ch), new[] { character }).FirstOrDefault();
            var linked = ch.CharacterIds.Contains(character.Id);
            if (m == null && !linked) continue;
            rows.Add(new Row
            {
                Chapter = ch, Label = $"{(book.SectionOf(ch) is { } s ? s.Title + " · " : "")}{book.ChapterNumber(ch)}. {ch.Title}",
                Count = m?.Count ?? 0, Matched = m?.MatchedName ?? "", AlreadyLinked = linked, Link = linked || m != null
            });
        }

        var root = new DockPanel { Margin = new Thickness(16) };
        var names = string.Join(", ", MentionFinder.NamesOf(character));
        var head = new TextBlock { Text = $"Looked for: {names}. Tick the chapters to list \"{character.Name}\" as present in; chapters already linked are shown for reference.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "Link ticked chapters", Padding = new Thickness(12, 4, 12, 4), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        ok.Click += (_, _) => { ToLink.AddRange(rows.Where(r => r.Link && !r.AlreadyLinked).Select(r => r.Chapter)); DialogResult = true; Close(); };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var grid = TimelineView.MakeGrid();
        grid.IsReadOnly = false;
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Present", Binding = new System.Windows.Data.Binding(nameof(Row.Link)) { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, Width = 70 });
        var c1 = TimelineView.Col("Chapter", nameof(Row.Label), 0); c1.IsReadOnly = true; grid.Columns.Add(c1);
        var c2 = TimelineView.Col("Mentions", nameof(Row.Count), 80); c2.IsReadOnly = true; grid.Columns.Add(c2);
        var c3 = TimelineView.Col("First as", nameof(Row.Matched), 140); c3.IsReadOnly = true; grid.Columns.Add(c3);
        var c4 = new DataGridCheckBoxColumn { Header = "Linked now", Binding = new System.Windows.Data.Binding(nameof(Row.AlreadyLinked)), IsReadOnly = true, Width = 90 }; grid.Columns.Add(c4);
        grid.ItemsSource = rows;
        root.Children.Add(rows.Count == 0
            ? new TextBlock { Text = "Not mentioned in any chapter (by name or alias).", Foreground = Brushes.Gray }
            : grid);
        Content = root;
    }
}

/// <summary>Book › Check Consistency: the rule-based findings, double-click to go to the node concerned.</summary>
public sealed class ConsistencyWindow : Window
{
    private sealed record Row(Finding Finding, string Severity, string Rule, string Message);

    private readonly Func<bool, IReadOnlyList<Finding>> _check;
    private readonly DataGrid _grid = TimelineView.MakeGrid();
    private readonly CheckBox _scanText = new() { Content = "Also scan chapter text for character names (slower)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _summary = new() { Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

    public ConsistencyWindow(Func<bool, IReadOnlyList<Finding>> check, Action<Guid> navigate)
    {
        _check = check;
        Title = "Consistency Check";
        Width = 860; Height = 560; MinWidth = 640; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var refresh = new Button { Content = "Check again", Padding = new Thickness(10, 3, 10, 3) };
        refresh.Click += (_, _) => Run();
        _scanText.Checked += (_, _) => Run();
        _scanText.Unchecked += (_, _) => Run();
        top.Children.Add(refresh); top.Children.Add(_scanText); top.Children.Add(_summary);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        var help = new TextBlock { Text = "Rules only, no AI: dangling links, point of view not present, plotlines unresolved or without chapters, one-sided convergences, events told out of story order, missing summaries, and (with the text scan) characters named before they are linked. Double-click a line to go there.", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(help, Dock.Top);
        root.Children.Add(help);

        _grid.Columns.Add(TimelineView.Col("Severity", nameof(Row.Severity), 80));
        _grid.Columns.Add(TimelineView.Col("Rule", nameof(Row.Rule), 110));
        var msg = TimelineView.Col("Finding", nameof(Row.Message), 0);
        msg.ElementStyle = new Style(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap) } };
        _grid.Columns.Add(msg);
        _grid.MouseDoubleClick += (_, _) => { if (_grid.SelectedItem is Row r && r.Finding.TargetId is { } id) navigate(id); };
        root.Children.Add(_grid);
        Content = root;
        Run();
    }

    public void Run()
    {
        Cursor = Cursors.Wait;
        try
        {
            var findings = _check(_scanText.IsChecked == true);
            _grid.ItemsSource = findings.Select(f => new Row(f, f.Severity.ToString(), f.Rule, f.Message)).ToList();
            _summary.Text = findings.Count == 0 ? "Nothing to report."
                : $"{findings.Count(f => f.Severity == Severity.Error)} errors, {findings.Count(f => f.Severity == Severity.Warning)} warnings, {findings.Count(f => f.Severity == Severity.Info)} notes";
        }
        finally { Cursor = null; }
    }
}
