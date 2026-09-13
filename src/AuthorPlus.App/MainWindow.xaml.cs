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
using AuthorPlus.Core.Services.Export;
using AuthorPlus.Core.Services.Import;
using AuthorPlus.Core.Services.Style;
using System.Windows.Threading;
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
    private Progress? _progress;                         // words-today tracking for the open book
    private DateTime _lastEdit;                          // for autosave: save once the author pauses
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _focusMode;
    private Rect _restoreBounds;

    public static readonly RoutedCommand FocusCommand = new("Focus", typeof(MainWindow));
    public static readonly RoutedCommand FindCommand  = new("Find",  typeof(MainWindow));
    public static readonly RoutedCommand ConsistencyCommand = new("Consistency", typeof(MainWindow));
    private ConsistencyWindow? _consistency;

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
        CommandBindings.Add(new CommandBinding(FocusCommand, (_, _) => ToggleFocusMode()));
        CommandBindings.Add(new CommandBinding(FindCommand,  (_, _) => ShowFind()));
        CommandBindings.Add(new CommandBinding(ConsistencyCommand, (_, _) => Consistency_Click(this, new RoutedEventArgs())));
        _autosave.Tick += (_, _) => { if (_book != null && _dirty && (DateTime.Now - _lastEdit).TotalSeconds >= 12) SaveAll(silent: true); };
        _autosave.Start();
        Deactivated += (_, _) => { if (_book != null && _dirty) SaveAll(silent: true); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _focusMode) { ToggleFocusMode(); e.Handled = true; }
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
        _progress = Progress.Load(book);
        _progress.Touch(book.TotalWords);
        _progress.Save(book);
        TxtSaved.Text = "";
        TxtBookTitle.Text = book.Title;
        Title = $"{book.Title} — AuthorPlus";
        RememberRecent(book.FolderPath);
        BuildRecentMenu();
        BuildTree(select: book.Chapters.FirstOrDefault() ?? (object)book);
        UpdateStatus($"Opened {book.FolderPath}");
        UpdateMenus();
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => SaveAll();

    /// <summary>Marks the book changed and restarts the autosave pause.</summary>
    private void MarkDirty()
    {
        _dirty = true;
        _lastEdit = DateTime.Now;
        TxtSaved.Text = "Unsaved changes";
    }

    private void SaveAll(bool silent = false)
    {
        if (_book == null) return;
        try
        {
            CommitCurrent();
            _store.Save(_book);
            _dirty = false;
            _progress?.Touch(_book.TotalWords);
            _progress?.Save(_book);
            TxtSaved.Text = $"{(silent ? "Autosaved" : "Saved")} {DateTime.Now:HH:mm:ss}";
            if (!silent) UpdateStatus($"Saved {DateTime.Now:HH:mm:ss}");
            RefreshTreeTexts();
        }
        catch (Exception ex)
        {
            if (silent) { TxtSaved.Text = "Autosave failed — use File › Save"; ActivityLog.Error("AuthorPlus", "Autosave", ex); return; }
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
        foreach (var item in items)
        {
            var child = Node(item);
            if (IdOf(item) is { } id) foreach (var it in _book!.ItemsOf(id)) child.Items.Add(Node(it));
            node.Items.Add(child);
        }
        return node;
    }

    /// <summary>The model object with this id, anywhere in the book.</summary>
    private object? FindById(Guid id)
    {
        if (_book == null) return null;
        if (_book.Id == id) return _book;
        return (object?)_book.Sections.FirstOrDefault(s => s.Id == id) ?? _book.Chapters.FirstOrDefault(c => c.Id == id)
            ?? _book.Items.FirstOrDefault(i => i.Id == id) ?? _book.Characters.FirstOrDefault(c => c.Id == id)
            ?? _book.Timeline.FirstOrDefault(t => t.Id == id) ?? (object?)_book.Plotlines.FirstOrDefault(p => p.Id == id);
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
        TxtWords.Text = WordsStatus(null, 0);
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
                Add("Continuity Check, whole book (AI)…", AiContinuity_Click, _ai.IsAvailable());
                Add("Plot Analysis, whole book (AI)…", AiPlot_Click, _ai.IsAvailable());
                Add("Check Consistency…", Consistency_Click);
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
                Add("Continuity Check (AI)…", AiContinuity_Click, _ai.IsAvailable());
                Add("Plot Analysis (AI)…", AiPlot_Click, _ai.IsAvailable());
                Sep();
                Add("Move Up", MoveUp_Click); Add("Move Down", MoveDown_Click);
                Add("Delete Section…", DeleteItem_Click);
                break;
            case Chapter:
                Add("Summarize (AI)", AiSummarize_Click, _ai.IsAvailable());
                Add("Analyze… (AI)", AiAnalyze_Click, _ai.IsAvailable());
                Add("Characters in This Chapter", AiCharactersInChapter_Click);
                Add("Style Report…", Style_Click);
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
                Add("Scan Chapters for Mentions…", ScanMentions_Click);
                Add("Suggest Profile (AI)", AiCharacter_Click, _ai.IsAvailable());
                Add("Add Notes", AddNotesItem_Click);
                Sep();
                Add("Move Up", MoveUp_Click); Add("Move Down", MoveDown_Click);
                Add("Delete…", DeleteItem_Click);
                break;
            case TimelineEvent or Plotline:
                Add("Add Notes", AddNotesItem_Click);
                Sep();
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
            {
                TxtKind.Text = "Character";
                TxtName.Text = c.Name;
                var appears = _book!.Chapters.Where(ch => ch.CharacterIds.Contains(c.Id))
                    .Select(ch => $"{(_book.SectionOf(ch) is { } s ? s.Title + " · " : "")}{_book.ChapterNumber(ch)}. {ch.Title}{(ch.PovCharacterId == c.Id ? "  (POV)" : "")}").ToList();
                var scan = new Button { Content = "Scan chapters for mentions…", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
                scan.Click += ScanMentions_Click;
                Editor.Content = FieldFormWith(new UIElement[]
                    {
                        new TextBlock { Text = "Appears in", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 2) },
                        new TextBlock { Text = appears.Count == 0 ? "No chapter lists this character yet." : string.Join("\n", appears), TextWrapping = TextWrapping.Wrap },
                        scan
                    },
                    ("Role",        () => c.Role,        v => c.Role = v,        1),
                    ("Also called (other names the text uses, one per line)", () => c.Aliases, v => c.Aliases = v, 2),
                    ("Origin",      () => c.Origin,      v => c.Origin = v,      2),
                    ("Description", () => c.Description, v => c.Description = v, 4),
                    ("Motivations", () => c.Motivations, v => c.Motivations = v, 4),
                    ("Actions",     () => c.Actions,     v => c.Actions = v,     4),
                    ("Arc",         () => c.Arc,         v => c.Arc = v,         3),
                    ("Notes",       () => c.Notes,       v => c.Notes = v,       3));
                break;
            }
            case TimelineEvent t:
                TxtKind.Text = "Event";
                TxtName.Text = t.Title;
                Editor.Content = FieldFormWith(new UIElement[]
                    {
                        new LinkPicker("Chapters where it is told", ChapterCandidates(), t.ChapterIds, MarkDirty, expanded: true),
                        new LinkPicker("Characters involved", CharacterCandidates(), t.CharacterIds, MarkDirty, expanded: true),
                        new LinkPicker("Plotlines", PlotlineCandidates(), t.PlotlineIds, MarkDirty)
                    },
                    ("When",        () => t.When,        v => t.When = v,        1),
                    ("Description", () => t.Description, v => t.Description = v, 6));
                break;
            case Plotline p:
                TxtKind.Text = "Plotline";
                TxtName.Text = p.Name;
                Editor.Content = FieldFormWith(new UIElement[]
                    {
                        new LinkPicker("Chapters it runs through", ChapterCandidates(), p.ChapterIds, MarkDirty),
                        new LinkPicker("Characters involved", CharacterCandidates(), p.CharacterIds, MarkDirty),
                        BuildConvergencesEditor(p)
                    },
                    ("Status (Planned / Active / Resolved)", () => p.Status.ToString(),
                        v => { if (Enum.TryParse<PlotlineStatus>(v, true, out var s)) p.Status = s; }, 1),
                    ("Summary", () => p.Summary, v => p.Summary = v, 5),
                    ("Notes",   () => p.Notes,   v => p.Notes = v,   3));
                break;
            case string header when _book != null:
                TxtKind.Text = "";
                TxtName.Text = header;
                Editor.Content = header switch
                {
                    TimelineHeader   => new TimelineView(_book, SelectNode, MarkDirty),
                    PlotlinesHeader  => new PlotlineBoard(_book, SelectNode, MarkDirty),
                    CharactersHeader => new CharactersView(_book, SelectNode),
                    _                => new TextBlock()
                };
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

    private IEnumerable<(Guid, string)> ChapterCandidates() =>
        _book!.Chapters.Select(ch => (ch.Id, $"{(_book.SectionOf(ch) is { } s ? _book.Sections.IndexOf(s) + 1 + "." : "")}{_book.ChapterNumber(ch)} {ch.Title}"));
    private IEnumerable<(Guid, string)> CharacterCandidates() => _book!.Characters.Select(c => (c.Id, c.Name));
    private IEnumerable<(Guid, string)> PlotlineCandidates()  => _book!.Plotlines.Select(p => (p.Id, p.Name));

    /// <summary>Convergences of a plotline: other plotline, the chapter it happens in, a note; kept mirrored on the other side.</summary>
    private UIElement BuildConvergencesEditor(Plotline p)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var rows = new StackPanel();
        void Rebuild()
        {
            rows.Children.Clear();
            foreach (var conv in p.Convergences.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var others = _book!.Plotlines.Where(o => o.Id != p.Id).ToList();
                var otherBox = new ComboBox { ItemsSource = others, DisplayMemberPath = "Name", SelectedItem = others.FirstOrDefault(o => o.Id == conv.OtherPlotlineId), Width = 180, Margin = new Thickness(0, 0, 6, 0) };
                var chapters = new List<object> { "(any chapter)" }; chapters.AddRange(_book.Chapters);
                var chapterBox = new ComboBox { ItemsSource = chapters, Width = 220, Margin = new Thickness(0, 0, 6, 0), SelectedItem = _book.Chapters.FirstOrDefault(c => c.Id == conv.ChapterId) ?? chapters[0] };
                chapterBox.ItemTemplate = ChapterTemplate();
                var note = new TextBox { Text = conv.Note, Margin = new Thickness(0, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center };
                var remove = new Button { Content = "✕", Padding = new Thickness(6, 1, 6, 1) };
                otherBox.SelectionChanged += (_, _) => { if (!_loading && otherBox.SelectedItem is Plotline o) { conv.OtherPlotlineId = o.Id; Mirror(p, conv); MarkDirty(); } };
                chapterBox.SelectionChanged += (_, _) => { if (!_loading) { conv.ChapterId = (chapterBox.SelectedItem as Chapter)?.Id; Mirror(p, conv); MarkDirty(); } };
                note.TextChanged += (_, _) => { if (!_loading) { conv.Note = note.Text; MarkDirty(); } };
                remove.Click += (_, _) => { p.Convergences.Remove(conv); MarkDirty(); Rebuild(); };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                row.Children.Add(new TextBlock { Text = "with", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                row.Children.Add(otherBox);
                row.Children.Add(new TextBlock { Text = "in", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
                row.Children.Add(chapterBox);
                row.Children.Add(note);
                rows.Children.Add(row);
            }
        }
        var add = new Button { Content = "Add convergence", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        add.Click += (_, _) =>
        {
            var other = _book!.Plotlines.FirstOrDefault(o => o.Id != p.Id);
            if (other == null) { MessageBox.Show(this, "Add another plotline first — a convergence needs two.", "Plotlines", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var conv = new PlotlineConvergence { OtherPlotlineId = other.Id };
            p.Convergences.Add(conv);
            Mirror(p, conv);
            MarkDirty();
            Rebuild();
        };
        panel.Children.Add(new TextBlock { Text = "Converges with (recorded on both plotlines)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(rows);
        panel.Children.Add(add);
        Rebuild();
        return panel;
    }

    /// <summary>Makes sure the other plotline records the same convergence back.</summary>
    private void Mirror(Plotline p, PlotlineConvergence conv)
    {
        var other = _book!.Plotlines.FirstOrDefault(o => o.Id == conv.OtherPlotlineId);
        if (other == null) return;
        var back = other.Convergences.FirstOrDefault(c => c.OtherPlotlineId == p.Id);
        if (back == null) other.Convergences.Add(new PlotlineConvergence { OtherPlotlineId = p.Id, ChapterId = conv.ChapterId, Note = conv.Note });
        else back.ChapterId = conv.ChapterId;
    }

    private DataTemplate ChapterTemplate()
    {
        var f = new FrameworkElementFactory(typeof(TextBlock));
        f.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(".") { Converter = new ChapterLabelConverter(_book!) });
        return new DataTemplate { VisualTree = f };
    }

    private sealed class ChapterLabelConverter(Book book) : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) => value is Chapter ch ? $"{book.ChapterNumber(ch)}. {ch.Title}" : value?.ToString() ?? "";
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
    }

    /// <summary>A field form with extra elements (pickers, read-only lists) appended after the fields.</summary>
    private UIElement FieldFormWith(IEnumerable<UIElement> extras, params (string Label, Func<string> Get, Action<string> Set, int Lines)[] fields)
    {
        var scroll = (ScrollViewer)FieldForm(fields);
        var panel = (StackPanel)scroll.Content;
        foreach (var e in extras) panel.Children.Add(e);
        return scroll;
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
            box.TextChanged += (_, _) => { if (_loading) return; set(box.Text); MarkDirty(); };
            panel.Children.Add(box);
        }
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    // ── Chapter editor ────────────────────────────────────────────────────────

    private UIElement BuildChapterEditor(Chapter ch)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Toolbar — formatting via the built-in EditingCommands.
        var bar = new ToolBar();
        bar.Items.Add(ToolButton("↶", ApplicationCommands.Undo, FontWeights.Normal));
        bar.Items.Add(ToolButton("↷", ApplicationCommands.Redo, FontWeights.Normal));
        bar.Items.Add(new Separator());
        bar.Items.Add(ToolButton("B", EditingCommands.ToggleBold, FontWeights.Bold));
        bar.Items.Add(ToolButton("I", EditingCommands.ToggleItalic, FontWeights.Normal, italic: true));
        bar.Items.Add(ToolButton("U", EditingCommands.ToggleUnderline, FontWeights.Normal, underline: true));
        bar.Items.Add(new Separator());
        bar.Items.Add(ToolButton("¶ Left", EditingCommands.AlignLeft, FontWeights.Normal));
        bar.Items.Add(ToolButton("¶ Center", EditingCommands.AlignCenter, FontWeights.Normal));
        bar.Items.Add(ToolButton("¶ Justify", EditingCommands.AlignJustify, FontWeights.Normal));
        bar.Items.Add(new Separator());
        bar.Items.Add(ToolButton("• List", EditingCommands.ToggleBullets, FontWeights.Normal));
        bar.Items.Add(ToolButton("1. List", EditingCommands.ToggleNumbering, FontWeights.Normal));
        bar.Items.Add(new Separator());
        var sizeBox = new ComboBox { ItemsSource = new[] { "12", "13", "14", "15", "16", "18", "20", "24" }, Width = 56, IsEditable = true, ToolTip = "Font size of the selection" };
        sizeBox.SelectionChanged += (_, _) => { if (!_loading && _rtb != null && double.TryParse(sizeBox.SelectedItem as string, out var sz)) { _rtb.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, sz); _rtb.Focus(); } };
        bar.Items.Add(new TextBlock { Text = "Size:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        bar.Items.Add(sizeBox);
        var heading = new Button { Content = "Heading", Padding = new Thickness(8, 2, 8, 2), ToolTip = "Make the current paragraph a heading (or back to body text)" };
        heading.Click += (_, _) => ToggleHeading();
        bar.Items.Add(heading);
        var scene = new Button { Content = "Scene break", Padding = new Thickness(8, 2, 8, 2), ToolTip = "Insert a centred * * * on its own line" };
        scene.Click += (_, _) => InsertSceneBreak();
        bar.Items.Add(scene);
        var find = new Button { Content = "Find…", Padding = new Thickness(8, 2, 8, 2), ToolTip = "Find and replace (Ctrl+F)" };
        find.Click += (_, _) => ShowFind();
        bar.Items.Add(find);
        bar.Items.Add(new Separator());
        var goal = new TextBox { Width = 60, Text = ch.TargetWords > 0 ? ch.TargetWords.ToString() : "", ToolTip = "Word goal for this chapter (blank = none)", VerticalAlignment = VerticalAlignment.Center };
        goal.TextChanged += (_, _) => { if (_loading) return; ch.TargetWords = int.TryParse(goal.Text.Replace(",", ""), out var g) ? g : 0; MarkDirty(); UpdateWordCount(ch); };
        bar.Items.Add(new TextBlock { Text = "Goal:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        bar.Items.Add(goal);
        bar.Items.Add(new Separator());
        var status = new ComboBox { ItemsSource = Enum.GetValues<ChapterStatus>(), SelectedItem = ch.Status, Width = 100, VerticalAlignment = VerticalAlignment.Center };
        status.SelectionChanged += (_, _) => { if (!_loading && status.SelectedItem is ChapterStatus s) { ch.Status = s; MarkDirty(); } };
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

        _findPanel = BuildFindPanel();
        Grid.SetRow(_findPanel, 1);
        grid.Children.Add(_findPanel);

        // Links: who is in the chapter (with POV) and which plotlines run through it.
        grid.RowDefinitions.Insert(2, new RowDefinition { Height = GridLength.Auto });
        var links = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        links.Children.Add(new LinkPicker("Characters present", CharacterCandidates(), ch.CharacterIds, () => { MarkDirty(); SyncCharactersItem(ch); },
            single: ch.PovCharacterId, singleChanged: id => ch.PovCharacterId = id));
        links.Children.Add(new LinkPicker("Plotlines in this chapter", PlotlineCandidates(), ch.PlotlineIds, () => { MarkDirty(); foreach (var p in _book!.Plotlines) { if (ch.PlotlineIds.Contains(p.Id)) { if (!p.ChapterIds.Contains(ch.Id)) p.ChapterIds.Add(ch.Id); } else p.ChapterIds.Remove(ch.Id); } }));
        Grid.SetRow(links, 2);
        grid.Children.Add(links);

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
        _rtb.TextChanged += (_, _) => { if (!_loading) { MarkDirty(); UpdateWordCount(ch); } };
        Grid.SetRow(_rtb, 3);
        grid.Children.Add(_rtb);

        UpdateWordCount(ch);
        return grid;
    }

    // ── Headings, scene breaks ────────────────────────────────────────────────

    private void ToggleHeading()
    {
        if (_rtb?.Selection.Start.Paragraph is not { } p) return;
        bool isHeading = p.FontWeight == FontWeights.Bold && p.FontSize >= 20;
        if (isHeading) { p.ClearValue(TextElement.FontWeightProperty); p.ClearValue(TextElement.FontSizeProperty); p.ClearValue(Block.MarginProperty); }
        else { p.FontWeight = FontWeights.Bold; p.FontSize = 22; p.Margin = new Thickness(0, 18, 0, 8); }
        MarkDirty();
        _rtb.Focus();
    }

    private void InsertSceneBreak()
    {
        if (_rtb == null) return;
        var here = _rtb.Selection.Start.Paragraph;
        var brk = new Paragraph(new Run(FlowDocumentXaml.SceneBreak)) { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
        var after = new Paragraph();
        if (here != null) { _rtb.Document.Blocks.InsertAfter(here, brk); _rtb.Document.Blocks.InsertAfter(brk, after); }
        else { _rtb.Document.Blocks.Add(brk); _rtb.Document.Blocks.Add(after); }
        _rtb.CaretPosition = after.ContentStart;
        MarkDirty();
        _rtb.Focus();
    }

    // ── Find and replace ──────────────────────────────────────────────────────

    private DockPanel? _findPanel;
    private TextBox? _findBox, _replaceBox;
    private TextBlock? _findStatus;

    private DockPanel BuildFindPanel()
    {
        var panel = new DockPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 4), Background = System.Windows.Media.Brushes.WhiteSmoke };
        _findBox = new TextBox { Width = 200, Margin = new Thickness(4, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center };
        _replaceBox = new TextBox { Width = 200, Margin = new Thickness(4, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center };
        _findStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(8, 0, 0, 0) };
        var next = new Button { Content = "Find next", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        var replace = new Button { Content = "Replace", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        var all = new Button { Content = "Replace all", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
        var close = new Button { Content = "✕", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 0, 4, 0) };
        next.Click += (_, _) => FindNext();
        replace.Click += (_, _) => ReplaceOne();
        all.Click += (_, _) => ReplaceAll();
        close.Click += (_, _) => { panel.Visibility = Visibility.Collapsed; _rtb?.Focus(); };
        _findBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { FindNext(); e.Handled = true; } if (e.Key == Key.Escape) { panel.Visibility = Visibility.Collapsed; _rtb?.Focus(); } };
        DockPanel.SetDock(close, Dock.Right);
        panel.Children.Add(close);
        panel.Children.Add(new TextBlock { Text = "Find:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        panel.Children.Add(_findBox);
        panel.Children.Add(new TextBlock { Text = "Replace:", VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(_replaceBox);
        panel.Children.Add(next); panel.Children.Add(replace); panel.Children.Add(all);
        panel.Children.Add(_findStatus);
        return panel;
    }

    private void ShowFind()
    {
        if (_findPanel == null || _rtb == null) { UpdateStatus("Open a chapter to search in it."); return; }
        _findPanel.Visibility = Visibility.Visible;
        if (_rtb.Selection.Text.Length > 0 && !_rtb.Selection.Text.Contains('\n')) _findBox!.Text = _rtb.Selection.Text;
        _findBox!.Focus();
        _findBox.SelectAll();
    }

    private void Find_Click(object sender, RoutedEventArgs e) => ShowFind();

    /// <summary>Finds the next occurrence after the current selection (wrapping once); selects it.</summary>
    private bool FindNext(bool fromStart = false)
    {
        if (_rtb == null || _findBox == null || _findBox.Text.Length == 0) return false;
        var needle = _findBox.Text;
        var start = fromStart ? _rtb.Document.ContentStart : _rtb.Selection.End;
        var hit = FindFrom(start, needle) ?? (fromStart ? null : FindFrom(_rtb.Document.ContentStart, needle));
        if (hit == null) { _findStatus!.Text = "Not found."; return false; }
        _rtb.Selection.Select(hit.Value.Start, hit.Value.End);
        _rtb.Focus();
        var rect = hit.Value.Start.GetCharacterRect(LogicalDirection.Forward);
        _rtb.ScrollToVerticalOffset(_rtb.VerticalOffset + rect.Top - _rtb.ActualHeight / 3);
        _findStatus!.Text = "";
        return true;
    }

    private static (TextPointer Start, TextPointer End)? FindFrom(TextPointer from, string needle)
    {
        for (var p = from; p != null; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
            var run = p.GetTextInRun(LogicalDirection.Forward);
            var idx = run.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var s = p.GetPositionAtOffset(idx);
            var e = s?.GetPositionAtOffset(needle.Length);
            if (s != null && e != null) return (s, e);
        }
        return null;
    }

    private void ReplaceOne()
    {
        if (_rtb == null || _findBox == null || _replaceBox == null) return;
        if (string.Equals(_rtb.Selection.Text, _findBox.Text, StringComparison.OrdinalIgnoreCase) && _findBox.Text.Length > 0)
        {
            _rtb.Selection.Text = _replaceBox.Text;
            MarkDirty();
        }
        FindNext();
    }

    private void ReplaceAll()
    {
        if (_rtb == null || _findBox == null || _replaceBox == null || _findBox.Text.Length == 0) return;
        int n = 0;
        _rtb.BeginChange();
        try
        {
            var p = _rtb.Document.ContentStart;
            while (FindFrom(p, _findBox.Text) is { } hit)
            {
                var range = new TextRange(hit.Start, hit.End);
                range.Text = _replaceBox.Text;
                p = range.End;
                n++;
                if (n > 10000) break;
            }
        }
        finally { _rtb.EndChange(); }
        if (n > 0) MarkDirty();
        _findStatus!.Text = $"Replaced {n}.";
    }

    // ── Distraction-free mode ─────────────────────────────────────────────────

    private void Focus_Click(object sender, RoutedEventArgs e) => ToggleFocusMode();

    private void ToggleFocusMode()
    {
        _focusMode = !_focusMode;
        if (_focusMode)
        {
            _restoreBounds = RestoreBounds;
            MainMenu.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;
            TreePane.Visibility = Visibility.Collapsed;
            Splitter.Visibility = Visibility.Collapsed;
            TreeColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            if (_rtb != null) { _rtb.Padding = new Thickness(Math.Max(24, (ActualWidth - 900) / 2), 40, Math.Max(24, (ActualWidth - 900) / 2), 40); _rtb.Focus(); }
        }
        else
        {
            MainMenu.Visibility = Visibility.Visible;
            StatusBar.Visibility = Visibility.Visible;
            TreePane.Visibility = Visibility.Visible;
            Splitter.Visibility = Visibility.Visible;
            TreeColumn.Width = new GridLength(320);
            SplitterColumn.Width = new GridLength(6);
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            if (_rtb != null) _rtb.Padding = new Thickness(24, 16, 24, 16);
        }
        MnuFocus.IsChecked = _focusMode;
    }

    // ── Export ────────────────────────────────────────────────────────────────

    private void ExportDocx_Click(object sender, RoutedEventArgs e) => Export("Word document (*.docx)|*.docx", ".docx", (src, path) => DocxWriter.Write(src, path));
    private void ExportMarkdown_Click(object sender, RoutedEventArgs e) => Export("Markdown (*.md)|*.md", ".md", (src, path) => MarkdownExporter.Write(src, path));

    private void Export(string filter, string ext, Action<ExportSource, string> write)
    {
        if (_book == null) return;
        CommitCurrent();
        var dlg = new SaveFileDialog { Title = "Export the whole book", Filter = filter, FileName = BookStore.SafeFolderName(_book.Title) + ext, AddExtension = true };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            write(ExportSource.Load(_book, _store), dlg.FileName);
            UpdateStatus($"Exported to {dlg.FileName}");
            if (MessageBox.Show(this, $"Exported {_book.Chapters.Count} chapters ({_book.TotalWords:N0} words) to\n{dlg.FileName}\n\nOpen it now?", "Export", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed:\n\n{ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Drag and drop in the tree ─────────────────────────────────────────────

    private System.Windows.Point _dragStart;
    private object? _dragCandidate;

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Tree);
        _dragCandidate = (e.OriginalSource as DependencyObject)?.FindAncestor<TreeViewItem>()?.Tag;
        if (_dragCandidate is string or Book or null) _dragCandidate = null;
    }

    private void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(Tree) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var payload = _dragCandidate;
        _dragCandidate = null;
        CommitCurrent();
        DragDrop.DoDragDrop(Tree, new DataObject("AuthorPlusNode", payload), DragDropEffects.Move);
    }

    private void Tree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanDrop(e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private bool CanDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("AuthorPlusNode")) return false;
        var target = (e.OriginalSource as DependencyObject)?.FindAncestor<TreeViewItem>()?.Tag;
        var moving = e.Data.GetData("AuthorPlusNode");
        if (target == null || ReferenceEquals(target, moving)) return false;
        return (moving, target) switch
        {
            (Chapter, Chapter or Section or Book) => true,
            (Section, Section) => true,
            (Item a, Item b) => a.OwnerId == b.OwnerId,
            (Character, Character) or (TimelineEvent, TimelineEvent) or (Plotline, Plotline) => true,
            _ => false
        };
    }

    private void Tree_Drop(object sender, DragEventArgs e)
    {
        if (_book == null || !CanDrop(e)) return;
        var target = (e.OriginalSource as DependencyObject)!.FindAncestor<TreeViewItem>()!.Tag!;
        var moving = e.Data.GetData("AuthorPlusNode")!;
        e.Handled = true;

        switch (moving, target)
        {
            case (Chapter c, Chapter t):                       // before the target, in the target's section
                _book.Chapters.Remove(c);
                c.SectionId = t.SectionId;
                _book.Chapters.Insert(_book.Chapters.IndexOf(t), c);
                break;
            case (Chapter c, Section s):                       // last in that section
                _book.Chapters.Remove(c);
                c.SectionId = s.Id;
                _book.Chapters.Insert(InsertIndexAtEnd(s), c);
                break;
            case (Chapter c, Book):                            // out of any section, at the end
                _book.Chapters.Remove(c);
                c.SectionId = null;
                _book.Chapters.Add(c);
                break;
            case (Section a, Section b):
                _book.Sections.Remove(a);
                _book.Sections.Insert(_book.Sections.IndexOf(b), a);
                break;
            case (Item a, Item b):
                _book.Items.Remove(a);
                _book.Items.Insert(_book.Items.IndexOf(b), a);
                break;
            case (Character a, Character b): MoveBefore(_book.Characters, a, b); break;
            case (TimelineEvent a, TimelineEvent b):
                MoveBefore(_book.Timeline, a, b);
                for (int i = 0; i < _book.Timeline.Count; i++) _book.Timeline[i].Order = i + 1;
                break;
            case (Plotline a, Plotline b): MoveBefore(_book.Plotlines, a, b); break;
        }
        MarkDirty();
        BuildTree(select: moving);

        static void MoveBefore<T>(List<T> list, T item, T before) where T : class
        {
            list.Remove(item);
            list.Insert(list.IndexOf(before), item);
        }
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
        TxtWords.Text = WordsStatus(ch, WordCounter.Count(PlainText(_rtb.Document)));
    }

    /// <summary>"Chapter: 2,345 / 3,000 · Book: 162,880 of 180,000 · today +412".</summary>
    private string WordsStatus(Chapter? ch, int liveChapterWords)
    {
        if (_book == null) return "";
        var bookWords = ch == null ? _book.TotalWords : _book.TotalWords - ch.WordCount + liveChapterWords;
        var parts = new List<string>();
        if (ch != null) parts.Add($"Chapter: {liveChapterWords:N0}{(ch.TargetWords > 0 ? $" / {ch.TargetWords:N0}" : "")} words");
        parts.Add($"Book: {bookWords:N0}{(_book.TargetWords > 0 ? $" of {_book.TargetWords:N0}" : "")} words");
        var today = (_progress?.WordsToday() ?? 0) + (ch == null ? 0 : liveChapterWords - ch.WordCount);
        if (_progress != null) parts.Add($"today {(today >= 0 ? "+" : "")}{today:N0}");
        return string.Join("   ·   ", parts);
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
        body.TextChanged += (_, _) => { if (!_loading) { it.Body = body.Text; it.ModifiedUtc = DateTime.UtcNow; MarkDirty(); } };
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
            check.Checked   += (_, _) => { if (_loading) return; if (!it.CharacterIds.Contains(c.Id)) it.CharacterIds.Add(c.Id); Touch(it); SyncChapterFromItem(it); };
            check.Unchecked += (_, _) => { if (_loading) return; it.CharacterIds.Remove(c.Id); if (it.PovCharacterId == c.Id) { it.PovCharacterId = null; pov.IsChecked = false; } Touch(it); SyncChapterFromItem(it); };
            pov.Checked += (_, _) => { if (_loading) return; it.PovCharacterId = c.Id; if (!it.CharacterIds.Contains(c.Id)) { it.CharacterIds.Add(c.Id); check.IsChecked = true; } Touch(it); SyncChapterFromItem(it); };
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

    private void Touch(Item it) { it.ModifiedUtc = DateTime.UtcNow; MarkDirty(); }

    /// <summary>The chapter's own links are the record; the Characters-in-chapter item mirrors them.</summary>
    private void SyncChapterFromItem(Item it)
    {
        if (_book?.Chapters.FirstOrDefault(c => c.Id == it.OwnerId) is not { } ch) return;
        ch.CharacterIds = it.CharacterIds.ToList();
        ch.PovCharacterId = it.PovCharacterId;
    }

    private void SyncCharactersItem(Chapter ch)
    {
        if (_book?.ItemsOf(ch.Id).FirstOrDefault(i => i.Kind == ItemKind.CharactersInChapter) is not { } it) return;
        it.CharacterIds = ch.CharacterIds.ToList();
        it.PovCharacterId = ch.PovCharacterId;
    }

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
        MarkDirty();
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
        Section s       => s.Id,
        Chapter c       => c.Id,
        Item i          => i.OwnerId,
        Character c     => c.Id,
        TimelineEvent t => t.Id,
        Plotline p      => p.Id,
        _               => _book!.Id
    };

    private void AddSection_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        CommitCurrent();
        var s = new Section { Title = $"Part {_book.Sections.Count + 1}" };
        _book.Sections.Add(s);
        MarkDirty();
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
        MarkDirty();
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
        MarkDirty();
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
        MarkDirty();
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
            MarkDirty();
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
        MarkDirty();
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
                _book.Characters.Remove(c); _book.Items.RemoveAll(i => i.OwnerId == c.Id);
                foreach (var ch in _book.Chapters) { ch.CharacterIds.Remove(c.Id); if (ch.PovCharacterId == c.Id) ch.PovCharacterId = null; }
                break;
            case TimelineEvent t:
                if (!ConfirmDelete(t.Title)) return;
                _book.Timeline.Remove(t); _book.Items.RemoveAll(i => i.OwnerId == t.Id); break;
            case Plotline p:
                if (!ConfirmDelete(p.Name)) return;
                _book.Plotlines.Remove(p); _book.Items.RemoveAll(i => i.OwnerId == p.Id);
                foreach (var ch in _book.Chapters) ch.PlotlineIds.Remove(p.Id);
                foreach (var o in _book.Plotlines) o.Convergences.RemoveAll(x => x.OtherPlotlineId == p.Id);
                break;
        }
        _current = null;
        MarkDirty();
        BuildTree(select: next);
        if (next == null) { ShowCurrent(); UpdateMenus(); }
        _consistency?.Run();

        bool ConfirmDelete(string name) =>
            MessageBox.Show(this, $"Delete \"{name}\"?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void BookProperties_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        SelectNode(_book);
    }

    private void ScanMentions_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || _current is not Character c) { UpdateStatus("Select a character first."); return; }
        CommitCurrent();
        Cursor = Cursors.Wait;
        MentionsWindow dlg;
        try { dlg = new MentionsWindow(_book, c, ChapterText) { Owner = this }; }
        finally { Cursor = Cursors.Arrow; }
        if (dlg.ShowDialog() != true || dlg.ToLink.Count == 0) return;
        foreach (var ch in dlg.ToLink)
        {
            if (!ch.CharacterIds.Contains(c.Id)) ch.CharacterIds.Add(c.Id);
            SyncCharactersItem(ch);
        }
        MarkDirty();
        ShowCurrent();
        UpdateStatus($"Linked {c.Name} to {dlg.ToLink.Count} chapter(s).");
    }

    private void Consistency_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        CommitCurrent();
        if (_consistency is { IsLoaded: true }) { _consistency.Run(); _consistency.Activate(); return; }
        _consistency = new ConsistencyWindow(
            scanText => Consistency.Check(_book, scanText ? ChapterText : null),
            id => { if (FindById(id) is { } node) { SelectNode(node); Activate(); } }) { Owner = this };
        _consistency.Closed += (_, _) => _consistency = null;
        _consistency.Show();
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

    private bool _previewPrompts;

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        _previewPrompts = MnuPreview.IsChecked;
        UpdateStatus(_previewPrompts ? "AI prompts will be shown before sending." : "AI prompts are sent without preview.");
    }

    /// <summary>The prompt-preview gate: true to send. Every AI request passes through here.</summary>
    private bool ConfirmSend(AiRequest request)
    {
        if (!_previewPrompts) return true;
        var dlg = new PromptPreviewWindow(_ai, request) { Owner = this };
        return dlg.ShowDialog() == true && dlg.Send;
    }

    /// <summary>Binds a template, refuses anything over the size ceiling, previews if asked, sends.</summary>
    private async Task<AiResponse> SendTemplateAsync(string template, object values, CancellationToken ct, AiProviderType? provider = null)
    {
        var request = _ai.Prompts.Get(template).Bind(values, provider);
        var chars = request.System.Length + request.User.Length;
        if (chars > AiPrompts.MaxPromptChars)
            throw new AiException(AiErrorKind.ProviderError, provider ?? _ai.DefaultProvider,
                $"This request would send about {AiHub.EstimateTokens(request.System + request.User):N0} tokens, more than the {AiHub.EstimateTokens(new string('x', AiPrompts.MaxPromptChars)):N0} this app allows in one go. Summarize the chapters first so the prompt can use summaries instead of full text.");
        if (!ConfirmSend(request)) throw new OperationCanceledException();
        return await _ai.CompleteAsync(request, ct);
    }

    /// <summary>
    /// The chapter summary, in one request when the text fits and otherwise in parts: each part is
    /// summarized on its own and the part summaries are merged. Nothing is ever cut off silently.
    /// </summary>
    private async Task<AiResponse> SummarizeChapterAsync(Chapter ch, string text, CancellationToken ct)
    {
        const int partLimit = AiPrompts.MaxPromptChars - 4000;
        if (text.Length <= partLimit)
            return await SendTemplateAsync(AiPrompts.ChapterSummary, new { book = _book!.Title, chapter = ch.Title, text }, ct);

        var parts = TextChunker.Split(text, partLimit);
        var partSummaries = new List<string>();
        for (int i = 0; i < parts.Count; i++)
        {
            TxtAi.Text = $"Summarizing \"{ch.Title}\" part {i + 1} of {parts.Count}…";
            var r = await SendTemplateAsync(AiPrompts.ChunkSummary, new { book = _book!.Title, chapter = ch.Title, part = i + 1, parts = parts.Count, text = parts[i] }, ct);
            partSummaries.Add($"Part {i + 1}: {r.Text.Trim()}");
        }
        return await SendTemplateAsync(AiPrompts.MergeSummaries, new { book = _book!.Title, chapter = ch.Title, summaries = string.Join("\n\n", partSummaries) }, ct);
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
            var r = await SummarizeChapterAsync(ch, text, ct);
            var item = _book.SetSummary(ch, r.Text.Trim(), r.Provider.ToString(), r.Model, AiPrompts.ChapterSummary);
            MarkDirty();
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
        var dlg = new AiRunWindow(_ai, _book, ch.Id, ItemKind.Analysis, $"Analyze \"{ch.Title}\"", "Analysis", AiPrompts.ChapterAnalysis,
            "Each finished answer is saved under the chapter as an Analysis item with the date, provider and model, so runs can be compared later. The prompt is \"chapter-analysis\" in AI › Prompt Library.",
            provider => _ai.Prompts.Get(AiPrompts.ChapterAnalysis).Bind(new { book = _book.Title, section, chapter = ch.Title, context, text }, provider),
            ConfirmSend) { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        MarkDirty();
        BuildTree(select: dlg.Created[^1]);
        UpdateStatus($"{dlg.Created.Count} analysis item(s) added to \"{ch.Title}\".");
    }

    // ── Continuity, plot, style ───────────────────────────────────────────────

    /// <summary>The chapters an AI book-level action covers: the selected section's, or the whole book's.</summary>
    private (Guid OwnerId, string Scope, List<Chapter> Chapters) AiScope()
    {
        if (CurrentSection() is { } s) return (s.Id, s.Title, _book!.ChaptersOf(s).ToList());
        return (_book!.Id, "the whole book", _book.Chapters.ToList());
    }

    private string SummariesFor(IEnumerable<Chapter> chapters) =>
        string.Join("\n", chapters.Select(c => $"{(_book!.SectionOf(c) is { } s ? s.Title + " · " : "")}{_book.ChapterNumber(c)}. {c.Title}: {(_book.SummaryText(c).Length > 0 ? _book.SummaryText(c) : "(no summary yet)")}"));

    private bool WarnIfNoSummaries(IReadOnlyList<Chapter> chapters, string what)
    {
        int have = chapters.Count(c => _book!.SummaryText(c).Length > 0);
        if (have == 0)
        {
            MessageBox.Show(this, $"{what} works from chapter summaries and none of these {chapters.Count} chapters has one yet. Summarize them first (right-click a chapter › Summarize).", "AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (have < chapters.Count &&
            MessageBox.Show(this, $"{have} of {chapters.Count} chapters have summaries; the rest will appear as \"(no summary yet)\". Continue anyway?", "AI", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return false;
        return true;
    }

    private string CharacterFacts() => _book!.Characters.Count == 0 ? "(none recorded)" : string.Join("\n\n", _book.Characters.Select(c =>
        $"{c.Name}{(c.Aliases.Length > 0 ? " (also " + string.Join(", ", MentionFinder.NamesOf(c).Skip(1)) + ")" : "")} — {c.Role}" +
        (c.Description.Length > 0 ? $"\n  Description: {c.Description}" : "") + (c.Motivations.Length > 0 ? $"\n  Motivations: {c.Motivations}" : "") +
        (c.Actions.Length > 0 ? $"\n  Actions: {c.Actions}" : "") + (c.Arc.Length > 0 ? $"\n  Arc: {c.Arc}" : "")));

    private string TimelineFacts() => _book!.Timeline.Count == 0 ? "(no events recorded)" : string.Join("\n", _book.Timeline.OrderBy(e => e.Order).Select(e =>
        $"{e.Order}. {(e.When.Length > 0 ? e.When + " — " : "")}{e.Title}{(e.Description.Length > 0 ? ": " + e.Description : "")}" +
        (e.ChapterIds.Count > 0 ? $" [told in {string.Join(", ", e.ChapterIds.Select(id => _book.Chapters.FirstOrDefault(c => c.Id == id)).Where(c => c != null).Select(c => "ch. " + _book.ChapterNumber(c!)))}]" : "")));

    private void AiContinuity_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var (ownerId, scope, chapters) = AiScope();
        if (!WarnIfNoSummaries(chapters, "The continuity check")) return;
        var values = new { book = _book.Title, scope, characters = CharacterFacts(), timeline = TimelineFacts(), summaries = SummariesFor(chapters) };
        var dlg = new AiRunWindow(_ai, _book, ownerId, ItemKind.Analysis, $"Continuity check — {scope}", "Continuity check", AiPrompts.ContinuityCheck,
            "Checks the chapter summaries against the character records and the timeline for contradictions, with chapter references. Saved as an Analysis item on the section (or the book). Better summaries and fuller character records give better results.",
            provider => _ai.Prompts.Get(AiPrompts.ContinuityCheck).Bind(values, provider), ConfirmSend) { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        MarkDirty();
        BuildTree(select: dlg.Created[^1]);
    }

    private void AiPlot_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var (ownerId, scope, chapters) = AiScope();
        if (!WarnIfNoSummaries(chapters, "Plot analysis")) return;
        var plotlines = _book.Plotlines.Count == 0 ? "(none recorded yet)" : string.Join("\n", _book.Plotlines.Select(p => $"{p.Name} ({p.Status}){(p.Summary.Length > 0 ? ": " + p.Summary : "")}"));
        var values = new { book = _book.Title, scope, plotlines, summaries = SummariesFor(chapters) };
        var dlg = new AiRunWindow(_ai, _book, ownerId, ItemKind.Analysis, $"Plot analysis — {scope}", "Plot analysis", AiPrompts.PlotAnalysis,
            "Acts, a tension score per chapter, slow stretches, the plotlines it can see and suggested convergences — from the chapter summaries. Saved as an Analysis item on the section (or the book).",
            provider => _ai.Prompts.Get(AiPrompts.PlotAnalysis).Bind(values, provider), ConfirmSend) { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        MarkDirty();
        BuildTree(select: dlg.Created[^1]);
    }

    private void Style_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || CurrentChapter() is not { } ch) { UpdateStatus("Select a chapter first."); return; }
        CommitCurrent();
        var text = ChapterText(ch);
        var report = StyleMetrics.Analyze(text);
        var dlg = new StyleWindow(ch.Title, report, () =>
        {
            var stats = StyleMetrics.Describe(report);
            var run = new AiRunWindow(_ai, _book, ch.Id, ItemKind.Analysis, $"Style read — \"{ch.Title}\"", "Style read", AiPrompts.StyleRead,
                "The AI reads the chapter with the local statistics as leads and points at specific habits with quoted examples. Saved as an Analysis item under the chapter.",
                provider => _ai.Prompts.Get(AiPrompts.StyleRead).Bind(new { book = _book.Title, chapter = ch.Title, stats, text }, provider), ConfirmSend) { Owner = this };
            run.ShowDialog();
            if (run.Created.Count == 0) return;
            MarkDirty();
            BuildTree(select: run.Created[^1]);
        }, _ai.IsAvailable()) { Owner = this };
        dlg.ShowDialog();
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
        SyncChapterFromItem(item);
        MarkDirty();

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
            var r = await SendTemplateAsync(AiPrompts.CharactersInChapter, new { chapter = ch.Title, known_characters = known, text }, ct);
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
            SyncChapterFromItem(item);
            MarkDirty();
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
            var r = await SendTemplateAsync(AiPrompts.SectionOutline, new { book = _book.Title, section = s.Title, summaries = SectionSummaries(s) }, ct);
            var item = new Item { OwnerId = s.Id, Kind = ItemKind.Outline, Title = $"Outline · {DateTime.Now:yyyy-MM-dd}", Body = r.Text.Trim(), Provider = r.Provider.ToString(), Model = r.Model, PromptName = AiPrompts.SectionOutline };
            _book.Items.Add(item);
            MarkDirty();
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
            var r = await SendTemplateAsync(AiPrompts.SectionSummary, new { book = _book.Title, section = s.Title, summaries = SectionSummaries(s) }, ct);
            s.Summary = r.Text.Trim();
            MarkDirty();
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
            var r = await SendTemplateAsync(AiPrompts.CharacterProfile,
                new { book = _book.Title, synopsis = _book.Synopsis, known, summaries }, ct);
            c.Notes = (c.Notes.Length > 0 ? c.Notes + "\n\n" : "") + $"— AI suggestions ({DateTime.Now:yyyy-MM-dd}) —\n{r.Text.Trim()}";
            MarkDirty();
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
