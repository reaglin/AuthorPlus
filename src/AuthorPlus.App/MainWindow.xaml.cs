﻿﻿﻿﻿using System.Diagnostics;
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
public partial class MainWindow : Window, ISuggestionActions
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
            else if (LoadRecent().FirstOrDefault() is { } last) TryOpenFolder(last);   // always come back to the last book
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
            ItemKind.ChapterPlotlines    => "🧵",
            ItemKind.Suggestions         => "💡",
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
        Item i          => (i.Resolved ? "✓ " : "") + (i.Kind == ItemKind.Summary ? "Summary" : i.Title.Length > 0 ? i.Title : i.Kind.ToString()) + SuggestionCounts(i),
        Character c     => c.Name,
        TimelineEvent t => string.IsNullOrWhiteSpace(t.When) ? t.Title : $"{t.When} — {t.Title}",
        Plotline p      => p.Name,
        string s        => s,
        _               => node.ToString() ?? ""
    };

    private static string SuggestionCounts(Item i)
    {
        if (i.Kind != ItemKind.Suggestions || i.Suggestions.Count == 0) return "";
        int applied = i.Suggestions.Count(s => s.Status == SuggestionStatus.Applied), marked = i.Suggestions.Count(s => s.Status == SuggestionStatus.Marked),
            resolved = i.Suggestions.Count(s => s.Status == SuggestionStatus.Resolved), open = i.Suggestions.Count(s => s.Status == SuggestionStatus.Open);
        var parts = new List<string>();
        if (open > 0) parts.Add($"{open} open"); if (applied > 0) parts.Add($"{applied} applied"); if (marked > 0) parts.Add($"{marked} marked"); if (resolved > 0) parts.Add($"{resolved} resolved");
        return "  (" + string.Join(", ", parts) + ")";
    }

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
        foreach (var item in _book.ItemsOf(_book.Id)) root.Items.Add(ItemNode(item));
        foreach (var section in _book.Sections)
        {
            var sNode = Node(section, bold: true);
            foreach (var item in _book.ItemsOf(section.Id)) sNode.Items.Add(ItemNode(item));
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
        foreach (var item in _book!.ItemsOf(chapter.Id)) node.Items.Add(ItemNode(item));
        return node;
    }

    /// <summary>An item node with the items it owns beneath it (an analysis and its suggestions).</summary>
    private TreeViewItem ItemNode(Item item)
    {
        var node = Node(item);
        foreach (var child in _book!.ItemsOf(item.Id)) node.Items.Add(ItemNode(child));
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
            if (IdOf(item) is { } id) foreach (var it in _book!.ItemsOf(id)) child.Items.Add(ItemNode(it));
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
                Add("Find Plotlines, whole book (AI)…", AiFindPlotlines_Click, _ai.IsAvailable());
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
                Add("Find Plotlines (AI)…", AiFindPlotlines_Click, _ai.IsAvailable());
                Sep();
                Add("Move Up", MoveUp_Click); Add("Move Down", MoveDown_Click);
                Add("Delete Section…", DeleteItem_Click);
                break;
            case Chapter:
                Add("Summarize (AI)", AiSummarize_Click, _ai.IsAvailable());
                Add("Analyze… (AI)", AiAnalyze_Click, _ai.IsAvailable());
                var aspects = new MenuItem { Header = "Analyze one aspect (AI)", IsEnabled = _ai.IsAvailable() };
                foreach (var a in AiPrompts.AspectTemplates.Keys)
                {
                    var mi = new MenuItem { Header = a + "…", Tag = a };
                    mi.Click += AiAspect_Click;
                    aspects.Items.Add(mi);
                }
                m.Items.Add(aspects);
                Add("Chapter Characters", AiCharactersInChapter_Click);
                Add("Suggestions for a Passage… (AI)", AiChoosePassage_Click, _ai.IsAvailable());
                Add("Style Report…", Style_Click);
                Add("Marked Passages…", MarkedPassages_Click);
                Add("Add Notes", AddNotesItem_Click);
                Sep();
                Add("Insert Chapter from File After This…", InsertChapterFile_Click);
                Sep();
                Add("Move Up", MoveUp_Click); Add("Move Down", MoveDown_Click);
                Add("Delete Chapter…", DeleteItem_Click);
                break;
            case Item it:
                Add(it.Resolved ? "Reopen" : "Mark as Resolved", ResolveItem_Click);
                if (it.Kind == ItemKind.Suggestions) Add("Marked Passages of This Chapter…", MarkedPassages_Click);
                Sep();
                Add("Move Up", MoveUp_Click); Add("Move Down", MoveDown_Click);
                Add("Delete…", DeleteItem_Click);
                break;
            case string header:
                Add(header == CharactersHeader ? "Add Character" : header == TimelineHeader ? "Add Timeline Event" : "Add Plotline",
                    header == CharactersHeader ? AddCharacter_Click : header == TimelineHeader ? AddEvent_Click : AddPlotline_Click);
                if (header == PlotlinesHeader) Add("Find Plotlines, whole book (AI)…", AiFindPlotlines_Click, _ai.IsAvailable());
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
                    ItemKind.Summary => "Summary", ItemKind.Analysis => "Analysis", ItemKind.CharactersInChapter => "Chapter Characters",
                    ItemKind.ChapterPlotlines => "Chapter Plotlines", ItemKind.Suggestions => "Suggestions", ItemKind.Outline => "Outline", _ => "Notes"
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
                        KindChooser(p),
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
                    CharactersHeader => new CharactersView(_book, SelectNode, MarkDirty),
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
        UpdateChapterLinks(CurrentChapter());
        _loading = false;
    }

    private string OwnerLabel(Item it)
    {
        if (_book == null) return "";
        if (it.OwnerId == _book.Id) return "the book";
        if (_book.Sections.FirstOrDefault(s => s.Id == it.OwnerId) is { } s) return s.Title;
        if (_book.Chapters.FirstOrDefault(c => c.Id == it.OwnerId) is { } c) return $"chapter {_book.ChapterNumber(c)}, {c.Title}";
        if (_book.Items.FirstOrDefault(i => i.Id == it.OwnerId) is { } parent) return $"the analysis \"{parent.Title}\"";
        if (_book.Characters.FirstOrDefault(x => x.Id == it.OwnerId) is { } ch) return ch.Name;
        if (_book.Timeline.FirstOrDefault(x => x.Id == it.OwnerId) is { } ev) return ev.Title;
        if (_book.Plotlines.FirstOrDefault(x => x.Id == it.OwnerId) is { } pl) return pl.Name;
        return "?";
    }

    private UIElement KindChooser(Plotline p)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "Kind:", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var box = new ComboBox { ItemsSource = Enum.GetValues<PlotlineKind>(), SelectedItem = p.Kind, Width = 130 };
        box.SelectionChanged += (_, _) => { if (!_loading && box.SelectedItem is PlotlineKind k) { p.Kind = k; MarkDirty(); } };
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock { Text = "primary = the spine the book turns on · secondary = a thread beside it · chapter = it opens and closes here · subplot = a side story · extra = one you are watching", Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
        return panel;
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
        var who = new Button { Content = "Chapter Characters", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
        who.Click += AiCharactersInChapter_Click;
        var passage = new Button { Content = "Suggestions… (AI)", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0),
                                   ToolTip = "Rewrites of one passage: select the text you mean first, or click with nothing selected to pick paragraphs" };
        passage.Click += AiPassageSuggestions_Click;
        bar.Items.Add(summarize); bar.Items.Add(analyze); bar.Items.Add(who); bar.Items.Add(passage);
        Grid.SetRow(bar, 0);
        grid.Children.Add(bar);

        _findPanel = BuildFindPanel();
        Grid.SetRow(_findPanel, 1);
        grid.Children.Add(_findPanel);

        // Who is in the chapter and which plotlines run through it are shown in the status bar and
        // edited on the Characters and Plotlines pages — the editor is the manuscript, nothing else.

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
        Grid.SetRow(_rtb, 2);
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

    // ── The open chapter's links, in the status bar ───────────────────────────

    /// <summary>
    /// The status bar carries what the chapter editor used to: how many characters are in the open
    /// chapter and how many threads run through it, each a link to the page where it is edited.
    /// </summary>
    private void UpdateChapterLinks(Chapter? ch)
    {
        if (_book == null || ch == null)
        {
            TxtChapChars.Text = "";
            TxtChapPlots.Text = "";
            return;
        }
        var people = _book.Characters.Where(c => ch.CharacterIds.Contains(c.Id)).ToList();
        var pov = _book.Characters.FirstOrDefault(c => c.Id == ch.PovCharacterId);
        TxtChapChars.Text = $"Characters: {people.Count}{(pov != null ? $" · POV {pov.Name}" : "")} ▸";
        TxtChapChars.ToolTip = people.Count == 0
            ? "Nobody is linked to this chapter yet. Click to open Characters and mark the chart."
            : string.Join(", ", people.Select(c => c.Name + (c.Id == ch.PovCharacterId ? " (POV)" : ""))) + "  —  click to open Characters";

        var threads = _book.Plotlines.Where(p => ch.PlotlineIds.Contains(p.Id) || p.ChapterIds.Contains(ch.Id)).ToList();
        TxtChapPlots.Text = $"Plotlines: {threads.Count} ▸";
        TxtChapPlots.ToolTip = threads.Count == 0
            ? "No thread is linked to this chapter yet. Click to open Plotlines and mark the chart."
            : string.Join(", ", threads.Select(p => $"{p.Name} ({p.Kind.ToString().ToLowerInvariant()})")) + "  —  click to open Plotlines";
    }

    private void StatusCharacters_Click(object sender, MouseButtonEventArgs e) { if (_book != null) SelectNode(CharactersHeader); }
    private void StatusPlotlines_Click(object sender, MouseButtonEventArgs e) { if (_book != null) SelectNode(PlotlinesHeader); }

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
        meta.Text = (it.Resolved ? $"✓ Resolved {it.ResolvedUtc?.ToLocalTime():yyyy-MM-dd} (right-click › Reopen) · " : "") +
                    (it.IsAiMade ? $"Written by {it.Provider} ({it.Model}) with the \"{it.PromptName}\" prompt · " : "") +
                    $"created {it.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}, last changed {it.ModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
        DockPanel.SetDock(meta, Dock.Top);
        panel.Children.Add(meta);

        if (it.Kind == ItemKind.CharactersInChapter)
        {
            panel.Children.Add(BuildCharactersInChapterEditor(it));
            return panel;
        }
        if (it.Kind == ItemKind.ChapterPlotlines)
        {
            panel.Children.Add(BuildChapterPlotlinesEditor(it));
            return panel;
        }
        if (it.Kind == ItemKind.Suggestions)
        {
            panel.Children.Add(SuggestionsEditor.Build(it, MarkedPassagesWindow.ChapterOf(_book!, it), this, _ai.IsAvailable()));
            return panel;
        }
        if (it.Kind == ItemKind.Analysis && AnalysisSections.Parse(it.Body).Any(x => x.Heading.Length > 0))
        {
            panel.Children.Add(BuildAnalysisEditor(it));
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

    /// <summary>
    /// Who is in the chapter, as a table to read. Who appears where is set on the Characters page
    /// (its chart), and a character's own page holds everything about them; a row here opens it.
    /// </summary>
    private UIElement BuildCharactersInChapterEditor(Item it)
    {
        var chapter = _book!.Chapters.FirstOrDefault(c => c.Id == it.OwnerId);
        var ids = it.CharacterIds.Count > 0 ? it.CharacterIds : chapter?.CharacterIds ?? new List<Guid>();
        var povId = it.PovCharacterId ?? chapter?.PovCharacterId;
        var rows = _book.Characters.Where(c => ids.Contains(c.Id)).ToList();

        var root = new DockPanel();
        var head = new TextBlock
        {
            Text = "Who is in this chapter, from the analysis. Click a name to open the character — that page holds their role, motivations and arc. Who appears in which chapter is set on the Characters page, on the chart under the cast.",
            Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var table = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var w in new[] { 200d, 150d, 190d, 0d })
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : new GridLength(1, GridUnitType.Star) });
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in rows) table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headers = new[] { "Character", "Role", "In this chapter", "Also called" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = Palette.HeaderCell(headers[c]);
            Grid.SetRow(cell, 0); Grid.SetColumn(cell, c);
            table.Children.Add(cell);
        }

        for (int r = 0; r < rows.Count; r++)
        {
            var person = rows[r];
            bool alt = r % 2 == 1;
            int appears = _book.Chapters.Count(c => c.CharacterIds.Contains(person.Id));
            var first = _book.Chapters.FirstOrDefault(c => c.CharacterIds.Contains(person.Id));
            bool introduced = chapter != null && first != null && first.Id == chapter.Id;
            var pov = povId == person.Id;

            var (state, tint, why) =
                pov        ? ("Point of view", Palette.PovBg, "The chapter is told from this character's point of view.")
                : introduced ? ("Introduced here", Palette.IntroducedBg, "This is the first chapter that lists them.")
                             : ($"Also in {appears - 1} other chapter{(appears == 2 ? "" : "s")}", Palette.ContinuedBg, "Open the character to see every chapter they are in.");

            var open = Palette.LinkText(person.Name, () => SelectNode(person), "Open this character");
            open.FontWeight = FontWeights.SemiBold;
            var cells = new UIElement[]
            {
                Palette.Cell(open, alt),
                Palette.Cell(person.Role.Trim().Length > 0 ? person.Role.Trim() : "—", alt),
                Palette.Cell(Palette.Chip(state, tint, why), alt),
                Palette.Cell(string.Join(", ", MentionFinder.NamesOf(person).Skip(1)) is { Length: > 0 } also ? also : "—", alt)
            };
            for (int c = 0; c < cells.Length; c++)
            {
                Grid.SetRow(cells[c], r + 1); Grid.SetColumn(cells[c], c);
                table.Children.Add(cells[c]);
            }
        }

        if (rows.Count == 0)
            root.Children.Add(WithTopDock(new TextBlock { Text = "No characters are linked to this chapter yet. Run Analyze… (AI) on the chapter, or open Characters and mark the chapter on the chart.", Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 10) }));
        else
            root.Children.Add(WithTopDock(new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(1, 1, 0, 0), Child = table }));

        var label = new TextBlock { Text = "What each does in this chapter (the AI's list, editable)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
        DockPanel.SetDock(label, Dock.Top);
        root.Children.Add(label);
        var body = new TextBox { Text = it.Body, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8), MinHeight = 120 };
        body.TextChanged += (_, _) => { if (!_loading) { it.Body = body.Text; Touch(it); } };
        root.Children.Add(body);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static UIElement WithTopDock(UIElement element) { DockPanel.SetDock(element, Dock.Top); return element; }

    /// <summary>The analysis as one card per aspect, each with a Suggestions… button that asks for rewrites for that aspect alone.</summary>
    private UIElement BuildAnalysisEditor(Item it)
    {
        var chapter = _book!.Chapters.FirstOrDefault(c => c.Id == it.OwnerId);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "What to do with this: each point is one thing you could act on. Click Suggestions… beside a point to get rewrites of the actual passage, which you can apply, mark for your own rewrite, " +
                   "or send back with what you were trying to convey. Go deeper… re-reads the chapter for that one aspect alone.",
            Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });
        var existing = _book.ItemsOf(it.Id).Where(s => s.Kind == ItemKind.Suggestions).ToList();
        foreach (var section in AnalysisSections.Parse(it.Body))
        {
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            var title = new TextBlock { Text = section.Heading.Length > 0 ? section.Heading : "Overview", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(title);
            if (section.Heading.Length > 0 && chapter != null && !section.Heading.StartsWith("Three changes", StringComparison.OrdinalIgnoreCase))
            {
                var deeper = new Button { Content = "Go deeper…", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(10, 0, 0, 0), IsEnabled = _ai.IsAvailable(),
                                          ToolTip = $"Read the whole chapter again for {section.Heading} alone, with a prompt written for it" };
                var aspectName = section.Heading;
                deeper.Click += (_, _) => AnalyzeAspect(chapter, aspectName);
                titleRow.Children.Add(deeper);
            }
            var points = new StackPanel { Margin = new Thickness(4, 2, 0, 6) };
            int n = 0;
            foreach (var point in section.Points)
            {
                n++;
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var body = new TextBox { Text = $"{n}. {point}", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(4, 2, 8, 2), FontSize = 14, VerticalAlignment = VerticalAlignment.Top };
                if (section.Heading.Length > 0 && chapter != null)
                {
                    var tag = $"{section.Heading} · {n}";
                    var done = existing.Count(sg => sg.Title.Contains(tag, StringComparison.OrdinalIgnoreCase));
                    var btn = new Button { Content = done == 0 ? "Suggestions…" : $"Suggestions… ({done})", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top,
                                           IsEnabled = _ai.IsAvailable(), ToolTip = "Ask the AI for concrete rewrites that address this one point; saved as a Suggestions item under this analysis" };
                    var aspect = section.Heading; var finding = point; var index = n;
                    btn.Click += (_, _) => RunSuggestions(it, chapter, aspect, index, finding);
                    DockPanel.SetDock(btn, Dock.Right);
                    row.Children.Add(btn);
                }
                row.Children.Add(body);
                points.Children.Add(row);
            }
            stack.Children.Add(new Expander { Header = titleRow, Content = points, IsExpanded = true, Margin = new Thickness(0, 0, 0, 6) });
        }
        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void RunSuggestions(Item analysis, Chapter chapter, string aspect, int index, string finding)
    {
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var text = ChapterText(chapter);
        var dlg = new AiRunWindow(_ai, _book!, analysis.Id, ItemKind.Suggestions, $"Suggestions — {aspect} · {index}", $"Suggestions · {aspect} · {index}", AiPrompts.AnalysisSuggestions,
            $"Concrete rewrites for this one point ({aspect}, point {index}): the passage, a rewritten version, and why. Saved under the analysis as a Suggestions item.\n\nThe point: {finding}",
            provider => _ai.Prompts.Get(AiPrompts.AnalysisSuggestions).Bind(new { book = _book!.Title, chapter = chapter.Title, aspect = $"{aspect} — point {index}", finding, text, intent = IntentOf(chapter) }, provider),
            ConfirmSend, nextLabel: "Next: review the suggestions →") { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        foreach (var created in dlg.Created) created.Suggestions = SuggestionParser.Parse(created.Body);
        MarkDirty();
        BuildTree(select: dlg.Created[^1]);
    }

    // ── Suggestions for a passage the author chose ────────────────────────────

    /// <summary>
    /// "Suggestions for selection": the passage is whatever is selected in the chapter editor. With
    /// nothing selected the paragraph picker opens instead, so the action never dead-ends.
    /// </summary>
    private void AiPassageSuggestions_Click(object sender, RoutedEventArgs e)
    {
        var ch = CurrentChapter();
        if (_book == null || ch == null) { UpdateStatus("Open a chapter first — suggestions work on a passage of its text."); return; }
        var selected = ReferenceEquals(_current, ch) && _rtb != null ? _rtb.Selection.Text.Trim() : "";
        if (selected.Length == 0) { AiChoosePassage_Click(sender, e); return; }
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        RunPassageSuggestions(ch, selected);
    }

    /// <summary>"Choose a passage": the chapter paragraph by paragraph, with Select on each.</summary>
    private void AiChoosePassage_Click(object sender, RoutedEventArgs e)
    {
        var ch = CurrentChapter();
        if (_book == null || ch == null) { UpdateStatus("Open a chapter first — suggestions work on a passage of its text."); return; }
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var paragraphs = PassagePickerWindow.Paragraphs(ChapterText(ch));
        if (paragraphs.Count == 0) { MessageBox.Show(this, "This chapter has no text yet.", "Choose a passage", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var picker = new PassagePickerWindow(ch, paragraphs) { Owner = this };
        if (picker.ShowDialog() != true || picker.Passage.Length == 0) return;
        RunPassageSuggestions(ch, picker.Passage);
    }

    /// <summary>Asks for rewrites of one passage, guided by what the author says it is for. Saved as a Suggestions item on the chapter.</summary>
    private void RunPassageSuggestions(Chapter ch, string passage)
    {
        if (_book == null) return;
        if (passage.Length < 20)
        {
            MessageBox.Show(this, "That is too short to rewrite usefully. Choose at least a full sentence.", "Suggestions", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (passage.Length > 8000 &&
            MessageBox.Show(this, $"That passage is {passage.Length:N0} characters — about {passage.Length / 5:N0} words. Suggestions work best on a few paragraphs at a time; a long stretch tends to come back with vaguer rewrites.\n\nAsk anyway?",
                            "Suggestions", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var form = new SuggestionRequestWindow(ch, passage, "the passage you chose") { Owner = this };
        if (form.ShowDialog() != true) return;

        var text = ChapterText(ch);
        var values = new
        {
            book = _book.Title, chapter = ch.Title, passage,
            intent = form.Intent.Length > 0 ? form.Intent : "(not stated)",
            request = form.Request.Length > 0 ? form.Request : "(not stated)",
            chapter_intent = IntentOf(ch), text
        };
        var preview = passage.Replace("\n", " ").Replace("\r", " ");
        if (preview.Length > 60) preview = preview[..60].TrimEnd() + "…";
        var dlg = new AiRunWindow(_ai, _book, ch.Id, ItemKind.Suggestions, $"Suggestions — \"{preview}\"", "Suggestions · Chosen passage", AiPrompts.PassageSuggestions,
            "Rewrites of the passage you chose" + (form.Intent.Length > 0 ? ", aimed at " + form.Intent : "") +
            ". They are saved as a Suggestions item on this chapter, where you can apply, mark or ask again for each one.",
            provider => _ai.Prompts.Get(AiPrompts.PassageSuggestions).Bind(values, provider), ConfirmSend,
            nextLabel: "Next: review the suggestions →") { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        foreach (var created in dlg.Created) created.Suggestions = SuggestionParser.Parse(created.Body);
        MarkDirty();
        BuildTree(select: dlg.Created[^1]);
    }

    // ── Applying, marking, resolving suggestions (ISuggestionActions) ─────────

    public bool Apply(Chapter chapter, SuggestionEntry entry, string replacement)
    {
        if (_book == null) return false;
        if (ReferenceEquals(_current, chapter) && _rtb != null)
        {
            var hit = PassageInDocument.Locate(_rtb.Document, entry.Original);
            if (hit == null) return false;
            new TextRange(hit.Value.Start, hit.Value.End).Text = replacement;
            MarkDirty();
            UpdateWordCount(chapter);
            return true;
        }
        var xaml = _store.LoadChapterBody(_book, chapter);
        if (string.IsNullOrEmpty(xaml)) return false;
        FlowDocument doc;
        try { doc = (FlowDocument)XamlReader.Parse(xaml); } catch { return false; }
        var span = PassageInDocument.Locate(doc, entry.Original);
        if (span == null) return false;
        new TextRange(span.Value.Start, span.Value.End).Text = replacement;
        _store.SaveChapterBody(_book, chapter, XamlWriter.Save(doc), WordCounter.Count(PlainText(doc)));
        MarkDirty();
        RefreshTreeTexts();
        return true;
    }

    public bool GoTo(Chapter chapter, SuggestionEntry entry)
    {
        if (_book == null) return false;
        if (!ReferenceEquals(_current, chapter)) SelectNode(chapter);
        if (_rtb == null) return false;
        var hit = PassageInDocument.Locate(_rtb.Document, entry.Original);
        if (hit == null) return false;
        _rtb.Selection.Select(hit.Value.Start, hit.Value.End);
        _rtb.Focus();
        var rect = hit.Value.Start.GetCharacterRect(LogicalDirection.Forward);
        _rtb.ScrollToVerticalOffset(_rtb.VerticalOffset + rect.Top - _rtb.ActualHeight / 3);
        return true;
    }

    public void Changed(Item item)
    {
        MarkDirty();
        RefreshTreeTexts();
    }

    private void ResolveItem_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || _current is not Item it) return;
        it.Resolved = !it.Resolved;
        it.ResolvedUtc = it.Resolved ? DateTime.UtcNow : null;
        MarkDirty();
        RefreshTreeTexts();
        ShowCurrent();
    }

    private void MarkedPassages_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        CommitCurrent();
        var chapter = CurrentChapter() ?? (_current is Item it ? MarkedPassagesWindow.ChapterOf(_book, it) : null);
        new MarkedPassagesWindow(_book, chapter, this) { Owner = this }.ShowDialog();
        RefreshTreeTexts();
        if (_current is Item) ShowCurrent();
    }

    /// <summary>
    /// The chapter's plotlines as a table to read, not a form to fill in: what each thread is, how
    /// much of the book it carries, whether it starts here or was already running, and what it is
    /// about. Every row opens the plotline, which is where it is edited.
    /// </summary>
    private UIElement BuildChapterPlotlinesEditor(Item it)
    {
        var chapter = _book!.Chapters.FirstOrDefault(c => c.Id == it.OwnerId);
        var ids = it.PlotlineIds.Count > 0 ? it.PlotlineIds : chapter?.PlotlineIds ?? new List<Guid>();
        var rows = _book.Plotlines.Where(p => ids.Contains(p.Id)).ToList();

        var root = new DockPanel();
        var head = new TextBlock
        {
            Text = "The threads running through this chapter, from the analysis. Everything here is edited on the Plotlines page — click a name to open it; the chart there says which chapters each thread runs through.",
            Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var table = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var w in new[] { 200d, 110d, 190d, 0d })
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w) : new GridLength(1, GridUnitType.Star) });
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in rows) table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headers = new[] { "Plotline", "Type", "In this chapter", "Description" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = Palette.HeaderCell(headers[c]);
            Grid.SetRow(cell, 0); Grid.SetColumn(cell, c);
            table.Children.Add(cell);
        }

        for (int r = 0; r < rows.Count; r++)
        {
            var p = rows[r];
            bool alt = r % 2 == 1;
            var runs = _book.Chapters.Where(c => p.ChapterIds.Contains(c.Id)).ToList();
            var here = chapter != null && p.ChapterIds.Contains(chapter.Id);
            var before = chapter == null ? null : runs.LastOrDefault(c => _book.Chapters.IndexOf(c) < _book.Chapters.IndexOf(chapter));

            var (state, tint, why) =
                !here                  ? ("Named here, not linked", Palette.UnlinkedBg, "The analysis named this thread for the chapter but the plotline does not list the chapter — link it on the Plotlines page.")
                : before == null       ? ("Introduced here", Palette.IntroducedBg, "This is the first chapter the thread runs through.")
                                       : ($"Continued from ch. {_book.ChapterNumber(before)}", Palette.ContinuedBg, $"Last seen in {_book.ChapterNumber(before)}. {before.Title}");

            var open = Palette.LinkText(p.Name, () => SelectNode(p), "Open this plotline");
            open.FontWeight = FontWeights.SemiBold;
            var cells = new UIElement[]
            {
                Palette.Cell(open, alt),
                Palette.Cell(Palette.Chip(p.Kind.ToString().ToLowerInvariant(), Palette.KindBg(p.Kind), KindMeaning(p.Kind)), alt),
                Palette.Cell(Palette.Chip(state, tint, why), alt),
                Palette.Cell(p.Summary.Trim().Length > 0 ? p.Summary.Trim() : "— no description yet; write one on the Plotlines page —", alt)
            };
            for (int c = 0; c < cells.Length; c++)
            {
                Grid.SetRow(cells[c], r + 1); Grid.SetColumn(cells[c], c);
                table.Children.Add(cells[c]);
            }
        }

        var tableHost = new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(1, 1, 0, 0), Child = table };
        if (rows.Count == 0)
        {
            DockPanel.SetDock(tableHost, Dock.Top);
            root.Children.Add(new TextBlock { Text = "No plotlines are linked to this chapter yet. Run Analyze… (AI) on the chapter, or open Plotlines and mark the chapter on the chart.", Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 10) });
        }
        else
        {
            DockPanel.SetDock(tableHost, Dock.Top);
            root.Children.Add(tableHost);
        }

        var label = new TextBlock { Text = "What happens in each thread here (the AI's notes, editable)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) };
        DockPanel.SetDock(label, Dock.Top);
        root.Children.Add(label);
        var body = new TextBox { Text = it.Body, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8), MinHeight = 120 };
        body.TextChanged += (_, _) => { if (!_loading) { it.Body = body.Text; Touch(it); } };
        root.Children.Add(body);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static string KindMeaning(PlotlineKind kind) => kind switch
    {
        PlotlineKind.Primary   => "A spine the whole book turns on.",
        PlotlineKind.Secondary => "A thread running beside the spine, across chapters.",
        PlotlineKind.Chapter   => "Opens and closes inside this one chapter.",
        PlotlineKind.Subplot   => "A side story of its own.",
        _                      => "Something you are keeping an eye on; it carries no weight yet."
    };

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
                next = FindById(it.OwnerId) ?? (object)_book;
                _book.RemoveItemTree(it);
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

    private async void AiAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || CurrentChapter() is not { } ch) { UpdateStatus("Select a chapter first."); return; }
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var text = ChapterText(ch).Trim();
        var section = _book.SectionOf(ch)?.Title ?? "(none)";
        var context = EarlierSummaries(ch);
        var dlg = new AiRunWindow(_ai, _book, ch.Id, ItemKind.Analysis, $"Analyze \"{ch.Title}\"", "Analysis", AiPrompts.ChapterAnalysis,
            "The editor's notes on eight aspects — Pacing, Tension, Stakes, Point of view and voice, Dialogue, Continuity with the earlier chapters, Prose habits, and the Three changes that would help most — " +
            "each quoting the text. Saved under the chapter as an Analysis item (date, provider, model); open it to see one card per aspect, each with a Suggestions… button for concrete rewrites. " +
            "When the run finishes, a second short pass lists the chapter's characters and plotlines: known ones are linked, new ones are offered for adding. " +
            "The prompt is \"chapter-analysis\" in AI › Prompt Library.",
            provider => _ai.Prompts.Get(AiPrompts.ChapterAnalysis).Bind(new { book = _book.Title, section, chapter = ch.Title, context, text }, provider),
            ConfirmSend, nextLabel: "Next: read the analysis and ask for suggestions →") { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        MarkDirty();
        BuildTree(select: dlg.Created[^1]);
        UpdateStatus($"{dlg.Created.Count} analysis item(s) added to \"{ch.Title}\".");
        await ExtractEntitiesAsync(ch, text);
    }

    /// <summary>
    /// After an analysis: one structured pass names the chapter's characters and plotlines.
    /// Known ones are linked to the chapter; new ones are offered in a checklist and, if kept,
    /// added to the book. The Chapter Characters and Chapter Plotlines items are written.
    /// </summary>
    private async Task ExtractEntitiesAsync(Chapter ch, string text)
    {
        if (_book == null || !_ai.IsAvailable() || text.Trim().Length < 200) return;
        var knownCharacters = _book.Characters.Count == 0 ? "(none recorded yet)"
            : string.Join("\n", _book.Characters.Select(c => $"{c.Name}{(c.Aliases.Length > 0 ? " — " + string.Join(", ", MentionFinder.NamesOf(c).Skip(1)) : "")}"));
        var knownPlotlines = _book.Plotlines.Count == 0 ? "(none recorded yet)" : string.Join("\n", _book.Plotlines.Select(p => p.Name));

        ChapterExtract extract;
        try
        {
            TxtAi.Text = $"Listing the characters and plotlines of \"{ch.Title}\"…";
            Cursor = Cursors.AppStarting;
            var r = await SendTemplateAsync(AiPrompts.ChapterExtract, new { chapter = ch.Title, known_characters = knownCharacters, known_plotlines = knownPlotlines, text }, _aiCts?.Token ?? CancellationToken.None);
            extract = ChapterExtract.Parse(r.Text);
            TxtAi.Text = $"AI: done in {r.Elapsed.TotalSeconds:0.0}s" + (r.EstimatedCostUsd is { } cost ? $" · est. ${cost:0.0000}" : "");
        }
        catch (OperationCanceledException) { TxtAi.Text = "AI: cancelled."; return; }
        catch (AiException ex) { TxtAi.Text = "AI: the character/plotline pass failed."; MessageBox.Show(this, ex.Message, "AI", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        finally { Cursor = Cursors.Arrow; }

        Character? Known(string name) => _book.Characters.FirstOrDefault(c => MentionFinder.NamesOf(c).Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)));
        Plotline? KnownPlot(string name) => _book.Plotlines.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        var newCharacters = extract.Characters.Where(e => Known(e.Name) == null).ToList();
        var newPlotlines  = extract.Plotlines.Where(e => KnownPlot(e.Name) == null).ToList();
        if (newCharacters.Count > 0 || newPlotlines.Count > 0)
        {
            var dlg = new NewEntitiesWindow(ch.Title, newCharacters, newPlotlines) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                foreach (var e in dlg.ChosenCharacters) _book.Characters.Add(new Character { Name = e.Name, Notes = e.Note.Length > 0 ? $"— from the analysis of \"{ch.Title}\" —\n{e.Note}" : "" });
                foreach (var e in dlg.ChosenPlotlines) _book.Plotlines.Add(new Plotline { Name = e.Name, Status = PlotlineStatus.Active, Summary = e.Note, Kind = KindOf(e) });
            }
        }

        // Chapter Characters item + chapter links
        var charItem = _book.ItemsOf(ch.Id).FirstOrDefault(i => i.Kind == ItemKind.CharactersInChapter)
                       ?? new Item { OwnerId = ch.Id, Kind = ItemKind.CharactersInChapter, Title = "Chapter Characters" };
        if (!_book.Items.Contains(charItem)) _book.Items.Add(charItem);
        var lines = new List<string>();
        foreach (var e in extract.Characters)
        {
            var c = Known(e.Name);
            lines.Add($"{e.Name}{(e.IsPov ? " (POV)" : "")}{(e.IsNew ? " (introduced)" : "")}{(e.Note.Length > 0 ? " — " + e.Note : "")}");
            if (c == null) continue;
            if (!charItem.CharacterIds.Contains(c.Id)) charItem.CharacterIds.Add(c.Id);
            if (e.IsPov) charItem.PovCharacterId = c.Id;
        }
        charItem.Body = string.Join("\n", lines);
        charItem.Provider = _ai.DefaultProvider.ToString(); charItem.PromptName = AiPrompts.ChapterExtract; charItem.ModifiedUtc = DateTime.UtcNow;
        SyncChapterFromItem(charItem);

        // Chapter Plotlines item + links on both sides
        var plotItem = _book.ItemsOf(ch.Id).FirstOrDefault(i => i.Kind == ItemKind.ChapterPlotlines)
                       ?? new Item { OwnerId = ch.Id, Kind = ItemKind.ChapterPlotlines, Title = "Chapter Plotlines" };
        if (!_book.Items.Contains(plotItem)) _book.Items.Add(plotItem);
        var plotLines = new List<string>();
        foreach (var e in extract.Plotlines)
        {
            var p = KnownPlot(e.Name);
            plotLines.Add($"{e.Name}{(e.Kind.Length > 0 ? " [" + e.Kind + "]" : "")}{(e.IsNew ? " (introduced here)" : "")}{(e.Note.Length > 0 ? " — " + e.Note : "")}");
            if (p == null) continue;
            if (!plotItem.PlotlineIds.Contains(p.Id)) plotItem.PlotlineIds.Add(p.Id);
            if (!ch.PlotlineIds.Contains(p.Id)) ch.PlotlineIds.Add(p.Id);
            if (!p.ChapterIds.Contains(ch.Id)) p.ChapterIds.Add(ch.Id);
        }
        plotItem.Body = string.Join("\n", plotLines);
        plotItem.Provider = _ai.DefaultProvider.ToString(); plotItem.PromptName = AiPrompts.ChapterExtract; plotItem.ModifiedUtc = DateTime.UtcNow;

        MarkDirty();
        BuildTree(select: charItem);
        UpdateStatus($"\"{ch.Title}\": {extract.Characters.Count} characters and {extract.Plotlines.Count} plotlines listed; {newCharacters.Count + newPlotlines.Count} were new.");
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

    /// <summary>What the author says this chapter is for: its Notes field, used as intent in the editorial prompts.</summary>
    private static string IntentOf(Chapter ch) => ch.Notes.Trim().Length > 0 ? ch.Notes.Trim() : "(the author has not said; judge from the text)";

    private void AiAspect_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || CurrentChapter() is not { } ch) { UpdateStatus("Select a chapter first."); return; }
        if (sender is MenuItem { Tag: string aspect }) AnalyzeAspect(ch, aspect);
    }

    /// <summary>A deep read of one aspect of one chapter, with the prompt written for that aspect.</summary>
    private void AnalyzeAspect(Chapter ch, string aspect)
    {
        if (_book == null) return;
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var text = ChapterText(ch);
        var section = _book.SectionOf(ch)?.Title ?? "(none)";
        var context = EarlierSummaries(ch);
        var template = AiPrompts.AspectTemplate(aspect);
        var values = new { book = _book.Title, section, chapter = ch.Title, aspect, context, intent = IntentOf(ch), text };
        var dlg = new AiRunWindow(_ai, _book, ch.Id, ItemKind.Analysis, $"{aspect} — \"{ch.Title}\"", $"Analysis · {aspect}", template,
            $"A deeper read of this chapter for {aspect.ToLowerInvariant()} alone, with a prompt written for it (\"{template}\" in AI › Prompt Library). " +
            "The points come back the same way, so each one has its own Suggestions… button. What you put in the chapter's Notes field is passed on as what you are aiming for.",
            provider => _ai.Prompts.Get(template).Bind(values, provider), ConfirmSend,
            nextLabel: "Next: read it and ask for suggestions →") { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        MarkDirty();
        BuildTree(select: dlg.Created[^1]);
        UpdateStatus($"{aspect} analysis added to \"{ch.Title}\".");
    }

    /// <summary>The aspect a Suggestions or Analysis item is about, taken from its title ("Suggestions · Pacing · 1").</summary>
    private static string AspectOf(Item item)
    {
        var parts = item.Title.Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? parts[1] : "this passage";
    }

    /// <summary>"Ask again": the author says what the passage is for, and the AI tries fresh rewrites aimed at that.</summary>
    public void AskAgain(Chapter chapter, Item item, SuggestionEntry entry)
    {
        if (_book == null) return;
        if (!EnsureAiOrExplain()) return;
        var aspect = AspectOf(item);
        var form = new SuggestionRequestWindow(chapter, entry, aspect) { Owner = this };
        if (form.ShowDialog() != true) return;

        CommitCurrent();
        var text = ChapterText(chapter);
        var values = new
        {
            book = _book.Title, chapter = chapter.Title, aspect,
            original = entry.Original, previous_rewrite = entry.Rewrite, previous_why = entry.Why,
            intent = form.Intent.Length > 0 ? form.Intent : "(not stated)", request = form.Request.Length > 0 ? form.Request : "(not stated)", text
        };
        var run = new AiRunWindow(_ai, _book, item.OwnerId, ItemKind.Suggestions, $"Ask again — {aspect}", "Suggestions", AiPrompts.SuggestionRefine,
            $"Fresh rewrites of the same passage, aimed at what you said you are conveying{(form.Intent.Length > 0 ? ": " + form.Intent : "")}. " +
            "They are added to this same Suggestions item, under the one you asked about, so you can compare them.",
            provider => _ai.Prompts.Get(AiPrompts.SuggestionRefine).Bind(values, provider), ConfirmSend,
            nextLabel: "Next: compare the new suggestions →",
            handleResponse: r => AppendRefinements(item, entry, r, form.Intent, form.Request)) { Owner = this };
        run.ShowDialog();
        Refresh(item);
    }

    /// <summary>Adds the rewrites from an "ask again" run to the item, just after the suggestion they answer.</summary>
    private void AppendRefinements(Item item, SuggestionEntry source, AiResponse response, string intent, string request)
    {
        var fresh = SuggestionParser.Parse(response.Text);
        if (fresh.Count == 0)
        {
            MessageBox.Show(this, "That reply did not come back in the suggestion format, so there is nothing to show as a diff. It is kept in the item's text.", "Ask again", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        foreach (var f in fresh)
        {
            f.RefinesId = source.Id;
            f.Intent = intent;
            f.AuthorRequest = request;
            f.Provider = response.Provider.ToString();
            f.Model = response.Model;
        }
        int at = item.Suggestions.IndexOf(source);
        item.Suggestions.InsertRange(at < 0 ? item.Suggestions.Count : at + 1, fresh);
        for (int i = 0; i < item.Suggestions.Count; i++) item.Suggestions[i].Index = i + 1;
        item.Body = item.Body.TrimEnd() + $"\n\n--- asked again ({DateTime.Now:yyyy-MM-dd HH:mm}) — {(intent.Length > 0 ? intent : "no effect chosen")}" +
                    (request.Length > 0 ? $"; \"{request}\"" : "") + $" ---\n{response.Text.Trim()}";
        item.ModifiedUtc = DateTime.UtcNow;
        MarkDirty();
    }

    /// <summary>Throws a suggestion away for good. The view confirms before calling this.</summary>
    public void Dismiss(Item item, SuggestionEntry entry)
    {
        if (!item.Suggestions.Remove(entry)) return;
        foreach (var child in item.Suggestions.Where(s => s.RefinesId == entry.Id)) child.RefinesId = null;
        for (int i = 0; i < item.Suggestions.Count; i++) item.Suggestions[i].Index = i + 1;
        item.ModifiedUtc = DateTime.UtcNow;
        MarkDirty();
        RefreshTreeTexts();
        Refresh(item);
    }

    public void Refresh(Item item)
    {
        RefreshTreeTexts();
        if (ReferenceEquals(_current, item)) ShowCurrent();
        else BuildTree(select: item);
    }

    private static PlotlineKind KindOf(ExtractedEntity e) => e.Kind switch
    {
        "primary" or "main" => PlotlineKind.Primary,
        "chapter"           => PlotlineKind.Chapter,
        "subplot"           => PlotlineKind.Subplot,
        "extra"             => PlotlineKind.Extra,
        _                   => PlotlineKind.Secondary
    };

    private async void AiFindPlotlines_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var (_, scope, chapters) = AiScope();
        if (!WarnIfNoSummaries(chapters, "Find Plotlines")) return;
        var known = _book.Plotlines.Count == 0 ? "(none recorded yet)" : string.Join("\n", _book.Plotlines.Select(p => $"{p.Name} [{p.Kind.ToString().ToLowerInvariant()}]"));

        IReadOnlyList<ExtractedEntity> found;
        try
        {
            TxtAi.Text = $"Looking for the plotlines of {scope}…";
            Cursor = Cursors.AppStarting;
            _aiCts?.Cancel(); _aiCts = new CancellationTokenSource();
            var r = await SendTemplateAsync(AiPrompts.FindPlotlines, new { book = _book.Title, scope, known_plotlines = known, summaries = SummariesFor(chapters) }, _aiCts.Token);
            found = PlotlineFinder.Parse(r.Text);
            TxtAi.Text = $"AI: done in {r.Elapsed.TotalSeconds:0.0}s" + (r.EstimatedCostUsd is { } cost ? $" · est. ${cost:0.0000}" : "");
        }
        catch (OperationCanceledException) { TxtAi.Text = "AI: cancelled."; return; }
        catch (AiException ex) { TxtAi.Text = "AI: failed."; MessageBox.Show(this, ex.Message, "AI", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        finally { Cursor = Cursors.Arrow; }

        if (found.Count == 0) { MessageBox.Show(this, "No plotlines came back. Summaries that say what happens (not just mood) give the best results.", "Find Plotlines", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        // Chapter numbers in the reply refer to the numbering shown in the summaries (per section).
        Chapter? ByNumber(int n) => chapters.FirstOrDefault(c => _book.ChapterNumber(c) == n);
        Plotline? Known(string name) => _book.Plotlines.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        var newOnes = found.Where(f => Known(f.Name) == null).ToList();
        var dlg = new NewEntitiesWindow(scope, Array.Empty<ExtractedEntity>(), newOnes,
            $"The AI sees these plotlines in {scope}. Ticked ones are added to the book with their kind and linked to the chapters listed; known plotlines were linked to any new chapters already. Untick anything that is a single event rather than a thread.") { Owner = this };
        var add = dlg.ShowDialog() == true ? dlg.ChosenPlotlines : new List<ExtractedEntity>();

        int linked = 0;
        foreach (var f in found)
        {
            var p = Known(f.Name);
            if (p == null)
            {
                if (!add.Contains(f)) continue;
                p = new Plotline { Name = f.Name, Status = PlotlineStatus.Active, Summary = f.Note, Kind = KindOf(f) };
                _book.Plotlines.Add(p);
            }
            foreach (var n in f.Chapters)
            {
                if (ByNumber(n) is not { } ch) continue;
                if (!p.ChapterIds.Contains(ch.Id)) { p.ChapterIds.Add(ch.Id); linked++; }
                if (!ch.PlotlineIds.Contains(p.Id)) ch.PlotlineIds.Add(p.Id);
            }
        }
        MarkDirty();
        BuildTree(select: PlotlinesHeader);
        UpdateStatus($"Plotlines: {found.Count} seen, {add.Count} added, {linked} chapter links made. Open Plotlines in the tree to see the board.");
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
                   ?? new Item { OwnerId = ch.Id, Kind = ItemKind.CharactersInChapter, Title = "Chapter Characters" };
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
