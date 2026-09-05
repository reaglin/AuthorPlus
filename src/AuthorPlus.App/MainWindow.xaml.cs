using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using AuthorPlus.AI;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using WinForms = System.Windows.Forms;

namespace AuthorPlus.App;

/// <summary>
/// The single main window: a tree of the book's parts on the left (Chapters, Characters,
/// Timeline, Plotlines), an editor for the selected item on the right. Chapters edit in a
/// RichTextBox whose FlowDocument is saved as XAML; everything else edits in a field form built
/// at runtime from the item's properties. Switching selection saves the item being left.
/// </summary>
public partial class MainWindow : Window
{
    private readonly BookStore       _store    = new();
    private readonly AiSettingsStore _aiStore  = new();
    private Book?  _book;
    private object? _current;           // Chapter | Character | TimelineEvent | Plotline
    private RichTextBox? _rtb;          // live only while a chapter is open
    private bool _loading;              // suppress change handlers while populating
    private bool _dirty;
    private CancellationTokenSource? _aiCts;

    private const string RecentKey = "recent_books.txt";

    public MainWindow()
    {
        InitializeComponent();
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
        TxtBookTitle.Text = book.Title;
        Title = $"{book.Title} — AuthorPlus";
        RememberRecent(book.FolderPath);
        BuildRecentMenu();
        BuildTree(selectFirst: true);
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

    private TreeViewItem? _chaptersNode, _charactersNode, _timelineNode, _plotlinesNode;

    private void BuildTree(bool selectFirst = false, object? select = null)
    {
        _loading = true;
        Tree.Items.Clear();
        if (_book == null) { _loading = false; return; }

        _chaptersNode   = Section("Chapters",   _book.Chapters,   c => c.Title);
        _charactersNode = Section("Characters", _book.Characters, c => c.Name);
        _timelineNode   = Section("Timeline",   _book.Timeline,   t => string.IsNullOrWhiteSpace(t.When) ? t.Title : $"{t.When} — {t.Title}");
        _plotlinesNode  = Section("Plotlines",  _book.Plotlines,  p => p.Name);
        _loading = false;

        var target = select ?? (selectFirst ? _book.Chapters.FirstOrDefault() : null);
        if (target != null) SelectItem(target);
    }

    private TreeViewItem Section<T>(string header, List<T> items, Func<T, string> label) where T : class
    {
        var node = new TreeViewItem { Header = $"{header} ({items.Count})", IsExpanded = true, FontWeight = FontWeights.SemiBold, Tag = header };
        foreach (var item in items)
            node.Items.Add(new TreeViewItem { Header = label(item), Tag = item, FontWeight = FontWeights.Normal });
        Tree.Items.Add(node);
        return node;
    }

    private void RefreshTreeTexts()
    {
        if (_book == null) return;
        void Refresh<T>(TreeViewItem? node, string header, List<T> list, Func<T, string> label)
        {
            if (node == null) return;
            node.Header = $"{header} ({list.Count})";
            foreach (TreeViewItem child in node.Items)
                if (child.Tag is T t) child.Header = label(t);
        }
        Refresh(_chaptersNode,   "Chapters",   _book.Chapters,   c => c.Title);
        Refresh(_charactersNode, "Characters", _book.Characters, c => c.Name);
        Refresh(_timelineNode,   "Timeline",   _book.Timeline,   t => string.IsNullOrWhiteSpace(t.When) ? t.Title : $"{t.When} — {t.Title}");
        Refresh(_plotlinesNode,  "Plotlines",  _book.Plotlines,  p => p.Name);
        TxtWords.Text = $"{_book.TotalWords:N0} words" + (_book.TargetWords > 0 ? $" of {_book.TargetWords:N0}" : "");
    }

    private void SelectItem(object item)
    {
        foreach (TreeViewItem section in Tree.Items)
            foreach (TreeViewItem child in section.Items)
                if (ReferenceEquals(child.Tag, item)) { child.IsSelected = true; child.BringIntoView(); return; }
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_loading) return;
        CommitCurrent();
        var tag = (Tree.SelectedItem as TreeViewItem)?.Tag;
        _current = tag is string ? null : tag;
        ShowCurrent();
        UpdateMenus();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EDITOR
    // ══════════════════════════════════════════════════════════════════════════

    private void ShowCurrent()
    {
        _loading = true;
        _rtb = null;
        TxtName.IsEnabled = _current != null;

        switch (_current)
        {
            case Chapter ch:
                TxtKind.Text = "Chapter";
                TxtName.Text = ch.Title;
                Editor.Content = BuildChapterEditor(ch);
                break;
            case Character c:
                TxtKind.Text = "Character";
                TxtName.Text = c.Name;
                Editor.Content = FieldForm(
                    ("Role",        () => c.Role,        v => c.Role = v,        1),
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
                    Text = _book == null ? "Open a book to begin." : "Pick an item in the tree, or add one from the Book menu.",
                    Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                break;
        }
        _loading = false;
    }

    /// <summary>A label + multi-line TextBox per field; edits flow straight into the model.</summary>
    private UIElement FieldForm(params (string Label, Func<string> Get, Action<string> Set, int Lines)[] fields)
    {
        var panel = new StackPanel();
        foreach (var (label, get, set, lines) in fields)
        {
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
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

    private UIElement BuildChapterEditor(Chapter ch)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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

        // Summary + notes below the text (kept short; the summary is what AI fills in).
        var bottom = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        bottom.Children.Add(new TextBlock { Text = "Summary", FontWeight = FontWeights.SemiBold });
        var summary = new TextBox { Text = ch.Summary, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinLines = 2, MaxLines = 5, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(4) };
        summary.TextChanged += (_, _) => { if (!_loading) { ch.Summary = summary.Text; _dirty = true; } };
        _summaryBox = summary;
        bottom.Children.Add(summary);
        Grid.SetRow(bottom, 2);
        grid.Children.Add(bottom);

        UpdateWordCount(ch);
        return grid;
    }

    private TextBox? _summaryBox;

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

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private void UpdateWordCount(Chapter ch)
    {
        if (_rtb == null) return;
        var words = CountWords(PlainText(_rtb.Document));
        TxtWords.Text = $"Chapter: {words:N0} words   ·   Book: {(_book?.TotalWords - ch.WordCount + words) ?? 0:N0}";
    }

    /// <summary>Pushes the on-screen state of the current item back into the model / disk.</summary>
    private void CommitCurrent()
    {
        if (_book == null || _current == null) return;
        if (_current is Chapter ch && _rtb != null && _dirty)
        {
            var xaml = XamlWriter.Save(_rtb.Document);
            _store.SaveChapterBody(_book, ch, xaml, CountWords(PlainText(_rtb.Document)));
        }
    }

    private void TxtName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current == null) return;
        switch (_current)
        {
            case Chapter ch:       ch.Title = TxtName.Text; break;
            case Character c:      c.Name   = TxtName.Text; break;
            case TimelineEvent t:  t.Title  = TxtName.Text; break;
            case Plotline p:       p.Name   = TxtName.Text; break;
        }
        _dirty = true;
        if (Tree.SelectedItem is TreeViewItem tvi && ReferenceEquals(tvi.Tag, _current))
            tvi.Header = _current is TimelineEvent te && !string.IsNullOrWhiteSpace(te.When) ? $"{te.When} — {te.Title}" : TxtName.Text;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // BOOK MENU
    // ══════════════════════════════════════════════════════════════════════════

    private void AddChapter_Click(object sender, RoutedEventArgs e)   => AddItem(_book?.Chapters,   new Chapter { Title = $"Chapter {(_book?.Chapters.Count ?? 0) + 1}" });
    private void AddCharacter_Click(object sender, RoutedEventArgs e) => AddItem(_book?.Characters, new Character());
    private void AddEvent_Click(object sender, RoutedEventArgs e)     => AddItem(_book?.Timeline,   new TimelineEvent { Order = (_book?.Timeline.Count ?? 0) + 1 });
    private void AddPlotline_Click(object sender, RoutedEventArgs e)  => AddItem(_book?.Plotlines,  new Plotline());

    private void AddItem<T>(List<T>? list, T item) where T : class
    {
        if (_book == null || list == null) return;
        CommitCurrent();
        list.Add(item);
        _dirty = true;
        BuildTree(select: item);
        TxtName.Focus();
        TxtName.SelectAll();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)   => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        if (_book == null || _current == null) return;
        bool moved = _current switch
        {
            Chapter c       => Shift(_book.Chapters, c, delta),
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

    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || _current == null) return;
        var name = TxtName.Text;
        var extra = _current is Chapter ? " Its text file will be deleted on the next save." : "";
        if (MessageBox.Show(this, $"Delete \"{name}\"?{extra}", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        switch (_current)
        {
            case Chapter c:       _book.Chapters.Remove(c); break;
            case Character c:     _book.Characters.Remove(c); break;
            case TimelineEvent t: _book.Timeline.Remove(t); break;
            case Plotline p:      _book.Plotlines.Remove(p); break;
        }
        _current = null;
        _dirty = true;
        BuildTree();
        ShowCurrent();
        UpdateMenus();
    }

    private void BookProperties_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;
        var dlg = new PromptWindow("Book Properties", "Title:", "Author:", "Synopsis:", "Target words:")
        {
            Owner = this, Value1 = _book.Title, Value2 = _book.Author, Value3 = _book.Synopsis,
            Value4 = _book.TargetWords > 0 ? _book.TargetWords.ToString() : ""
        };
        if (dlg.ShowDialog() != true) return;
        _book.Title = string.IsNullOrWhiteSpace(dlg.Value1) ? _book.Title : dlg.Value1.Trim();
        _book.Author = dlg.Value2.Trim();
        _book.Synopsis = dlg.Value3;
        _book.TargetWords = int.TryParse(dlg.Value4, out var tw) ? tw : 0;
        TxtBookTitle.Text = _book.Title;
        Title = $"{_book.Title} — AuthorPlus";
        _dirty = true;
        RefreshTreeTexts();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AI
    // ══════════════════════════════════════════════════════════════════════════

    private void AiSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AiSettingsWindow(_aiStore) { Owner = this };
        dlg.ShowDialog();
        UpdateMenus();
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var dir = ActivityLog.LogDirectory;
        if (dir != null) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private IAiProvider? GetProviderOrExplain()
    {
        var effective = PreseMakerCredentials.CreateEffectiveSettings(_aiStore.Load());
        try { return AiProviderRouter.GetProvider(effective); }
        catch (InvalidOperationException ex)
        {
            if (MessageBox.Show(this, ex.Message + "\n\nOpen AI Settings now?", "AI", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                AiSettings_Click(this, new RoutedEventArgs());
            return null;
        }
    }

    private async void AiSummarize_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || _current is not Chapter ch || _rtb == null) return;
        var text = PlainText(_rtb.Document).Trim();
        if (text.Length < 200)
        {
            MessageBox.Show(this, "Write at least a few paragraphs first — there is not enough text to summarize.", "AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var provider = GetProviderOrExplain();
        if (provider == null) return;

        await RunAi($"Summarizing \"{ch.Title}\" with {provider.ProviderName}…", async ct =>
        {
            var result = await provider.GenerateAsync(
                "You are an editorial assistant for a novelist. Summarize the chapter in 3–5 sentences of plain prose: " +
                "what happens, who is involved, and what changes. Do not praise, critique, or add anything not in the text.",
                $"Book: {_book.Title}\nChapter: {ch.Title}\n\n{text}", ct, maxTokens: 1000);
            ch.Summary = result.Trim();
            if (_summaryBox != null) { _loading = true; _summaryBox.Text = ch.Summary; _loading = false; }
            _dirty = true;
        });
    }

    private async void AiCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_book == null || _current is not Character c) return;
        var provider = GetProviderOrExplain();
        if (provider == null) return;

        var known = $"Name: {c.Name}\nRole: {c.Role}\nOrigin: {c.Origin}\nDescription: {c.Description}\nMotivations: {c.Motivations}\nActions: {c.Actions}\nArc: {c.Arc}\nNotes: {c.Notes}";
        var context = string.Join("\n\n", _book.Chapters.Where(x => !string.IsNullOrWhiteSpace(x.Summary)).Select(x => $"{x.Title}: {x.Summary}"));

        await RunAi($"Drafting a profile for {c.Name} with {provider.ProviderName}…", async ct =>
        {
            var result = await provider.GenerateAsync(
                "You are a story-development assistant. Given what the author already knows about a character and the chapter " +
                "summaries, suggest additions for the blank or thin fields. Return plain text with headings exactly: " +
                "Description, Motivations, Actions, Arc. Keep each under 120 words. Never contradict what is given.",
                $"Book: {_book.Title}\nSynopsis: {_book.Synopsis}\n\nCharacter as known:\n{known}\n\nChapter summaries:\n{context}", ct, maxTokens: 1500);
            c.Notes = (c.Notes.Length > 0 ? c.Notes + "\n\n" : "") + $"— AI suggestions ({DateTime.Now:yyyy-MM-dd}) —\n{result.Trim()}";
            _dirty = true;
            ShowCurrent();
        });
    }

    private async Task RunAi(string statusText, Func<CancellationToken, Task> work)
    {
        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        MnuAi.IsEnabled = false;
        TxtAi.Text = statusText;
        Cursor = Cursors.AppStarting;
        try
        {
            await work(_aiCts.Token);
            TxtAi.Text = "AI: done.";
        }
        catch (OperationCanceledException) { TxtAi.Text = "AI: cancelled."; }
        catch (Exception ex)
        {
            TxtAi.Text = "AI: failed.";
            ActivityLog.Error("AI", statusText, ex);
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
        var settings = _aiStore.Load();
        var effective = PreseMakerCredentials.CreateEffectiveSettings(settings);
        TxtAi.Text = effective.SelectedProviderHasKey
            ? $"AI: {AiProviderRouter.DisplayName(effective.SelectedProvider)} · {effective.ModelFor(effective.SelectedProvider)}"
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
            $"AuthorPlus {v}\n\nAn AI-enhanced authoring tool for organising and writing books: chapters, characters, timeline and plotlines.\n\n" +
            $"Books live in:\n{_store.Root}\n\n© Ron Eaglin",
            "About AuthorPlus", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
