using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using AuthorPlus.Core.Models;
using Section = AuthorPlus.Core.Models.Section;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Import;
using Eaglin.AiManager;
using Eaglin.AiManager.Wpf;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace AuthorPlus.App;

/// <summary>
/// The single main window: a tree of the book on the left — the book itself, its sections,
/// their chapters, the items hanging off each (summary, analyses, characters-in-chapter, notes),
/// then characters, timeline and plotlines — and an editor for the selected node on the right.
/// Chapters edit in a RichTextBox whose FlowDocument is saved as XAML; items get an editor per
/// kind; everything else edits in a field form built at runtime. Switching selection saves the
/// node being left. Right-click gives each node type its own menu (the CIATLE tree pattern).
/// </summary>
public partial class MainWindow : Window
{
    private readonly BookStore _store = new();
    private readonly AiHub     _ai    = AiHub.Open("AuthorPlus");   // the shared AI Manager (keys, log, ledger, prompts)
    private Book?   _book;
    private object? _current;           // Book | Section | Chapter | Item | Character | TimelineEvent | Plotline | string (group header)
    private RichTextBox? _rtb;          // live only while a chapter is open
    private bool _loading;              // suppress change handlers while populating
    private bool _dirty;
    private CancellationTokenSource? _aiCts;
    private readonly HashSet<Guid> _collapsed = new();   // tree nodes the user closed (everything else stays open)

    private const string RecentKey = "recent_books.txt";
    private const string CharactersHeader = "Characters", TimelineHeader = "Timeline", PlotlinesHeader = "Plotlines";

    public MainWindow()
    {
        InitializeComponent();
        _ai.Prompts.RegisterDefaults(AiPrompts.Defaults);
        _ai.BudgetExceeded += b => Dispatcher.Invoke(() =>
            TxtAi.Text = $"AI: over the ${b.BudgetUsd:0.00} monthly budget (${b.SpentUsd:0.00} so far)");
        Loaded += (_, _) =>
        {
            BuildRecentMenu();
            UpdateMenus();
            // "AuthorPlus.exe <book folder>" opens that book (also the hook for a file association).
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && Directory.Exists(args[1])) TryOpenFolder(args[1]);
        };
        Closing += MainWindow_Closing;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.Alt) { MoveUp_Click(this, e); e.Handled = true; }
            if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt) { MoveDown_Click(this, e); e.Handled = true; }
            if (e.Key == Key.Delete && Tree.IsKeyboardFocusWithin) { DeleteItem_Click(this, e); e.Handled = true; }
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    // FILE
    // ══════════════════════════════════════════════════════════════════════════

    private void HasBook_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _book != null;

    private void NewBook_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!ConfirmDiscardOrSave()) return;
        var dlg = new PromptWindow("New Book", "Title:", "Author:") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value1)) return;

        var book = _store.Create(dlg.Value1.Trim(), dlg.Value2.Trim());
        book.Chapters.Add(new Chapter { Title = "Chapter 1" });
        _store.Save(book);
        OpenBook(book);
    }

    private void OpenBook_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!ConfirmDiscardOrSave()) return;
        using var dlg = new WinForms.FolderBrowserDialog
        {
            Description = "Select a book folder (it contains book.json)",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_store.Root) ? _store.Root : string.Empty
        };
        if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
        TryOpenFolder(dlg.SelectedPath);
    }

    private void TryOpenFolder(string folder)
    {
        try { OpenBook(_store.Load(folder)); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the book:\n\n{ex.Message}", "Open Book", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenBook(Book book)
    {
        _book = book;
        _current = null;
        _dirty = false;
        _collapsed.Clear();
        TxtBookTitle.Text = book.Title;
        Title = $"{book.Title} — AuthorPlus";
        RememberRecent(book.FolderPath);
        BuildRecentMenu();
        BuildTree(select: book.Chapters.FirstOrDefault() ?? (object)book);
        UpdateStatus($"Opened {book.FolderPath}");
        UpdateMenus();
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => SaveAll();

    private void SaveAll()
    {
        if (_book == null) return;
        try
        {
            CommitCurrent();
            _store.Save(_book);
            _dirty = false;
            UpdateStatus($"Saved {DateTime.Now:HH:mm:ss}");
            RefreshTreeTexts();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Save failed:\n\n{ex.Message}", "Save", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ConfirmDiscardOrSave()
    {
        if (_book == null) return true;
        CommitCurrent();
        if (!_dirty) return true;
        var r = MessageBox.Show(this, $"Save changes to \"{_book.Title}\"?", "AuthorPlus",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) SaveAll();
        return true;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmDiscardOrSave()) e.Cancel = true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = _book?.FolderPath ?? _store.Root;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    // ── Recent books (a plain text list in %APPDATA%\AuthorPlus) ─────────────

    private static string RecentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuthorPlus", RecentKey);

    private static List<string> LoadRecent()
    {
        try { return File.Exists(RecentPath) ? File.ReadAllLines(RecentPath).Where(Directory.Exists).Take(8).ToList() : new(); }
        catch { return new(); }
    }

    private static void RememberRecent(string folder)
    {
        try
        {
            var list = LoadRecent();
            list.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, folder);
            Directory.CreateDirectory(Path.GetDirectoryName(RecentPath)!);
            File.WriteAllLines(RecentPath, list.Take(8));
        }
        catch { /* not important */ }
    }

    private void BuildRecentMenu()
    {
        MnuRecent.Items.Clear();
        var recent = LoadRecent();
        // Also list every book in the library root so a first-time user sees their books.
        foreach (var (folder, title, _) in _store.ListBooks())
            if (!recent.Contains(folder, StringComparer.OrdinalIgnoreCase)) recent.Add(folder);

        if (recent.Count == 0) { MnuRecent.Items.Add(new MenuItem { Header = "(no books yet)", IsEnabled = false }); return; }
        foreach (var folder in recent)
        {
            var item = new MenuItem { Header = Path.GetFileName(folder), ToolTip = folder };
            item.Click += (_, _) => { if (ConfirmDiscardOrSave()) TryOpenFolder(folder); };
            MnuRecent.Items.Add(item);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TREE
    // ══════════════════════════════════════════════════════════════════════════

    private static string Glyph(object node) => node switch
    {
        Book          => "📖",
        Section       => "📚",
        Chapter       => "📄",
        Item i        => i.Kind switch
        {
            ItemKind.Summary             => "✦",
            ItemKind.Analysis            => "🔍",
            ItemKind.CharactersInChapter => "👥",
            ItemKind.Outline             => "☰",
            _                            => "📝"
        },
        Character     => "👤",
        TimelineEvent => "🕒",
        Plotline      => "🧵",
        _             => ""
    };

    private string Label(object node) => node switch
    {
        Book b          => b.Title,
        Section s       => s.Title,
        Chapter c       => $"{_book!.ChapterNumber(c)}. {c.Title}",
        Item i          => i.Kind == ItemKind.Summary ? "Summary" : i.Title.Length > 0 ? i.Title : i.Kind.ToString(),
        Character c     => c.Name,
        TimelineEvent t => string.IsNullOrWhiteSpace(t.When) ? t.Title : $"{t.When} — {t.Title}",
        Plotline p      => p.Name,
        string s        => s,
        _               => node.ToString() ?? ""
    };

    private static Guid? IdOf(object node) => node switch
    {
        Book b => b.Id, Section s => s.Id, Chapter c => c.Id, Item i => i.Id,
        Character c => c.Id, TimelineEvent t => t.Id, Plotline p => p.Id, _ => null
    };

    private TreeViewItem Node(object model, bool bold = false)
    {
        var tvi = new TreeViewItem
        {
            Header = $"{Glyph(model)} {Label(model)}".Trim(),
            Tag = model,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            IsExpanded = IdOf(model) is not { } id || !_collapsed.Contains(id)
        };
        tvi.Expanded  += (_, e) => { if (ReferenceEquals(e.OriginalSource, tvi) && IdOf(model) is { } g) _collapsed.Remove(g); };
        tvi.Collapsed += (_, e) => { if (ReferenceEquals(e.OriginalSource, tvi) && IdOf(model) is { } g) _collapsed.Add(g); };
        return tvi;
    }

    private void BuildTree(object? select = null)
    {
        _loading = true;
        Tree.Items.Clear();
        if (_book == null) { _loading = false; return; }

        var root = Node(_book, bold: true);
        foreach (var item in _book.ItemsOf(_book.Id)) root.Items.Add(Node(item));
        foreach (var section in _book.Sections)
        {
            var sNode = Node(section, bold: true);
            foreach (var item in _book.ItemsOf(section.Id)) sNode.Items.Add(Node(item));
            foreach (var chapter in _book.ChaptersOf(section)) sNode.Items.Add(ChapterNode(chapter));
            root.Items.Add(sNode);
        }
        foreach (var chapter in _book.UnsectionedChapters) root.Items.Add(ChapterNode(chapter));
        Tree.Items.Add(root);

        Tree.Items.Add(Group(CharactersHeader, _book.Characters));
        Tree.Items.Add(Group(TimelineHeader,   _book.Timeline));
        Tree.Items.Add(Group(PlotlinesHeader,  _book.Plotlines));
        _loading = false;

        if (select != null) SelectNode(select);
        RefreshTreeTexts();
    }

    private TreeViewItem ChapterNode(Chapter chapter)
    {
        var node = Node(chapter);
        foreach (var item in _book!.ItemsOf(chapter.Id)) node.Items.Add(Node(item));
        return node;
    }

    private TreeViewItem Group<T>(string header, List<T> items) where T : class
    {
        var node = new TreeViewItem
        {
            Header = $"{header} ({items.Count})", Tag = header, FontWeight = FontWeights.SemiBold,
            IsExpanded = !_collapsed.Contains(GroupKey(header)), Margin = new Thickness(0, 6, 0, 0)
        };
        node.Expanded  += (_, e) => { if (ReferenceEquals(e.OriginalSource, node)) _collapsed.Remove(GroupKey(header)); };
        node.Collapsed += (_, e) => { if (ReferenceEquals(e.OriginalSource, node)) _collapsed.Add(GroupKey(header)); };
        foreach (var item in items) node.Items.Add(Node(item));
        return node;
    }

    private static Guid GroupKey(string header) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(header)));

    private void RefreshTreeTexts()
    {
        if (_book == null) return;
        foreach (var tvi in AllNodes())
        {
            if (tvi.Tag is string header)
            {
                var count = header switch { CharactersHeader => _book.Characters.Count, TimelineHeader => _book.Timeline.Count, _ => _book.Plotlines.Count };
                tvi.Header = $"{header} ({count})";
            }
            else if (tvi.Tag is not null) tvi.Header = $"{Glyph(tvi.Tag)} {Label(tvi.Tag)}".Trim();
        }
        TxtBookTitle.Text = _book.Title;
        TxtWords.Text = $"{_book.TotalWords:N0} words" + (_book.TargetWords > 0 ? $" of {_book.TargetWords:N0}" : "");
    }

    private IEnumerable<TreeViewItem> AllNodes()
    {
        var stack = new Stack<TreeViewItem>(Tree.Items.OfType<TreeViewItem>());
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            foreach (var c in n.Items.OfType<TreeViewItem>()) stack.Push(c);
        }
    }

    private TreeViewItem? FindNode(object model) => AllNodes().FirstOrDefault(n => ReferenceEquals(n.Tag, model));

    private void SelectNode(object model)
    {
        var node = FindNode(model);
        if (node == null) return;
        for (var p = node.Parent as TreeViewItem; p != null; p = p.Parent as TreeViewItem) p.IsExpanded = true;
        node.IsSelected = true;
        node.BringIntoView();
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_loading) return;
        CommitCurrent();
        _current = (Tree.SelectedItem as TreeViewItem)?.Tag;
        ShowCurrent();
        UpdateMenus();
    }

    /// <summary>Right-click selects the node under the mouse and shows its menu.</summary>
    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var node = (e.OriginalSource as DependencyObject)?.FindAncestor<TreeViewItem>();
        if (node == null) return;
        node.IsSelected = true;
        node.Focus();
        var menu = BuildContextMenu(node.Tag);
        if (menu.Items.Count == 0) return;
        menu.PlacementTarget = node;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu BuildContextMenu(object? tag)
    {
        var m = new ContextMenu();
        void Add(string header, RoutedEventHandler handler, bool enabled = true) { var mi = new MenuItem { Header = header, IsEnabled = enabled }; mi.Click += handler; m.Items.Add(mi); }
        void Sep() => m.Items.Add(new Separator());

        switch (tag)
        {
            case Book:
                Add("Add Section", AddSection_Click);
                Add("Add Chapter (book level)", AddChapter_Click);
                Add("Add Notes", AddNotesItem_Click);
                Add("Add Outline", AddOutlineItem_Click);
                Sep();
                Add("Import Chapters from Folder…", ImportFolder_Click);
                Sep();
                Add("Book Properties", BookProperties_Click);
                break;
            case Section:
                Add("Add Chapter", AddChapter_Click);
                Add("Insert Chapter from File…", InsertChapterFile_Click);
                Add("Import Chapters from Folder…", ImportFolder_Click);
                Add("Add Notes", AddNotesItem_Click);
                Add("Add Outline", AddOutlineItem_Click);
                Sep();
                Add("Outline This Section (AI)", AiSectionOutline_Click, _ai.IsAvailable());
                Add("Summarize This Section (AI)", AiSectionSummary_Click, _ai.IsAvailable());
                Sep();
                Add("Move Up", MoveUp_Click); Add("Move Down", MoveDown_Click);
                Add("Delete Section…", DeleteItem_Click);
                break;
            case Chapter:
                Add("Summarize (AI)", AiSummarize_Click, _ai.IsAvailable());
                Add("Analyze… (AI)", AiAnalyze_Click, _ai.IsAvailable());
                Add("Characters in This Chapter", AiCharactersInChapter_Click);
                Add("Add Notes", AddNotesItem_Click);
                Sep();
                Add("Insert Chapter from File After This…", InsertChapterFile_Click);
                Sep();
                Add("Move Up", MoveUp_Click); Add("Move Down", MoveDown_Click);
                Add("Delete Chapter…", DeleteItem_Click);
                break;
            case Item:
                Add("Move Up", MoveUp_Click); Add("Move Down", MoveDown_Click);
                Add("Delete…", DeleteItem_Click);
                break;
            case string header:
                Add(header == CharactersHeader ? "Add Character" : header == TimelineHeader ? "Add Timeline Event" : "Add Plotline",
                    header == CharactersHeader ? AddCharacter_Click : header == TimelineHeader ? AddEvent_Click : AddPlotline_Click);
                break;
            case Character:
                Add("Suggest Profile (AI)", AiCharacter_Click, _ai.IsAvailable());
                Sep();
                Add("Move Up", MoveUp_Click); Add("Move Down", MoveDown_Click);
                Add("Delete…", DeleteItem_Click);
                break;
            case TimelineEvent or Plotline:
                Add("Move Up", MoveUp_Click); Add("Move Down", MoveDown_Click);
                Add("Delete…", DeleteItem_Click);
                break;
        }
        return m;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EDITOR
    // ══════════════════════════════════════════════════════════════════════════

    private void ShowCurrent()
    {
        _loading = true;
        _rtb = null;
        TxtName.IsEnabled = _current is not (null or string) && _current is not Item { Kind: ItemKind.Summary };

        switch (_current)
        {
            case Book b:
                TxtKind.Text = "Book";
                TxtName.Text = b.Title;
                Editor.Content = FieldForm(
                    ("Author",       () => b.Author,   v => b.Author = v,   1),
                    ("Genre",        () => b.Genre,    v => b.Genre = v,    1),
                    ("Synopsis",     () => b.Synopsis, v => b.Synopsis = v, 6),
                    ("Target words", () => b.TargetWords > 0 ? b.TargetWords.ToString() : "", v => b.TargetWords = int.TryParse(v, out var n) ? n : 0, 1));
                break;
            case Section s:
                TxtKind.Text = "Section";
                TxtName.Text = s.Title;
                Editor.Content = FieldForm(
                    ("Summary (AI › Summarize This Section fills this from the chapter summaries)", () => s.Summary, v => s.Summary = v, 6),
                    ("Notes", () => s.Notes, v => s.Notes = v, 6));
                break;
            case Chapter ch:
                TxtKind.Text = $"Chapter {_book!.ChapterNumber(ch)}" + (_book.SectionOf(ch) is { } sec ? $" of {sec.Title}" : "");
                TxtName.Text = ch.Title;
                Editor.Content = BuildChapterEditor(ch);
                break;
            case Item it:
                TxtKind.Text = it.Kind switch
                {
                    ItemKind.Summary => "Summary", ItemKind.Analysis => "Analysis", ItemKind.CharactersInChapter => "Characters in chapter",
                    ItemKind.Outline => "Outline", _ => "Notes"
                } + " of " + OwnerLabel(it);
                TxtName.Text = it.Kind == ItemKind.Summary ? "Summary" : it.Title;
                Editor.Content = BuildItemEditor(it);
                break;
            case Character c:
                TxtKind.Text = "Character";
                TxtName.Text = c.Name;
                Editor.Content = FieldForm(
                    ("Role",        () => c.Role,        v => c.Role = v,        1),
                    ("Also called (other names the text uses, one per line)", () => c.Aliases, v => c.Aliases = v, 2),
                    ("Origin",      () => c.Origin,      v => c.Origin = v,      2),
                    ("Description", () => c.Description, v => c.Description = v, 4),
                    ("Motivations", () => c.Motivations, v => c.Motivations = v, 4),
                    ("Actions",     () => c.Actions,     v => c.Actions = v,     4),
                    ("Arc",         () => c.Arc,         v => c.Arc = v,         3),
                    ("Notes",       () => c.Notes,       v => c.Notes = v,       3));
                break;
            case TimelineEvent t:
                TxtKind.Text = "Event";
                TxtName.Text = t.Title;
                Editor.Content = FieldForm(
                    ("When",        () => t.When,        v => t.When = v,        1),
                    ("Description", () => t.Description, v => t.Description = v, 8));
                break;
            case Plotline p:
                TxtKind.Text = "Plotline";
                TxtName.Text = p.Name;
                Editor.Content = FieldForm(
                    ("Status (Planned / Active / Resolved)", () => p.Status.ToString(),
                        v => { if (Enum.TryParse<PlotlineStatus>(v, true, out var s)) p.Status = s; }, 1),
                    ("Summary", () => p.Summary, v => p.Summary = v, 6),
                    ("Notes",   () => p.Notes,   v => p.Notes = v,   4));
                break;
            default:
                TxtKind.Text = "";
                TxtName.Text = "";
                Editor.Content = new TextBlock
                {
                    Text = _book == null ? "Open a book to begin." : "Pick something in the tree, or right-click a node to add to it.",
                    Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                break;
        }
        _loading = false;
    }

    private string OwnerLabel(Item it)
    {
        if (_book == null) return "";
        if (it.OwnerId == _book.Id) return "the book";
        if (_book.Sections.FirstOrDefault(s => s.Id == it.OwnerId) is { } s) return s.Title;
        if (_book.Chapters.FirstOrDefault(c => c.Id == it.OwnerId) is { } c) return $"chapter {_book.ChapterNumber(c)}, {c.Title}";
        return "?";
    }

    /// <summary>A label + multi-line TextBox per field; edits flow straight into the model.</summary>
    private UIElement FieldForm(params (string Label, Func<string> Get, Action<string> Set, int Lines)[] fields)
    {
        var panel = new StackPanel();
        foreach (var (label, get, set, lines) in fields)
        {
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2), TextWrapping = TextWrapping.Wrap });
            var box = new TextBox
            {
                Text = get(),
                AcceptsReturn = lines > 1,
                TextWrapping = TextWrapping.Wrap,
                MinLines = lines,
                VerticalScrollBarVisibility = lines > 1 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
                Padding = new Thickness(4)
            };
            box.TextChanged += (_, _) => { if (_loading) return; set(box.Text); _dirty = true; };
            panel.Children.Add(box);
        }
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // ── Chapter editor ────────────────────────────────────────────────────────

    private UIElement BuildChapterEditor(Chapter ch)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Toolbar — formatting via the built-in EditingCommands.
        var bar = new ToolBar();
        bar.Items.Add(ToolButton("B", EditingCommands.ToggleBold, FontWeights.Bold));
        bar.Items.Add(ToolButton("I", EditingCommands.ToggleItalic, FontWeights.Normal, italic: true));
        bar.Items.Add(ToolButton("U", EditingCommands.ToggleUnderline, FontWeights.Normal, underline: true));
        bar.Items.Add(new Separator());
        bar.Items.Add(ToolButton("¶ Left", EditingCommands.AlignLeft, FontWeights.Normal));
        bar.Items.Add(ToolButton("¶ Center", EditingCommands.AlignCenter, FontWeights.Normal));
        bar.Items.Add(ToolButton("¶ Justify", EditingCommands.AlignJustify, FontWeights.Normal));
        bar.Items.Add(new Separator());
        var status = new ComboBox { ItemsSource = Enum.GetValues<ChapterStatus>(), SelectedItem = ch.Status, Width = 100, VerticalAlignment = VerticalAlignment.Center };
        status.SelectionChanged += (_, _) => { if (!_loading && status.SelectedItem is ChapterStatus s) { ch.Status = s; _dirty = true; } };
        bar.Items.Add(new TextBlock { Text = "Status:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) });
        bar.Items.Add(status);
        bar.Items.Add(new Separator());
        var summarize = new Button { Content = "Summarize (AI)", Padding = new Thickness(8, 2, 8, 2) };
        summarize.Click += AiSummarize_Click;
        var analyze = new Button { Content = "Analyze… (AI)", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
        analyze.Click += AiAnalyze_Click;
        var who = new Button { Content = "Characters in chapter", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
        who.Click += AiCharactersInChapter_Click;
        bar.Items.Add(summarize); bar.Items.Add(analyze); bar.Items.Add(who);
        Grid.SetRow(bar, 0);
        grid.Children.Add(bar);

        // The manuscript.
        _rtb = new RichTextBox
        {
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Georgia"),
            FontSize = 15,
            Padding = new Thickness(24, 16, 24, 16),
            SpellCheck = { IsEnabled = true },
            Document = LoadDocument(ch)
        };
        _rtb.TextChanged += (_, _) => { if (!_loading) { _dirty = true; UpdateWordCount(ch); } };
        Grid.SetRow(_rtb, 1);
        grid.Children.Add(_rtb);

        UpdateWordCount(ch);
        return grid;
    }

    private static Button ToolButton(string text, ICommand command, FontWeight weight, bool italic = false, bool underline = false)
    {
        var tb = new TextBlock { Text = text, FontWeight = weight, FontStyle = italic ? FontStyles.Italic : FontStyles.Normal };
        if (underline) tb.TextDecorations = TextDecorations.Underline;
        return new Button { Content = tb, Command = command, MinWidth = 28, Padding = new Thickness(6, 2, 6, 2) };
    }

    private FlowDocument LoadDocument(Chapter ch)
    {
        if (_book == null) return new FlowDocument();
        var xaml = _store.LoadChapterBody(_book, ch);
        if (string.IsNullOrEmpty(xaml)) return new FlowDocument(new Paragraph());
        try { return (FlowDocument)XamlReader.Parse(xaml); }
        catch (Exception ex)
        {
            ActivityLog.Error("Editor", $"Chapter body unreadable: {ch.Id}", ex);
            return new FlowDocument(new Paragraph(new Run("[The saved text of this chapter could not be read. The file is still in the chapters folder.]")));
        }
    }

    private static string PlainText(FlowDocument doc) =>
        new TextRange(doc.ContentStart, doc.ContentEnd).Text;

    /// <summary>The chapter's prose — from the live editor when it is open, else from disk.</summary>
    private string ChapterText(Chapter ch)
    {
        if (ReferenceEquals(_current, ch) && _rtb != null) return PlainText(_rtb.Document);
        var xaml = _store.LoadChapterBody(_book!, ch);
        if (string.IsNullOrEmpty(xaml)) return "";
        try { return PlainText((FlowDocument)XamlReader.Parse(xaml)); } catch { return ""; }
    }

    private void UpdateWordCount(Chapter ch)
    {
        if (_rtb == null) return;
        var words = WordCounter.Count(PlainText(_rtb.Document));
        TxtWords.Text = $"Chapter: {words:N0} words   ·   Book: {(_book?.TotalWords - ch.WordCount + words) ?? 0:N0}";
    }

    // ── Item editors ──────────────────────────────────────────────────────────

    private UIElement BuildItemEditor(Item it)
    {
        var panel = new DockPanel();
        var meta = new TextBlock { Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        meta.Text = (it.IsAiMade ? $"Written by {it.Provider} ({it.Model}) with the \"{it.PromptName}\" prompt · " : "") +
                    $"created {it.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}, last changed {it.ModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
        DockPanel.SetDock(meta, Dock.Top);
        panel.Children.Add(meta);

        if (it.Kind == ItemKind.CharactersInChapter)
        {
            panel.Children.Add(BuildCharactersInChapterEditor(it));
            return panel;
        }

        var body = new TextBox
        {
            Text = it.Body, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8),
            FontFamily = new System.Windows.Media.FontFamily(it.Kind == ItemKind.Analysis ? "Segoe UI" : "Georgia"),
            FontSize = 14
        };
        body.TextChanged += (_, _) => { if (!_loading) { it.Body = body.Text; it.ModifiedUtc = DateTime.UtcNow; _dirty = true; } };
        panel.Children.Add(body);
        return panel;
    }

    private UIElement BuildCharactersInChapterEditor(Item it)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var list = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        list.Children.Add(new TextBlock { Text = "Tick who appears; choose the point-of-view character.", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        if (_book!.Characters.Count == 0)
            list.Children.Add(new TextBlock { Text = "No characters yet — add them under Characters in the tree, then come back.", Foreground = System.Windows.Media.Brushes.Gray });
        foreach (var c in _book.Characters)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var pov = new RadioButton { Content = "POV", GroupName = "pov-" + it.Id, IsChecked = it.PovCharacterId == c.Id, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var check = new CheckBox { Content = c.Name, IsChecked = it.CharacterIds.Contains(c.Id), VerticalAlignment = VerticalAlignment.Center };
            check.Checked   += (_, _) => { if (_loading) return; if (!it.CharacterIds.Contains(c.Id)) it.CharacterIds.Add(c.Id); Touch(it); };
            check.Unchecked += (_, _) => { if (_loading) return; it.CharacterIds.Remove(c.Id); if (it.PovCharacterId == c.Id) { it.PovCharacterId = null; pov.IsChecked = false; } Touch(it); };
            pov.Checked += (_, _) => { if (_loading) return; it.PovCharacterId = c.Id; if (!it.CharacterIds.Contains(c.Id)) { it.CharacterIds.Add(c.Id); check.IsChecked = true; } Touch(it); };
            DockPanel.SetDock(pov, Dock.Right);
            row.Children.Add(pov);
            row.Children.Add(check);
            list.Children.Add(row);
        }
        Grid.SetRow(list, 0);
        grid.Children.Add(list);

        var notes = new DockPanel();
        notes.Children.Add(new TextBlock { Text = "What each does in this chapter (the AI's list, editable)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        DockPanel.SetDock(notes.Children[0], Dock.Top);
        var body = new TextBox { Text = it.Body, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
        body.TextChanged += (_, _) => { if (!_loading) { it.Body = body.Text; Touch(it); } };
        notes.Children.Add(body);
        Grid.SetRow(notes, 1);
        grid.Children.Add(notes);
        return grid;
    }

    private void Touch(Item it) { it.ModifiedUtc = DateTime.UtcNow; _dirty = true; }

    /// <summary>Pushes the on-screen state of the current node back into the model / disk.</summary>
    private void CommitCurrent()
    {
        if (_book == null || _current == null) return;
        if (_current is Chapter ch && _rtb != null && _dirty)
        {
            var xaml = XamlWriter.Save(_rtb.Document);
            _store.SaveChapterBody(_book, ch, xaml, WordCounter.Count(PlainText(_rtb.Document)));
        }
    }

    private void TxtName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current == null) return;
        switch (_current)
        {
            case Book b:           b.Title  = TxtName.Text; Title = $"{b.Title} — AuthorPlus"; break;
            case Section s:        s.Title  = TxtName.Text; break;
            case Chapter ch:       ch.Title = TxtName.Text; break;
            case Item it:          it.Title = TxtName.Text; break;
            case Character c:      c.Name   = TxtName.Text; break;
            case TimelineEvent t:  t.Title  = TxtName.Text; break;
            case Plotline p:       p.Name   = TxtName.Text; break;
        }
        _dirty = true;
        if (Tree.SelectedItem is TreeViewItem tvi && ReferenceEquals(tvi.Tag, _current))
            tvi.Header = $"{Glyph(_current)} {Label(_current)}".Trim();
        if (_current is Book) TxtBookTitle.Text = TxtName.Text;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // BOOK MENU — adding, moving, deleting
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>The section the current selection belongs to (a section, a chapter's section, or an item's owner's section).</summary>
    private Section? CurrentSection() => _current switch
    {
        Section s => s,
        Chapter c => _book?.SectionOf(c),
        Item i    => _book?.Sections.FirstOrDefault(s => s.Id == i.OwnerId) ?? (_book?.Chapters.FirstOrDefault(c => c.Id == i.OwnerId) is { } ch ? _book.SectionOf(ch) : null),
        _         => null
    };

    private Chapter? CurrentChapter() => _current switch
    {
        Chapter c => c,
        Item i    => _book?.Chapters.FirstOrDefault(c => c.Id == i.OwnerId),
        _         => null
    };

    /// <summary>Whose item a new Notes/Outline should be: the selected book/section/chapter (or the item's owner).</summary>
    private Guid CurrentOwnerId() => _current switch
    {
        Section s => s.Id,
        Chapter c => c.Id,
        Item i    => i.OwnerId,
        _         => _book!.Id
    };

    private void AddSection_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        CommitCurrent();
        var s = new Section { Title = $"Part {_book.Sections.Count + 1}" };
        _book.Sections.Add(s);
        _dirty = true;
        BuildTree(select: s);
        TxtName.Focus(); TxtName.SelectAll();
    }

    private void AddChapter_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        CommitCurrent();
        var section = CurrentSection();
        var ch = new Chapter { Title = "New Chapter", SectionId = section?.Id };
        int index = _current is Chapter cur ? _book.Chapters.IndexOf(cur) + 1 : InsertIndexAtEnd(section);
        _book.Chapters.Insert(index, ch);
        _dirty = true;
        BuildTree(select: ch);
        TxtName.Focus(); TxtName.SelectAll();
    }

    private int InsertIndexAtEnd(Section? section)
    {
        if (section == null) return _book!.Chapters.Count;
        int last = -1;
        for (int i = 0; i < _book!.Chapters.Count; i++) if (_book.Chapters[i].SectionId == section.Id) last = i;
        return last >= 0 ? last + 1 : _book.Chapters.Count;
    }

    private void AddNotesItem_Click(object sender, RoutedEventArgs e)   => AddItemOfKind(ItemKind.Notes, "Notes");
    private void AddOutlineItem_Click(object sender, RoutedEventArgs e) => AddItemOfKind(ItemKind.Outline, "Outline");

    private void AddItemOfKind(ItemKind kind, string title)
    {
        if (_book == null) return;
        CommitCurrent();
        var it = new Item { OwnerId = CurrentOwnerId(), Kind = kind, Title = title };
        _book.Items.Add(it);
        _dirty = true;
        BuildTree(select: it);
        TxtName.Focus(); TxtName.SelectAll();
    }

    private void AddCharacter_Click(object sender, RoutedEventArgs e) => AddToList(_book?.Characters, new Character());
    private void AddEvent_Click(object sender, RoutedEventArgs e)     => AddToList(_book?.Timeline,   new TimelineEvent { Order = (_book?.Timeline.Count ?? 0) + 1 });
    private void AddPlotline_Click(object sender, RoutedEventArgs e)  => AddToList(_book?.Plotlines,  new Plotline());

    private void AddToList<T>(List<T>? list, T item) where T : class
    {
        if (_book == null || list == null) return;
        CommitCurrent();
        list.Add(item);
        _dirty = true;
        BuildTree(select: item);
        TxtName.Focus(); TxtName.SelectAll();
    }

    private void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        CommitCurrent();
        var dlg = new ImportWindow(_book, _store, CurrentSection()) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _dirty = false;                                          // the importer saved the book
        BuildTree(select: dlg.Created.FirstOrDefault() ?? (object?)dlg.TargetSection ?? _book);
        UpdateStatus($"Imported {dlg.Created.Count} chapter(s), {dlg.Created.Sum(c => c.WordCount):N0} words.");
    }

    private void InsertChapterFile_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        CommitCurrent();
        var dlg = new OpenFileDialog
        {
            Title = "Choose the chapter file",
            Filter = "Manuscript files (*.docx;*.txt;*.md)|*.docx;*.txt;*.md|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        var section = CurrentSection();
        int? insertAt = _current is Chapter cur ? _book.Chapters.IndexOf(cur) + 1 : null;
        try
        {
            var importer = new ManuscriptImporter(_store);
            var ch = importer.ImportOne(_book, dlg.FileName, null, section, insertAt);
            _dirty = true;
            BuildTree(select: ch);
            UpdateStatus($"Inserted \"{ch.Title}\" ({ch.WordCount:N0} words) as chapter {_book.ChapterNumber(ch)}{(section != null ? $" of {section.Title}" : "")}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not read that file:\n\n{ex.Message}", "Insert Chapter", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)   => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        if (_book == null || _current == null) return;
        bool moved = _current switch
        {
            Section s       => Shift(_book.Sections, s, delta),
            Chapter c       => ShiftAmong(_book.Chapters, c, delta, x => x.SectionId == c.SectionId),
            Item i          => ShiftAmong(_book.Items, i, delta, x => x.OwnerId == i.OwnerId),
            Character c     => Shift(_book.Characters, c, delta),
            TimelineEvent t => Shift(_book.Timeline, t, delta),
            Plotline p      => Shift(_book.Plotlines, p, delta),
            _ => false
        };
        if (!moved) return;
        if (_current is TimelineEvent) for (int i = 0; i < _book.Timeline.Count; i++) _book.Timeline[i].Order = i + 1;
        CommitCurrent();
        _dirty = true;
        BuildTree(select: _current);
    }

    private static bool Shift<T>(List<T> list, T item, int delta) where T : class
    {
        int i = list.IndexOf(item), j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return false;
        (list[i], list[j]) = (list[j], list[i]);
        return true;
    }

    /// <summary>Swaps with the nearest sibling that satisfies <paramref name="sibling"/> (same section / same owner).</summary>
    private static bool ShiftAmong<T>(List<T> list, T item, int delta, Func<T, bool> sibling) where T : class
    {
        int i = list.IndexOf(item);
        if (i < 0) return false;
        for (int j = i + delta; j >= 0 && j < list.Count; j += delta)
        {
            if (!sibling(list[j])) continue;
            (list[i], list[j]) = (list[j], list[i]);
            return true;
        }
        return false;
    }

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || _current == null || _current is string) return;
        object? next = null;

        switch (_current)
        {
            case Book:
                MessageBox.Show(this, "The book itself cannot be deleted from here. Delete its folder in File › Open Book Folder if you really mean to.", "Delete", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            case Section s:
            {
                var n = _book.ChaptersOf(s).Count();
                var r = n == 0
                    ? MessageBox.Show(this, $"Delete the section \"{s.Title}\"?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                    : MessageBox.Show(this, $"Delete the section \"{s.Title}\" and its {n} chapter(s)?\n\nYes = delete the chapters too (their text files go on the next save)\nNo = keep the chapters at book level\nCancel = do nothing",
                        "Delete Section", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                if (r == MessageBoxResult.Cancel || (n == 0 && r != MessageBoxResult.Yes)) return;
                _book.RemoveSection(s, deleteChapters: r == MessageBoxResult.Yes);
                break;
            }
            case Chapter c:
                if (MessageBox.Show(this, $"Delete chapter \"{c.Title}\" and its items? Its text file will be deleted on the next save.", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                next = _book.SectionOf(c);
                _book.RemoveChapter(c);
                break;
            case Item it:
                if (MessageBox.Show(this, $"Delete this {(it.Kind == ItemKind.Summary ? "summary" : it.Kind == ItemKind.Analysis ? "analysis" : "item")} of {OwnerLabel(it)}?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                next = _book.Chapters.FirstOrDefault(c => c.Id == it.OwnerId) ?? _book.Sections.FirstOrDefault(s => s.Id == it.OwnerId) ?? (object)_book;
                _book.Items.Remove(it);
                break;
            case Character c:
                if (!ConfirmDelete(c.Name)) return;
                _book.Characters.Remove(c); break;
            case TimelineEvent t:
                if (!ConfirmDelete(t.Title)) return;
                _book.Timeline.Remove(t); break;
            case Plotline p:
                if (!ConfirmDelete(p.Name)) return;
                _book.Plotlines.Remove(p); break;
        }
        _current = null;
        _dirty = true;
        BuildTree(select: next);
        if (next == null) { ShowCurrent(); UpdateMenus(); }

        bool ConfirmDelete(string name) =>
            MessageBox.Show(this, $"Delete \"{name}\"?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void BookProperties_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        SelectNode(_book);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AI
    // ══════════════════════════════════════════════════════════════════════════

    private void AiSettings_Click(object sender, RoutedEventArgs e)
    {
        new AiSettingsWindow(_ai) { Owner = this }.ShowDialog();
        UpdateMenus();
    }

    private void AiPrompts_Click(object sender, RoutedEventArgs e) =>
        new PromptLibraryWindow(_ai) { Owner = this }.ShowDialog();

    private void AiUsage_Click(object sender, RoutedEventArgs e) =>
        new UsageDashboardWindow(_ai, appFilter: _ai.App) { Owner = this }.ShowDialog();

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var dir = AiPaths.LogsDir(_ai.Root);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    /// <summary>True when the default provider has a key; otherwise offers to open AI Settings.</summary>
    private bool EnsureAiOrExplain()
    {
        if (_ai.IsAvailable()) return true;
        var msg = $"No API key is stored for {AiHub.DisplayName(_ai.DefaultProvider)}. Keys are shared with your other apps through the AI Manager.\n\nOpen AI Settings now?";
        if (MessageBox.Show(this, msg, "AI", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            AiSettings_Click(this, new RoutedEventArgs());
        return false;
    }

    private string EarlierSummaries(Chapter ch)
    {
        var earlier = _book!.Chapters.TakeWhile(c => c.Id != ch.Id)
            .Select(c => (c, s: _book.SummaryText(c))).Where(x => x.s.Length > 0)
            .Select(x => $"{(_book.SectionOf(x.c) is { } sec ? sec.Title + ", " : "")}chapter {_book.ChapterNumber(x.c)} \"{x.c.Title}\": {x.s}");
        var text = string.Join("\n", earlier);
        return text.Length > 0 ? text : "(no earlier chapter summaries yet)";
    }

    private async void AiSummarize_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || CurrentChapter() is not { } ch) { UpdateStatus("Select a chapter first."); return; }
        var text = ChapterText(ch).Trim();
        if (text.Length < 200)
        {
            MessageBox.Show(this, "Write at least a few paragraphs first — there is not enough text to summarize.", "AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!EnsureAiOrExplain()) return;

        await RunAi($"Summarizing \"{ch.Title}\" with {AiHub.DisplayName(_ai.DefaultProvider)}…", async ct =>
        {
            var r = await _ai.RunTemplateAsync(AiPrompts.ChapterSummary, new { book = _book.Title, chapter = ch.Title, text }, ct);
            var item = _book.SetSummary(ch, r.Text.Trim(), r.Provider.ToString(), r.Model, AiPrompts.ChapterSummary);
            _dirty = true;
            BuildTree(select: item);
            return r;
        });
    }

    private void AiAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || CurrentChapter() is not { } ch) { UpdateStatus("Select a chapter first."); return; }
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var text = ChapterText(ch).Trim();
        var section = _book.SectionOf(ch)?.Title ?? "(none)";
        var context = EarlierSummaries(ch);
        var dlg = new AnalysisWindow(_ai, _book, ch, provider =>
            _ai.Prompts.Get(AiPrompts.ChapterAnalysis).Bind(new { book = _book.Title, section, chapter = ch.Title, context, text }, provider)) { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        _dirty = true;
        BuildTree(select: dlg.Created[^1]);
        UpdateStatus($"{dlg.Created.Count} analysis item(s) added to \"{ch.Title}\".");
    }

    private async void AiCharactersInChapter_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || CurrentChapter() is not { } ch) { UpdateStatus("Select a chapter first."); return; }
        CommitCurrent();
        var text = ChapterText(ch);

        // Local name scan first: cheap, offline, and right most of the time.
        var item = _book.ItemsOf(ch.Id).FirstOrDefault(i => i.Kind == ItemKind.CharactersInChapter)
                   ?? new Item { OwnerId = ch.Id, Kind = ItemKind.CharactersInChapter, Title = "Characters in this chapter" };
        if (!_book.Items.Contains(item)) _book.Items.Add(item);
        foreach (var c in _book.Characters)
        {
            var names = new[] { c.Name }.Concat(c.Aliases.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                        .Where(n => n.Length > 1);
            if (names.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase)) && !item.CharacterIds.Contains(c.Id))
                item.CharacterIds.Add(c.Id);
        }
        _dirty = true;

        if (!_ai.IsAvailable() || text.Trim().Length < 200)
        {
            BuildTree(select: item);
            UpdateStatus("Characters matched by name; no AI pass (no key or too little text).");
            return;
        }

        var known = _book.Characters.Count == 0 ? "(none recorded yet)"
            : string.Join("\n", _book.Characters.Select(c => $"{c.Name}{(c.Aliases.Length > 0 ? " (" + string.Join(", ", c.Aliases.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) + ")" : "")} — {c.Role}"));
        await RunAi($"Reading \"{ch.Title}\" for its characters…", async ct =>
        {
            var r = await _ai.RunTemplateAsync(AiPrompts.CharactersInChapter, new { chapter = ch.Title, known_characters = known, text }, ct);
            item.Body = r.Text.Trim();
            item.Provider = r.Provider.ToString(); item.Model = r.Model; item.PromptName = AiPrompts.CharactersInChapter;
            item.ModifiedUtc = DateTime.UtcNow;
            // Tick any known character the AI named; POV from the "(POV)" line.
            foreach (var line in item.Body.Split('\n'))
            {
                var name = line.Split('—', '-', ':')[0].Replace("(POV)", "").Trim().TrimStart('-', '*', ' ');
                var match = _book.Characters.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    c.Aliases.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase)));
                if (match == null) continue;
                if (!item.CharacterIds.Contains(match.Id)) item.CharacterIds.Add(match.Id);
                if (line.Contains("(POV)", StringComparison.OrdinalIgnoreCase) && item.PovCharacterId == null) item.PovCharacterId = match.Id;
            }
            _dirty = true;
            BuildTree(select: item);
            return r;
        });
    }

    private string SectionSummaries(Section s)
    {
        var lines = _book!.ChaptersOf(s).Select(c => $"{_book.ChapterNumber(c)}. {c.Title}: {(_book.SummaryText(c).Length > 0 ? _book.SummaryText(c) : "(no summary yet)")}");
        return string.Join("\n", lines);
    }

    private async void AiSectionOutline_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || CurrentSection() is not { } s) { UpdateStatus("Select a section first."); return; }
        if (!EnsureAiOrExplain()) return;
        if (!_book.ChaptersOf(s).Any(c => _book.SummaryText(c).Length > 0))
        {
            MessageBox.Show(this, "None of this section's chapters has a summary yet. Summarize a few chapters first (right-click a chapter › Summarize).", "AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await RunAi($"Outlining \"{s.Title}\"…", async ct =>
        {
            var r = await _ai.RunTemplateAsync(AiPrompts.SectionOutline, new { book = _book.Title, section = s.Title, summaries = SectionSummaries(s) }, ct);
            var item = new Item { OwnerId = s.Id, Kind = ItemKind.Outline, Title = $"Outline · {DateTime.Now:yyyy-MM-dd}", Body = r.Text.Trim(), Provider = r.Provider.ToString(), Model = r.Model, PromptName = AiPrompts.SectionOutline };
            _book.Items.Add(item);
            _dirty = true;
            BuildTree(select: item);
            return r;
        });
    }

    private async void AiSectionSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || CurrentSection() is not { } s) { UpdateStatus("Select a section first."); return; }
        if (!EnsureAiOrExplain()) return;
        if (!_book.ChaptersOf(s).Any(c => _book.SummaryText(c).Length > 0))
        {
            MessageBox.Show(this, "None of this section's chapters has a summary yet. Summarize a few chapters first.", "AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await RunAi($"Summarizing \"{s.Title}\"…", async ct =>
        {
            var r = await _ai.RunTemplateAsync(AiPrompts.SectionSummary, new { book = _book.Title, section = s.Title, summaries = SectionSummaries(s) }, ct);
            s.Summary = r.Text.Trim();
            _dirty = true;
            if (ReferenceEquals(_current, s)) ShowCurrent();
            return r;
        });
    }

    private async void AiCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || _current is not Character c) { UpdateStatus("Select a character first."); return; }
        if (!EnsureAiOrExplain()) return;

        var known = $"Name: {c.Name}\nRole: {c.Role}\nOrigin: {c.Origin}\nDescription: {c.Description}\nMotivations: {c.Motivations}\nActions: {c.Actions}\nArc: {c.Arc}\nNotes: {c.Notes}";
        var summaries = string.Join("\n\n", _book.Chapters.Where(x => _book.SummaryText(x).Length > 0).Select(x => $"{x.Title}: {_book.SummaryText(x)}"));

        await RunAi($"Drafting a profile for {c.Name} with {AiHub.DisplayName(_ai.DefaultProvider)}…", async ct =>
        {
            var r = await _ai.RunTemplateAsync(AiPrompts.CharacterProfile,
                new { book = _book.Title, synopsis = _book.Synopsis, known, summaries }, ct);
            c.Notes = (c.Notes.Length > 0 ? c.Notes + "\n\n" : "") + $"— AI suggestions ({DateTime.Now:yyyy-MM-dd}) —\n{r.Text.Trim()}";
            _dirty = true;
            ShowCurrent();
            return r;
        });
    }

    private async Task RunAi(string statusText, Func<CancellationToken, Task<AiResponse>> work)
    {
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        MnuAi.IsEnabled = false;
        TxtAi.Text = statusText;
        Cursor = Cursors.AppStarting;
        try
        {
            var r = await work(_aiCts.Token);
            TxtAi.Text = $"AI: done in {r.Elapsed.TotalSeconds:0.0}s · {r.InputTokens + r.OutputTokens:N0} tokens" +
                         (r.EstimatedCostUsd is { } cost ? $" · est. ${cost:0.0000}" : "");
        }
        catch (OperationCanceledException) { TxtAi.Text = "AI: cancelled."; }
        catch (AiException ex)
        {
            TxtAi.Text = "AI: failed.";
            MessageBox.Show(this, ex.Message, "AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (ex.Kind == AiErrorKind.NoKey) AiSettings_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            TxtAi.Text = "AI: failed.";
            ActivityLog.Error("AuthorPlus", statusText, ex);
            MessageBox.Show(this, ex.Message, "AI", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Cursor = Cursors.Arrow;
            MnuAi.IsEnabled = true;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MISC
    // ══════════════════════════════════════════════════════════════════════════

    private void UpdateMenus()
    {
        MnuBook.IsEnabled = _book != null;
        _ai.Reload();
        var p = _ai.DefaultProvider;
        TxtAi.Text = _ai.IsAvailable()
            ? $"AI: {AiHub.DisplayName(p)} · {_ai.DefaultModel(p)}"
            : "AI: not configured (AI › AI Settings…)";
    }

    private void UpdateStatus(string text)
    {
        TxtStatus.Text = text;
        RefreshTreeTexts();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var v = typeof(MainWindow).Assembly.GetName().Version;
        MessageBox.Show(this,
            $"AuthorPlus {v}\n\nAn AI-enhanced authoring tool for organising and writing books: sections, chapters, characters, timeline and plotlines.\n\n" +
            $"Books live in:\n{_store.Root}\n\n© Ron Eaglin",
            "About AuthorPlus", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

internal static class VisualTreeExtensions
{
    public static T? FindAncestor<T>(this DependencyObject start) where T : DependencyObject
    {
        for (var d = start; d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d) is { } p ? p : (d as FrameworkContentElement)?.Parent)
            if (d is T t) return t;
        return null;
    }
}
