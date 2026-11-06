using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Export;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace AuthorPlus.App;

/// <summary>What the chapter editor is for at the moment (Ron, 2026-11-06).</summary>
public enum EditorMode
{
    /// <summary>The editing tools, and nothing else.</summary>
    Write,
    /// <summary>The AI writes new text where the author clicks a + between paragraphs, or a new chapter.</summary>
    Create,
    /// <summary>The AI's read of a paragraph (from the ¶ beside it) or of the chapter, in a panel beside the text.</summary>
    Analyze,
    /// <summary>Every character's name coloured in the text; their form in a panel beside it.</summary>
    Characters,
    /// <summary>The plotlines in the chapter, their key moments washed in the text; each one in the chapter and the book beside it.</summary>
    Plotlines,
    /// <summary>The chapter read aloud: the narrator's voice and each character's; who says each line of dialogue.</summary>
    Listen
}

/// <summary>
/// The chapter editor's modes. Write is the editor as it always was, with only the editing tools
/// on its toolbar. Analysis and Characters keep the text in view, read-only, with marks drawn over
/// it (<see cref="ChapterOverlay"/>, never saved into the chapter) and a panel on the right:
/// the chapter's summary, characters, plotlines and analyses, or one paragraph's read and
/// rewrites; the chapter's cast, or one character's form.
/// </summary>
public partial class MainWindow
{
    public static readonly RoutedCommand WriteModeCommand      = new("WriteMode",      typeof(MainWindow));
    public static readonly RoutedCommand AnalyzeModeCommand    = new("AnalyzeMode",    typeof(MainWindow));
    public static readonly RoutedCommand CharactersModeCommand = new("CharactersMode", typeof(MainWindow));
    public static readonly RoutedCommand CreateModeCommand     = new("CreateMode",     typeof(MainWindow));
    public static readonly RoutedCommand PlotlinesModeCommand  = new("PlotlinesMode",  typeof(MainWindow));
    public static readonly RoutedCommand ListenModeCommand     = new("ListenMode",     typeof(MainWindow));

    private EditorMode _mode = EditorMode.Write;
    private ChapterOverlay? _overlay;
    private ContentControl? _side;
    private Guid? _shownChapterId;              // the chapter the panel state below belongs to
    private int? _restoreAt;                    // the text at the top of the view, kept when the same chapter is rebuilt (a position in the text, not pixels)
    private int? _caretAt;                      // where the caret goes when the chapter comes back in Write mode
    private TextBlock? _modeNotice;             // "the text is read-only here", shown for a moment when someone types
    private DispatcherTimer? _noticeTimer;
    private readonly Dictionary<Guid, int> _castOrder = new();     // Characters: colour order, by first appearance in this chapter
    private readonly Dictionary<Guid, string> _readSig = new();    // Analysis: what each read in the panel looked like when drawn
    private int _panelParagraph = -1;           // Analysis: the paragraph open in the panel, -1 = the chapter
    private string _paragraphRequest = "";      // Analysis: what the author wants that paragraph to do
    private Guid? _panelCharacterId;            // Characters: the character open in the panel
    private string? _panelCandidate;            // Characters: the possible new name open in the panel
    private int _mentionAt = -1;                // Characters: which mention Previous/Next is on
    private List<(Paragraph Paragraph, string Text)> _readable = new();
    private List<NameMark> _nameMarks = new();
    private IReadOnlyList<NameCandidate> _candidates = Array.Empty<NameCandidate>();
    private bool _marksQueued;

    private static readonly Brush StateNone = Rgb(0x6E, 0x78, 0x84), StateRead = Rgb(0x1A, 0x5F, 0xB4),
                                  StateOpen = Rgb(0xD9, 0x86, 0x00), StateDone = Rgb(0x2E, 0x85, 0x40);

    /// <summary>One colour per character, by their place in the cast (washed when drawn over text).</summary>
    private static readonly Color[] CastColours =
    {
        Color.FromRgb(0xF2, 0x8E, 0x2B), Color.FromRgb(0x4E, 0x9A, 0xF1), Color.FromRgb(0x57, 0xB8, 0x5A), Color.FromRgb(0xD9, 0x5C, 0xB8),
        Color.FromRgb(0x9B, 0x6B, 0xE0), Color.FromRgb(0xE5, 0xC0, 0x2B), Color.FromRgb(0x3C, 0xB8, 0xB0), Color.FromRgb(0xE0, 0x5A, 0x4F)
    };

    private static SolidColorBrush Rgb(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

    /// <summary>A character's colour: by the order they first appear in this chapter, so the first eight on the page all differ.</summary>
    private Color CastColour(Character c) =>
        CastColours[(_castOrder.TryGetValue(c.Id, out var n) ? n : _castOrder.Count + Math.Max(0, _book!.Characters.IndexOf(c))) % CastColours.Length];

    private bool AiBusy => !MnuAi.IsEnabled;     // a background AI run (Summarize, Find who is in it) is going
    private Brush Wash(Character c) { var col = CastColour(c); var b = new SolidColorBrush(Color.FromArgb(0x5C, col.R, col.G, col.B)); b.Freeze(); return b; }
    private Brush Solid(Character c) { var b = new SolidColorBrush(CastColour(c)); b.Freeze(); return b; }

    private void RegisterModeCommands()
    {
        CommandBindings.Add(new CommandBinding(WriteModeCommand,      (_, _) => SetMode(EditorMode.Write)));
        CommandBindings.Add(new CommandBinding(AnalyzeModeCommand,    (_, _) => SetMode(EditorMode.Analyze)));
        CommandBindings.Add(new CommandBinding(CharactersModeCommand, (_, _) => SetMode(EditorMode.Characters)));
        CommandBindings.Add(new CommandBinding(CreateModeCommand,     (_, _) => SetMode(EditorMode.Create)));
        CommandBindings.Add(new CommandBinding(PlotlinesModeCommand,  (_, _) => SetMode(EditorMode.Plotlines)));
        CommandBindings.Add(new CommandBinding(ListenModeCommand,     (_, _) => SetMode(EditorMode.Listen)));
        UpdateModeMenu();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && ListenKey(Key.Escape)) e.Handled = true; };
    }

    private void UpdateModeMenu()
    {
        MnuWriteMode.IsChecked = _mode == EditorMode.Write;
        MnuAnalyzeMode.IsChecked = _mode == EditorMode.Analyze;
        MnuCharactersMode.IsChecked = _mode == EditorMode.Characters;
        MnuCreateMode.IsChecked = _mode == EditorMode.Create;
        MnuPlotlinesMode.IsChecked = _mode == EditorMode.Plotlines;
        MnuListenMode.IsChecked = _mode == EditorMode.Listen;
    }

    private void SetMode(EditorMode mode)
    {
        UpdateModeMenu();                                    // a checkable menu item ticks itself; put it right
        if (_mode == mode) return;
        if (_mode == EditorMode.Listen) StopListening("Stopped reading — you left Listen mode.");
        _mode = mode;
        UpdateModeMenu();
        if (_current is Chapter)
        {
            CommitCurrent();
            ShowCurrent();
        }
        else if (CurrentChapter() is { } ch)
        {
            SelectNode(ch);                                  // an item of a chapter is open: go to its chapter in the new mode
        }
        else UpdateStatus(mode switch
        {
            EditorMode.Analyze    => "Analysis mode is on — open a chapter to use it.",
            EditorMode.Characters => "Characters mode is on — open a chapter to use it.",
            EditorMode.Create     => "Create mode is on — open a chapter to use it.",
            EditorMode.Plotlines  => "Plotlines mode is on — open a chapter to use it.",
            EditorMode.Listen     => "Listen mode is on — open a chapter to use it.",
            _                     => "Write mode is on."
        });
    }

    // ── The mode bar ──────────────────────────────────────────────────────────

    private UIElement ModeBar()
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (mode, label, tip) in new[]
        {
            (EditorMode.Write,      "✎  Write",      "Write mode (Ctrl+1): the editing tools — type, format, find and replace."),
            (EditorMode.Create,     "✚  Create",     "Create mode (Ctrl+2): click a + between paragraphs, say what to write and how, and the AI writes it there; or write a new chapter."),
            (EditorMode.Analyze,    "¶  Analysis",   "Analysis mode (Ctrl+3): click the ¶ beside a paragraph to analyze it or get suggestions for it; the chapter's summary, characters, plotlines and analysis are in the panel."),
            (EditorMode.Characters, "👤  Characters", "Characters mode (Ctrl+4): every character's name coloured where the text uses it; click a name for what this chapter shows of them, the book analysis and their form."),
            (EditorMode.Plotlines,  "🧵  Plotlines",  "Plotlines mode (Ctrl+5): the plotlines in this chapter, their key moments marked in the text; each one in this chapter and across the book."),
            (EditorMode.Listen,     "🔊  Listen",     "Listen mode (Ctrl+6): the chapter read aloud — the narrator's voice and each character's; who says each line of dialogue.")
        })
        {
            var b = new RadioButton
            {
                Content = label, GroupName = "EditorMode", IsChecked = _mode == mode, ToolTip = tip,
                Style = (Style)FindResource(ToolBar.RadioButtonStyleKey), Padding = new Thickness(12, 4, 12, 4),
                FontWeight = _mode == mode ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 2, 0),
                BorderBrush = Palette.Line, BorderThickness = new Thickness(1)
            };
            AutomationProperties(b, label.Trim());
            var m = mode;
            b.Checked += (_, _) => { if (!_loading) SetMode(m); };
            buttons.Children.Add(b);
        }
        DockPanel.SetDock(buttons, Dock.Left);
        row.Children.Add(buttons);
        _modeNotice = new TextBlock
        {
            Text = "The text is read-only here — click Write (Ctrl+1), or double-click where you want to type.",
            Background = Palette.UnlinkedBg, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(12, 0, 0, 0), FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed
        };
        DockPanel.SetDock(_modeNotice, Dock.Right);
        row.Children.Add(_modeNotice);
        row.Children.Add(new TextBlock
        {
            Text = _mode switch
            {
                EditorMode.Analyze    => "Click a paragraph (or the ¶ beside it) to analyze it or get suggestions for it. The whole chapter's summary, characters, plotlines and analysis are on the right. The text is read-only here — double-click to go back to writing there.",
                EditorMode.Create     => "Click a + between paragraphs (or the one at the end) and say on the right what to write and how — the AI writes it there for you to read before it goes in. With no + chosen, write a new chapter. The text is read-only here — double-click to go back to writing there.",
                EditorMode.Listen     => "Each line of dialogue is coloured by who says it (dotted: not known yet) — click one to change it. Click a paragraph to play from there; Play and the voices are on the right. The text is read-only here — double-click to go back to writing there.",
                EditorMode.Plotlines  => "The plotlines in this chapter are on the right; once one is analyzed here, its key moments are washed in its colour in the text — click one. The text is read-only here — double-click to go back to writing there.",
                EditorMode.Characters => "Each character is coloured wherever the text names them; click a name to see them in this chapter and across the book, and their form, on the right. Dotted underlines are names that are not characters yet. The text is read-only here — double-click to go back to writing there.",
                _                     => "The editing tools. Create has the AI write new text; Analysis reads a paragraph or the chapter; Characters and Plotlines show who and what is where."
            },
            Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontSize = 12
        });
        return row;
    }

    private static void AutomationProperties(FrameworkElement e, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(e, name);

    /// <summary>The body of the chapter editor below its toolbars: the text alone in Write mode, the text and the panel otherwise.</summary>
    private UIElement ModeBody(Chapter ch, RichTextBox rtb)
    {
        _overlay = null;
        _side = null;
        if (_mode == EditorMode.Write) return rtb;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430), MinWidth = 300 });
        Grid.SetColumn(rtb, 0);
        grid.Children.Add(rtb);
        var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.WhiteSmoke, ToolTip = "Drag to widen the panel or the text" };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);
        _side = new ContentControl();
        var frame = new Border { BorderBrush = Palette.Line, BorderThickness = new Thickness(1), Background = Brushes.White, Child = _side };
        Grid.SetColumn(frame, 2);
        grid.Children.Add(frame);

        rtb.IsReadOnly = true;
        rtb.IsReadOnlyCaretVisible = false;
        rtb.IsInactiveSelectionHighlightEnabled = true;          // Previous / Next mention stays visible while the panel has focus
        rtb.SpellCheck.IsEnabled = false;
        if (_mode is EditorMode.Analyze or EditorMode.Listen) rtb.Padding = new Thickness(ChapterOverlay.IconSize + 22, 16, 24, 16);
        if (_mode == EditorMode.Create) rtb.Padding = new Thickness(ChapterOverlay.IconSize + 22, 24, 24, 48);   // room for the + above the first paragraph and after the last

        // Typing here does nothing: say so, rather than ignore it.
        rtb.PreviewTextInput += (_, e) => { e.Handled = true; ReadOnlyNotice(); };
        rtb.PreviewKeyDown += (_, e) =>
        {
            if (ListenKey(e.Key)) { e.Handled = true; return; }
            if (e.Key is Key.Enter or Key.Back or Key.Delete or Key.Space or Key.Tab) { e.Handled = true; ReadOnlyNotice(); }
        };
        // Double-click: back to Write with the caret there.
        rtb.MouseDoubleClick += (_, e) =>
        {
            if (rtb.GetPositionFromPoint(e.GetPosition(rtb), true) is not { } at) return;
            _caretAt = rtb.Document.ContentStart.GetOffsetToPosition(at);
            e.Handled = true;
            SetMode(EditorMode.Write);
        };
        // Listen: clicking in a paragraph is where Play starts.
        if (_mode == EditorMode.Listen)
            rtb.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (!rtb.Selection.IsEmpty || e.ClickCount > 1) return;
                var para = rtb.GetPositionFromPoint(e.GetPosition(rtb), false)?.Paragraph;
                int i = _listenParas.FindIndex(r => ReferenceEquals(r.Paragraph, para));
                if (i < 0) return;
                if (_playCts != null && _current is Chapter playing) { _listenFrom = i; PlayFrom(playing, i); return; }   // while reading: read from here
                if (i != _listenFrom) { _listenFrom = i; _panelQuote = null; _overlay?.SetStart(para); ShowSidePanel(); }
            };
        // Analysis: clicking in a paragraph opens it, as the ¶ does (not when text is being selected).
        if (_mode == EditorMode.Analyze)
            rtb.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (!rtb.Selection.IsEmpty || e.ClickCount > 1) return;
                var para = rtb.GetPositionFromPoint(e.GetPosition(rtb), false)?.Paragraph;
                int i = _readable.FindIndex(r => ReferenceEquals(r.Paragraph, para));
                if (i >= 0 && i != _panelParagraph) OpenParagraph(i, scroll: false);
            };

        rtb.Loaded += (_, _) =>
        {
            if (!ReferenceEquals(rtb, _rtb)) return;
            var layer = AdornerLayer.GetAdornerLayer(rtb);
            if (layer == null) return;
            _overlay = new ChapterOverlay(rtb);
            _overlay.ParagraphClicked += i => OpenParagraph(i, scroll: false);
            _overlay.NameClicked += OpenName;
            _overlay.InsertClicked += OpenInsert;
            layer.Add(_overlay);
            RefreshMarks();
            ShowSidePanel();
        };
        return grid;
    }

    /// <summary>Called from ShowCurrent before the editor is rebuilt: keeps the panel and scroll position for the same chapter.</summary>
    private void BeforeShow()
    {
        var shownId = _current is Chapter c ? c.Id : (Guid?)null;
        if (_rtb != null && shownId != null && shownId == _shownChapterId)
        {
            // Kept as a place in the text: the column is a different width in each mode, so pixels would land elsewhere.
            var start = _rtb.Document.ContentStart;
            if (_rtb.GetPositionFromPoint(new System.Windows.Point(_rtb.Padding.Left + 4, _rtb.Padding.Top + 4), true) is { } top)
                _restoreAt = start.GetOffsetToPosition(top);
            if (_caretAt == null)
            {
                if (!_rtb.IsReadOnly) _caretAt = start.GetOffsetToPosition(_rtb.CaretPosition);
                else if (_mode == EditorMode.Write && _panelParagraph >= 0 && _panelParagraph < _readable.Count)
                    _caretAt = start.GetOffsetToPosition(_readable[_panelParagraph].Paragraph.ContentStart);
            }
        }
        else
        {
            _restoreAt = null; _caretAt = null;
            if (shownId != _shownChapterId)
            {
                _panelParagraph = -1; _paragraphRequest = ""; _panelCharacterId = null; _panelCandidate = null; _mentionAt = -1;
                _insertAt = -1; _draft = null; _draftAsWritten = null; _inserted = null; _insertedTexts = null; _showingPutIn = false; _panelPlotlineId = null;
                _panelQuote = null; _listenFrom = 0; StopListening("Stopped reading — you left the chapter.");
            }
        }
        _shownChapterId = shownId;
        _overlay = null; _side = null;
    }

    /// <summary>After the chapter editor is built: back to where the author was.</summary>
    private void AfterChapterShown(RichTextBox rtb)
    {
        var top = _restoreAt; var caret = _caretAt;
        _restoreAt = null; _caretAt = null;
        if (top == null && caret == null) return;
        rtb.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!ReferenceEquals(rtb, _rtb)) return;
            var start = rtb.Document.ContentStart;
            if (top is { } t && start.GetPositionAtOffset(t) is { } p)
                rtb.ScrollToVerticalOffset(rtb.VerticalOffset + p.GetCharacterRect(LogicalDirection.Forward).Top - rtb.Padding.Top);
            if (_mode == EditorMode.Write && caret is { } c && start.GetPositionAtOffset(c) is { } at)
            {
                rtb.CaretPosition = at;
                rtb.Focus();
                ScrollTo(at);
            }
        });
    }

    private void ReadOnlyNotice()
    {
        if (_modeNotice == null) return;
        _modeNotice.Visibility = Visibility.Visible;
        _noticeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _noticeTimer.Stop();
        _noticeTimer.Tick -= HideNotice;
        _noticeTimer.Tick += HideNotice;
        _noticeTimer.Start();
    }

    private void HideNotice(object? sender, EventArgs e)
    {
        _noticeTimer?.Stop();
        if (_modeNotice != null) _modeNotice.Visibility = Visibility.Collapsed;
    }

    /// <summary>The text changed under a mode (a rewrite applied): redraw the marks once things settle.</summary>
    private void QueueMarks()
    {
        if (_mode == EditorMode.Write || _marksQueued) return;
        _marksQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { _marksQueued = false; RefreshMarks(); });
    }

    // ── Paragraphs ────────────────────────────────────────────────────────────

    private static IEnumerable<Paragraph> AllParagraphs(BlockCollection blocks)
    {
        foreach (var b in blocks)
        {
            switch (b)
            {
                case Paragraph p: yield return p; break;
                case List l: foreach (var li in l.ListItems) foreach (var p in AllParagraphs(li.Blocks)) yield return p; break;
                case System.Windows.Documents.Section s: foreach (var p in AllParagraphs(s.Blocks)) yield return p; break;
                case Table t:
                    foreach (var g in t.RowGroups) foreach (var r in g.Rows) foreach (var cell in r.Cells)
                        foreach (var p in AllParagraphs(cell.Blocks)) yield return p;
                    break;
            }
        }
    }

    private static string TextOf(Paragraph p) => new TextRange(p.ContentStart, p.ContentEnd).Text.Trim();

    /// <summary>The paragraphs worth reading: three words or more, not scene breaks.</summary>
    private List<(Paragraph Paragraph, string Text)> ReadableParagraphs()
    {
        if (_rtb == null) return new();
        return AllParagraphs(_rtb.Document.Blocks)
            .Select(p => (p, TextOf(p)))
            .Where(x => x.Item2.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length >= 3 && x.Item2 != FlowDocumentXaml.SceneBreak)
            .ToList();
    }

    /// <summary>The reads of this chapter's paragraphs, by the paragraph they belong to now (-1: the paragraph is gone).</summary>
    private Dictionary<int, List<(Item Read, bool Exact)>> ReadsByParagraph(Chapter ch)
    {
        var texts = _readable.Select(r => r.Text).ToList();
        var map = new Dictionary<int, List<(Item, bool)>>();
        foreach (var read in _book!.ItemsOf(ch.Id).Where(i => i.Kind == ItemKind.ParagraphAnalysis))
        {
            var (index, exact) = ParagraphReads.Match(texts, read.Passage, read.ParagraphIndex);
            if (!map.TryGetValue(index, out var list)) map[index] = list = new();
            list.Add((read, exact));
        }
        foreach (var list in map.Values) list.Sort((a, b) => b.Item1.CreatedUtc.CompareTo(a.Item1.CreatedUtc));
        return map;
    }

    private static (Brush Fill, string Word) StateLook(ParagraphState s) => s switch
    {
        ParagraphState.Read => (StateRead, "analyzed"),
        ParagraphState.Open => (StateOpen, "suggestions waiting"),
        ParagraphState.Done => (StateDone, "dealt with"),
        _                   => (StateNone, "not looked at yet")
    };

    // ── Marks over the text ───────────────────────────────────────────────────

    private void RefreshMarks()
    {
        if (_overlay == null || _rtb == null || _book == null || _current is not Chapter ch) return;
        if (_mode == EditorMode.Analyze)
        {
            _readable = ReadableParagraphs();
            var reads = ReadsByParagraph(ch);
            var marks = new List<ParagraphMark>();
            for (int i = 0; i < _readable.Count; i++)
            {
                var state = ParagraphReads.StateOf(reads.TryGetValue(i, out var r) ? r.Select(x => x.Read) : Array.Empty<Item>());
                var (fill, word) = StateLook(state);
                marks.Add(new ParagraphMark(_readable[i].Paragraph, i, fill, $"Paragraph {i + 1}: {word} — click for its read and rewrites"));
            }
            if (_panelParagraph >= _readable.Count) _panelParagraph = -1;
            _overlay.ShowParagraphs(marks, _panelParagraph);
        }
        else if (_mode == EditorMode.Characters)
        {
            var paragraphs = AllParagraphs(_rtb.Document.Blocks).ToList();
            var texts = paragraphs.Select(p => RunsOf(p).Text).ToList();
            _candidates = NameScan.Candidates(texts, _book.Characters, _book.NotNames);
            var candidateNames = _candidates.Select(c => c.Name).ToList();
            var found = paragraphs.Select(p => { var (text, runs) = RunsOf(p); return (runs, spans: NameScan.Spans(text, _book.Characters, candidateNames)); }).ToList();
            _castOrder.Clear();
            foreach (var f in found)
                foreach (var sp in f.spans)
                    if (sp.CharacterId is { } cid && !_castOrder.ContainsKey(cid)) _castOrder[cid] = _castOrder.Count;
            _nameMarks = new List<NameMark>();
            foreach (var (runs, spans) in found)
            {
                foreach (var span in spans)
                {
                    var start = PointerAt(runs, span.Start, end: false);
                    var end = PointerAt(runs, span.Start + span.Length, end: true);
                    if (start == null || end == null) continue;
                    if (span.CharacterId is { } id && _book.Characters.FirstOrDefault(c => c.Id == id) is { } who)
                        _nameMarks.Add(new NameMark(start, end, Wash(who), who, $"{who.Name} — click for their form"));
                    else if (_candidates.FirstOrDefault(c => c.Name.Equals(span.Name, StringComparison.OrdinalIgnoreCase)) is { } cand)
                        _nameMarks.Add(new NameMark(start, end, null, cand, $"\"{cand.Name}\" is not a character yet — click to add them"));
                }
            }
            object? selected = _panelCharacterId is { } sel ? _book.Characters.FirstOrDefault(c => c.Id == sel)
                             : _panelCandidate is { } n ? _candidates.FirstOrDefault(c => c.Name == n) : null;
            _overlay.ShowNames(_nameMarks, selected);
        }
        else if (_mode == EditorMode.Create) ShowCreateMarks();
        else if (_mode == EditorMode.Plotlines) ShowPlotlineMarks(ch);
        else if (_mode == EditorMode.Listen) ShowListenMarks(ch);
        else _overlay.Clear();
    }

    /// <summary>A paragraph's text as its runs hold it, with where each run starts in that text.</summary>
    private static (string Text, List<(Run Run, int Start)> Runs) RunsOf(Paragraph p)
    {
        var sb = new System.Text.StringBuilder();
        var runs = new List<(Run, int)>();
        void Walk(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run r: runs.Add((r, sb.Length)); sb.Append(r.Text); break;
                    case LineBreak: sb.Append('\n'); break;
                    case Span s: Walk(s.Inlines); break;
                }
            }
        }
        Walk(p.Inlines);
        return (sb.ToString(), runs);
    }

    private static TextPointer? PointerAt(List<(Run Run, int Start)> runs, int offset, bool end)
    {
        foreach (var (run, start) in runs)
        {
            int len = run.Text.Length;
            if (offset >= start && (end ? offset <= start + len : offset < start + len))
                return run.ContentStart.GetPositionAtOffset(offset - start);
        }
        return null;
    }

    private void ScrollTo(TextPointer at)
    {
        if (_rtb == null) return;
        var rect = at.GetCharacterRect(LogicalDirection.Forward);
        if (rect.Top < 40 || rect.Bottom > _rtb.ActualHeight - 40)
            _rtb.ScrollToVerticalOffset(_rtb.VerticalOffset + rect.Top - _rtb.ActualHeight / 3);
    }

    private void OpenParagraph(int index, bool scroll)
    {
        if (index != _panelParagraph) _paragraphRequest = "";
        _panelParagraph = index;
        RefreshMarks();
        ShowSidePanel();
        if (scroll && index >= 0 && index < _readable.Count) ScrollTo(_readable[index].Paragraph.ContentStart);
    }

    private void OpenName(object target)
    {
        switch (target)
        {
            case Character c:
                if (_panelCharacterId != c.Id) _characterView = CharacterView.Chapter;   // another character opens on what this chapter shows of them
                _panelCharacterId = c.Id; _panelCandidate = null; break;
            case NameCandidate n: _panelCandidate = n.Name; _panelCharacterId = null; break;
            case QuoteLine q: _panelQuote = q; break;
            case Plotline p:
                if (_panelPlotlineId != p.Id) _plotlineView = PlotlineView.Chapter;
                _panelPlotlineId = p.Id; break;
        }
        _mentionAt = -1;
        RefreshMarks();
        ShowSidePanel();
    }

    // ── The panel ─────────────────────────────────────────────────────────────

    private void ShowSidePanel(bool keepScroll = false)
    {
        if (_side == null || _book == null || _current is not Chapter ch) return;
        var offset = keepScroll && _side.Content is ScrollViewer old ? old.VerticalOffset : 0;
        UIElement content = _mode switch
        {
            EditorMode.Analyze when _panelParagraph >= 0 && _panelParagraph < _readable.Count => ParagraphPanel(ch, _panelParagraph),
            EditorMode.Analyze when _panelParagraph == -2 => OrphanPanel(ch),
            EditorMode.Analyze => ChapterAnalysisPanel(ch),
            EditorMode.Characters when _panelCharacterId is { } id && _book.Characters.FirstOrDefault(c => c.Id == id) is { } who => CharacterPanel(ch, who),
            EditorMode.Characters when _panelCandidate is { } name => CandidatePanel(ch, name),
            EditorMode.Characters => CastPanel(ch),
            EditorMode.Create when _insertAt >= 0 => InsertPanel(ch),
            EditorMode.Create => CreateChapterPanel(ch),
            EditorMode.Plotlines when _panelPlotlineId is { } pid && _book.Plotlines.FirstOrDefault(x => x.Id == pid) is { } thread => PlotlinePanel(ch, thread),
            EditorMode.Plotlines => PlotlinesPanel(ch),
            EditorMode.Listen when _panelQuote != null => QuotePanel(ch, _panelQuote),
            EditorMode.Listen => ListenPanel(ch),
            _ => new TextBlock()
        };
        _side.Content = content;
        if (offset > 0 && content is ScrollViewer sv)
            sv.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => sv.ScrollToVerticalOffset(offset));
    }

    private static ScrollViewer Scroll(UIElement content) =>
        new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    private static StackPanel PanelStack() => new() { Margin = new Thickness(14, 12, 14, 16) };

    private static TextBlock Heading(string text, double size = 16) =>
        new() { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };

    private static TextBlock Hint(string text) =>
        new() { Text = text, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), FontSize = 12 };

    private static Button PanelButton(string text, string tip, Action click, bool primary = false)
    {
        var b = new Button
        {
            Content = text, ToolTip = tip, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 6, 6, 0),
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal
        };
        b.Click += (_, _) => click();
        return b;
    }

    private static TextBlock BackLink(string text, Action back)
    {
        var link = Palette.LinkText("← " + text, back, "Back to the list");
        link.Margin = new Thickness(0, 0, 0, 10);
        return link;
    }

    /// <summary>A titled box in the panel: what it is, what there is, and what can be done.</summary>
    private static Border Card(string title, UIElement body, params Button[] buttons)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(body);
        if (buttons.Length > 0)
        {
            var row = new WrapPanel();
            foreach (var b in buttons) row.Children.Add(b);
            stack.Children.Add(row);
        }
        return new Border
        {
            Background = Palette.RowAltBg, BorderBrush = Palette.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
            Padding = new Thickness(10, 8, 10, 10), Margin = new Thickness(0, 0, 0, 10), Child = stack
        };
    }

    private static TextBlock Body(string text, bool muted = false) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = muted ? Brushes.Gray : Brushes.Black };

    private static string Shorten(string text, int max)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    // ── Analysis: the chapter ─────────────────────────────────────────────────

    private UIElement ChapterAnalysisPanel(Chapter ch)
    {
        var p = PanelStack();
        p.Children.Add(Heading("This chapter"));
        p.Children.Add(Hint("What the AI can make for the whole chapter. Each one is saved under the chapter in the tree, where you can read and edit it in full " +
                            "(Open goes there; Ctrl+3 brings you back here). For one paragraph, click it or the ¶ beside it. " +
                            "Buttons ending in … open a window that shows what will be sent first; the others start straight away, with progress in the status bar."));
        if (AiBusy) p.Children.Add(Body("The AI is working — see the status bar at the bottom. The AI buttons come back when it finishes.", muted: true));
        var items = _book!.ItemsOf(ch.Id).ToList();
        var noArgs = new RoutedEventArgs();

        // Summary
        var summary = items.FirstOrDefault(i => i.Kind == ItemKind.Summary);
        var sumButtons = new List<Button>
        {
            AiButton(summary == null ? "Write the summary (AI)" : "Write it again (AI)",
                     "What happens, in order, and what each character does." + (summary == null ? "" : " Replaces the summary there is (it asks first)."),
                     () =>
                     {
                         if (summary != null && MessageBox.Show(this,
                                 $"Write the summary of \"{ch.Title}\" again?\n\nThe new one replaces the summary there is now, including any changes you made to it. " +
                                 "If the book keeps versions, Versions › This Chapter's History can bring the old one back.",
                                 "Write the summary again", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                         AiSummarize_Click(this, noArgs);
                     }, primary: summary == null)
        };
        if (summary != null) sumButtons.Add(PanelButton("Open", "Open the summary to read or edit it (Ctrl+3 brings you back here)", () => SelectNode(summary)));
        p.Children.Add(Card("✦  Summary",
            summary == null ? Body("Not written yet. The AI writes what happens, in order, and who does what — the other AI tools use it as the chapter's record.", muted: true)
                            : Body(Shorten(summary.Body, 420)),
            sumButtons.ToArray()));

        // Characters
        var who = ch.CharacterIds.Select(id => _book.Characters.FirstOrDefault(c => c.Id == id)).OfType<Character>().ToList();
        var charItem = items.FirstOrDefault(i => i.Kind == ItemKind.CharactersInChapter);
        var charButtons = new List<Button>
        {
            AiButton("Find who is in it (AI)", "Matches the characters' names in the text, then asks the AI who appears and whose point of view it is",
                     () => AiCharactersInChapter_Click(this, noArgs), primary: who.Count == 0),
            PanelButton("See them in the text", "Character mode: every character's name coloured where it appears", () => SetMode(EditorMode.Characters))
        };
        if (charItem != null) charButtons.Add(PanelButton("Open", "Open the chapter's character list", () => SelectNode(charItem)));
        p.Children.Add(Card("👥  Characters",
            who.Count == 0 ? Body("No one is listed for this chapter yet.", muted: true)
                           : Body(string.Join(", ", who.Select(c => c.Name + (ch.PovCharacterId == c.Id ? " (point of view)" : ""))) ),
            charButtons.ToArray()));

        // Plotlines
        var threads = ch.PlotlineIds.Select(id => _book.Plotlines.FirstOrDefault(x => x.Id == id)).OfType<Plotline>().ToList();
        var plotItem = items.FirstOrDefault(i => i.Kind == ItemKind.ChapterPlotlines);
        var plotButtons = new List<Button>
        {
            AiButton("Find its plotlines (AI)", "Asks the AI which threads run through this chapter (and who is in it); known ones are linked, new ones are offered for adding",
                     FindChapterPlotlines, primary: threads.Count == 0),
            PanelButton("Plotlines page", "The chart of every thread across the book", () => SelectNode(PlotlinesHeader))
        };
        if (plotItem != null) plotButtons.Add(PanelButton("Open", "Open the chapter's plotline list", () => SelectNode(plotItem)));
        p.Children.Add(Card("🧵  Plotlines",
            threads.Count == 0 ? Body("No thread is linked to this chapter yet.", muted: true)
                               : Body(string.Join("\n", threads.Select(t => $"{t.Name} — {Palette.RoleWord(t.RoleIn(ch.Id) ?? PlotlineRole.Continuing)}"))),
            plotButtons.ToArray()));

        // Chapter analysis
        var analyses = items.Where(i => i.Kind == ItemKind.Analysis).OrderByDescending(i => i.CreatedUtc).ToList();
        var list = new StackPanel();
        if (analyses.Count == 0) list.Children.Add(Body("None yet. The analysis covers pacing, tension, stakes, point of view and voice, dialogue, continuity and prose habits, each with rewrites you can ask for.", muted: true));
        foreach (var a in analyses.Take(8))
        {
            var target = a;
            var link = Palette.LinkText((a.Resolved ? "✓ " : "") + a.Title + $" — {a.Provider ?? "you"} · {a.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
                                        () => SelectNode(target), "Open this analysis (Ctrl+3 brings you back here)");
            link.Margin = new Thickness(0, 1, 0, 1);
            list.Children.Add(link);
        }
        if (analyses.Count > 8) list.Children.Add(Body($"…and {analyses.Count - 8} more, under the chapter in the tree.", muted: true));
        var aspect = PanelButton("One aspect ▾", "A deeper read of one thing: pacing, tension, stakes, voice, dialogue, continuity or prose habits", () => { });
        aspect.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = aspect, Placement = PlacementMode.Bottom };
            foreach (var name in AiPrompts.AspectTemplates.Keys)
            {
                var mi = new MenuItem { Header = name + "…" };
                var chosen = name;
                mi.Click += (_, _) => AnalyzeAspect(ch, chosen);
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        };
        p.Children.Add(Card("🔍  Chapter analysis", list,
            AiButton("Analyze the chapter (AI)…", "The editor's notes on every aspect, each quoting the text; then the chapter's characters and plotlines are listed",
                     () => AiAnalyze_Click(this, noArgs), primary: analyses.Count == 0),
            aspect));

        // Paragraphs
        var reads = ReadsByParagraph(ch);
        int read = 0, open = 0;
        for (int i = 0; i < _readable.Count; i++)
        {
            if (!reads.TryGetValue(i, out var r)) continue;
            read++;
            if (ParagraphReads.StateOf(r.Select(x => x.Read)) == ParagraphState.Open) open++;
        }
        var legend = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var s in new[] { ParagraphState.None, ParagraphState.Read, ParagraphState.Open, ParagraphState.Done })
        {
            var (fill, word) = StateLook(s);
            legend.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(3), Width = 14, Height = 14, Margin = new Thickness(0, 2, 4, 2),
                                             Child = new TextBlock { Text = "¶", Foreground = Brushes.White, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center } });
            legend.Children.Add(new TextBlock { Text = word, Margin = new Thickness(0, 0, 12, 0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        }
        var paraBody = new StackPanel();
        paraBody.Children.Add(Body(_readable.Count == 0 ? "This chapter has no paragraphs to look at yet."
                                   : $"{read} of {_readable.Count} paragraphs looked at" + (open > 0 ? $" · {open} with suggestions waiting." : ".")));
        paraBody.Children.Add(legend);
        var gone = reads.TryGetValue(-1, out var orphans) ? orphans.Count : 0;
        if (gone > 0)
        {
            var orphanLink = Palette.LinkText($"{gone} read{(gone == 1 ? " is" : "s are")} of paragraphs no longer in the chapter — see {(gone == 1 ? "it" : "them")}",
                                              () => OpenParagraph(-2, scroll: false), "Reads of paragraphs that were rewritten past recognition or deleted");
            orphanLink.Margin = new Thickness(0, 4, 0, 0);
            paraBody.Children.Add(orphanLink);
        }
        var nextButtons = new List<Button>();
        if (_readable.Count > 0)
        {
            int next = Enumerable.Range(0, _readable.Count).FirstOrDefault(i => !reads.ContainsKey(i), -1);
            if (next >= 0) nextButtons.Add(PanelButton("Go to the first one not looked at", "Opens that paragraph here and scrolls the text to it", () => OpenParagraph(next, scroll: true)));
            int waiting = Enumerable.Range(0, _readable.Count).FirstOrDefault(i => reads.TryGetValue(i, out var r) && ParagraphReads.StateOf(r.Select(x => x.Read)) == ParagraphState.Open, -1);
            if (waiting >= 0) nextButtons.Add(PanelButton("Go to suggestions waiting", "Opens the first paragraph with suggestions you have not acted on", () => OpenParagraph(waiting, scroll: true)));
            if (nextButtons.Count == 0) nextButtons.Add(PanelButton("Open paragraph 1", "Opens the first paragraph here; Next › walks through the rest", () => OpenParagraph(0, scroll: true)));
        }
        p.Children.Add(Card("¶  Paragraphs", paraBody, nextButtons.ToArray()));
        return Scroll(p);
    }

    private async void FindChapterPlotlines()
    {
        if (_book == null || _current is not Chapter ch) return;
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var text = ChapterText(ch);
        if (text.Trim().Length < 200)
        {
            MessageBox.Show(this, "Write at least a few paragraphs first — there is not enough text to find threads in.", "Plotlines", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await ExtractEntitiesAsync(ch, text);
    }

    /// <summary>
    /// Where a finished AI result is shown: in Analysis or Character mode, on the chapter it
    /// belongs to (the panel shows it and the author keeps their place); otherwise it opens in the tree.
    /// </summary>
    private void ShowResult(Item item)
    {
        if (_mode != EditorMode.Write && _current is Chapter ch && item.OwnerId == ch.Id)
        {
            CommitCurrent();
            BuildTree(select: ch);
            if (ReferenceEquals(_current, ch) && _side != null) ShowSidePanel();
        }
        else BuildTree(select: item);
    }

    // ── Analysis: one paragraph ───────────────────────────────────────────────

    private UIElement ParagraphPanel(Chapter ch, int index)
    {
        var p = PanelStack();
        p.Children.Add(BackLink("The whole chapter", () => OpenParagraph(-1, scroll: false)));

        var head = new DockPanel();
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        var prev = new Button { Content = "‹ Previous", Padding = new Thickness(8, 2, 8, 2), IsEnabled = index > 0, ToolTip = "The paragraph before" };
        var next = new Button { Content = "Next ›", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0), IsEnabled = index < _readable.Count - 1, ToolTip = "The paragraph after" };
        prev.Click += (_, _) => OpenParagraph(index - 1, scroll: true);
        next.Click += (_, _) => OpenParagraph(index + 1, scroll: true);
        nav.Children.Add(prev); nav.Children.Add(next);
        DockPanel.SetDock(nav, Dock.Right);
        head.Children.Add(nav);
        head.Children.Add(Heading($"¶  Paragraph {index + 1} of {_readable.Count}"));
        p.Children.Add(head);

        var reads = ReadsByParagraph(ch).TryGetValue(index, out var r) ? r : new List<(Item Read, bool Exact)>();
        var (fill, word) = StateLook(ParagraphReads.StateOf(reads.Select(x => x.Read)));
        var chip = Palette.Chip(word, fill);
        ((TextBlock)chip.Child).Foreground = Brushes.White;
        chip.Margin = new Thickness(0, 2, 0, 8);
        p.Children.Add(chip);

        p.Children.Add(new TextBox
        {
            Text = _readable[index].Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Georgia"), FontSize = 14,
            Padding = new Thickness(8), BorderBrush = Palette.Line, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 10)
        });

        p.Children.Add(new TextBlock { Text = "What should this paragraph do? (optional)", FontWeight = FontWeights.SemiBold });
        var request = new TextBox
        {
            Text = _paragraphRequest, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinLines = 2, Padding = new Thickness(4), Margin = new Thickness(0, 2, 0, 2),
            ToolTip = "For example: \"build dread before she opens the door\" or \"a beat of humour\". The AI judges the paragraph against it."
        };
        System.Windows.Automation.AutomationProperties.SetName(request, "What should this paragraph do?");
        request.TextChanged += (_, _) => _paragraphRequest = request.Text;
        p.Children.Add(request);
        p.Children.Add(Hint("The AI judges the paragraph against what you write here. Leave it blank and it works out what the paragraph is reaching for."));

        bool analysed = reads.Any(x => KindOfRead(x.Read) != ParagraphReadKind.Suggestions);
        bool suggested = reads.Any(x => KindOfRead(x.Read) != ParagraphReadKind.Analysis);
        var asks = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        asks.Children.Add(PanelButton(analysed ? "Analyze it again (AI)…" : "Analyze this paragraph (AI)…",
            "An analysis only — no changes: its tone, whether it is consistent with the paragraphs around it, its style, and what it achieves. " +
            "Sends this paragraph, the three before it, the two after and the chapter's summary — not the whole chapter.",
            () => ReadParagraph(ch, index, request.Text.Trim(), suggest: false), primary: true));
        asks.Children.Add(PanelButton(suggested ? "More suggestions (AI)…" : "Suggest changes (AI)…",
            "Changes, additions and readability fixes for this paragraph, each one you can apply to the chapter. " +
            "Sends this paragraph, the three before it, the two after and the chapter's summary — not the whole chapter.",
            () => ReadParagraph(ch, index, request.Text.Trim(), suggest: true), primary: true));
        p.Children.Add(asks);
        p.Children.Add(Hint("Analyze says how the paragraph reads: tone, consistency, style, what it achieves. Suggest offers changes, additions and readability fixes you can apply. Use either, or both."));

        if (reads.Count == 0)
        {
            p.Children.Add(Hint("\nNothing yet. After an analysis the ¶ turns blue; suggestions waiting turn it amber; Apply puts one into the chapter, and the ¶ turns green when you have dealt with them."));
            return Scroll(p);
        }

        foreach (var (read, exact) in reads)
        {
            _readSig[read.Id] = Sig(read);
            var kind = KindOfRead(read);
            var box = new StackPanel();
            box.Children.Add(new TextBlock
            {
                Text = $"{read.Provider ?? "AI"} · {read.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}" + (exact ? "" : "  ·  you have changed the paragraph since this was made"),
                Foreground = exact ? Brushes.Gray : StateOpen, FontSize = 11, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap
            });
            var readText = kind == ParagraphReadKind.Analysis ? read.Body.Trim() : ParagraphReads.ReadPart(read.Body);
            if (readText.Length > 0) box.Children.Add(Body(readText));
            else if (kind == ParagraphReadKind.Combined) box.Children.Add(Body("(The reply had no READ part — see the rewrites below.)", muted: true));
            if (read.Suggestions.Count == 0 && kind != ParagraphReadKind.Analysis && read.Body.Contains("SUGGESTION", StringComparison.OrdinalIgnoreCase)) read.Suggestions = SuggestionParser.Parse(read.Body);
            if (kind == ParagraphReadKind.Suggestions && read.Suggestions.Count == 0)
                box.Children.Add(Body("No suggestion could be read from the reply. Its text is below.\n\n" + read.Body.Trim(), muted: true));
            int n = 0;
            if (read.Suggestions.Count > 0)
            {
                if (kind == ParagraphReadKind.Combined) box.Children.Add(new TextBlock { Text = "Rewrites", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
                foreach (var group in SuggestionsEditor.GroupByPassage(read.Suggestions))
                {
                    n++;
                    box.Children.Add(SuggestionsEditor.PassageBlock(read, ch, group, this, $"Passage {n}", $"P{n}", showChapterTitle: false));
                }
            }
            var footer = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var noun = kind switch { ParagraphReadKind.Analysis => "analysis", ParagraphReadKind.Suggestions => "set of suggestions", _ => "read" };
            var done = new CheckBox { Content = "Dealt with", IsChecked = read.Resolved, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 12, 0),
                                      ToolTip = $"Tick when you have done what you mean to with this {noun}; the ¶ turns green" };
            var target = read;
            done.Click += (_, _) =>
            {
                target.Resolved = done.IsChecked == true;
                target.ResolvedUtc = target.Resolved ? DateTime.UtcNow : null;
                MarkDirty();
                RefreshMarks();
                ShowSidePanel();
            };
            footer.Children.Add(done);
            footer.Children.Add(PanelButton($"Delete this {noun}", $"Removes this {noun} (the chapter text is not changed)", () =>
            {
                if (MessageBox.Show(this, $"Delete this {noun} of the paragraph? The chapter text stays as it is.", "Delete",
                                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _book!.Items.Remove(target);
                MarkDirty();
                RefreshMarks();
                ShowSidePanel();
            }));
            box.Children.Add(footer);
            p.Children.Add(Card(kind switch { ParagraphReadKind.Analysis => "Analysis", ParagraphReadKind.Suggestions => "Suggestions", _ => "Read" }, box));
        }
        return Scroll(p);
    }

    private enum ParagraphReadKind { Analysis, Suggestions, Combined }

    /// <summary>Which of the paragraph's two AI asks made this item; reads from the first build (read + rewrites in one) are Combined.</summary>
    private static ParagraphReadKind KindOfRead(Item read) => read.PromptName switch
    {
        AiPrompts.ParagraphAnalyze => ParagraphReadKind.Analysis,
        AiPrompts.ParagraphSuggest => ParagraphReadKind.Suggestions,
        _                          => ParagraphReadKind.Combined
    };

    /// <summary>Sends one paragraph, with a few around it and the chapter's summary, for an analysis or for suggestions.</summary>
    private void ReadParagraph(Chapter ch, int index, string request, bool suggest)
    {
        if (_book == null || index < 0 || index >= _readable.Count) return;
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var text = _readable[index].Text;
        string Join(IEnumerable<string> xs) { var s = string.Join("\n\n", xs); return s.Length > 0 ? s : "(none)"; }
        var values = new
        {
            book = _book.Title, chapter = ch.Title,
            summary = _book.SummaryText(ch).Length > 0 ? _book.SummaryText(ch) : "(no summary yet)",
            before = Join(_readable.Take(index).TakeLast(3).Select(x => x.Text)),
            after = Join(_readable.Skip(index + 1).Take(2).Select(x => x.Text)),
            paragraph = text, number = index + 1, count = _readable.Count,
            request = request.Length > 0 ? request : "(not stated)",
            chapter_intent = IntentOf(ch)
        };
        var prompt = suggest ? AiPrompts.ParagraphSuggest : AiPrompts.ParagraphAnalyze;
        var dlg = new AiRunWindow(_ai, _book, ch.Id, ItemKind.ParagraphAnalysis,
            suggest ? $"Suggest changes to paragraph {index + 1} — \"{ch.Title}\"" : $"Analyze paragraph {index + 1} — \"{ch.Title}\"",
            suggest ? $"¶ {index + 1} suggestions" : $"¶ {index + 1} analysis", prompt,
            (suggest
                ? "Changes, additions and readability fixes for this paragraph, each one you can apply to the chapter. "
                : "An analysis of this paragraph — its tone, consistency with what is around it, style, and what it achieves. It changes nothing. ") +
            $"It is shown beside the paragraph in Analysis mode and kept with the chapter. The prompt is \"{prompt}\" in AI › Prompt Library.",
            provider => _ai.Prompts.Get(prompt).Bind(values, provider), ConfirmSend,
            nextLabel: "Next: see it beside the paragraph →") { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        foreach (var item in dlg.Created)
        {
            item.Passage = text;
            item.ParagraphIndex = index;
            item.Suggestions = suggest ? SuggestionParser.Parse(item.Body) : new();
            foreach (var s in item.Suggestions) { s.Provider = item.Provider; s.Model = item.Model; }
        }
        MarkDirty();
        _panelParagraph = index;
        RefreshMarks();
        ShowSidePanel();
        UpdateStatus(suggest ? $"Paragraph {index + 1}: {dlg.Created.Sum(i => i.Suggestions.Count)} suggestion(s) beside it."
                             : $"Paragraph {index + 1} analysed: the analysis is beside it.");
    }

    // ── Characters: the cast of the chapter ───────────────────────────────────

    private UIElement CastPanel(Chapter ch)
    {
        var p = PanelStack();
        p.Children.Add(Heading("Characters in this chapter"));
        p.Children.Add(Hint("Each character is coloured in the text wherever it uses their name or an \"also called\". Click a name — here or in the text — to open their form. " +
                            "Names with a dotted underline are not characters yet."));

        var counts = _nameMarks.Where(m => m.Target is Character).GroupBy(m => (Character)m.Target).ToDictionary(g => g.Key, g => g.Count());
        var named = counts.Keys.OrderByDescending(c => counts[c]).ToList();
        if (_book!.Characters.Count == 0)
            p.Children.Add(Body("This book has no characters yet. Add one from the names below, or with Book › Add Character.", muted: true));
        else if (named.Count == 0)
            p.Children.Add(Body("No character is named in this chapter. A character the text calls something else can be given that name under \"Also called\" on their form.", muted: true));

        foreach (var c in named)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(Swatch(Solid(c)));
            var who = c;
            var link = Palette.LinkText(c.Name, () => OpenName(who), "Open them here: in this chapter, in the book, and their form");
            link.FontWeight = FontWeights.SemiBold;
            row.Children.Add(link);
            var info = $"   {counts[c]} mention{(counts[c] == 1 ? "" : "s")}";
            row.Children.Add(new TextBlock { Text = info, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center });
            if (ch.PovCharacterId == c.Id) row.Children.Add(Padded(Palette.Chip("point of view", Palette.PovBg)));
            if (!ch.CharacterIds.Contains(c.Id)) row.Children.Add(Padded(Palette.Chip("not on the chapter's list", Palette.UnlinkedBg, "Named in the text, but the chapter does not list them yet — the button below adds them")));
            p.Children.Add(row);
        }

        var missing = named.Where(c => !ch.CharacterIds.Contains(c.Id)).ToList();
        if (missing.Count > 0)
            p.Children.Add(PanelButton($"Add {(missing.Count == 1 ? missing[0].Name : missing.Count + " characters")} to this chapter's list",
                "The chapter's list is what the Characters chart and the AI use for who is in it", () =>
                {
                    foreach (var c in missing) ch.CharacterIds.Add(c.Id);
                    SyncCharactersItem(ch);
                    SeedCharacterRoles();
                    MarkDirty();
                    UpdateChapterLinks(ch);
                    ShowSidePanel();
                    UpdateStatus($"{missing.Count} character(s) added to the list for \"{ch.Title}\".");
                }, primary: true));

        var listedNotNamed = ch.CharacterIds.Select(id => _book.Characters.FirstOrDefault(c => c.Id == id)).OfType<Character>().Where(c => !counts.ContainsKey(c)).ToList();
        if (listedNotNamed.Count > 0)
        {
            p.Children.Add(new TextBlock { Text = "On the chapter's list, but not named in the text", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 2) });
            var wrap = new WrapPanel();
            foreach (var c in listedNotNamed)
            {
                var who = c;
                var link = Palette.LinkText(c.Name, () => OpenName(who), "Open them here — perhaps the text calls them something else; add it under Also called");
                link.Margin = new Thickness(0, 0, 12, 0);
                wrap.Children.Add(link);
            }
            p.Children.Add(wrap);
        }

        p.Children.Add(new TextBlock { Text = "Not characters yet", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 2) });
        if (_candidates.Count == 0) p.Children.Add(Body("No other names stand out in this chapter.", muted: true));
        else p.Children.Add(Hint("Capitalised names in the text that no character answers to. Some will be places or things — click one to look at it and decide."));
        foreach (var cand in _candidates.Take(30))
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var name = cand.Name;
            row.Children.Add(Palette.LinkText(name, () => { _panelCandidate = name; _panelCharacterId = null; _mentionAt = -1; RefreshMarks(); ShowSidePanel(); }, "Look at it, and add it as a character if it is one"));
            var likely = cand.LikelyCharacterId is { } lid ? _book.Characters.FirstOrDefault(c => c.Id == lid) : null;
            row.Children.Add(new TextBlock { Text = $"   {cand.Count}×" + (likely != null ? $" — perhaps {likely.Name}?" : ""), Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(row);
        }

        if (_book.NotNames.Count > 0)
        {
            p.Children.Add(new TextBlock { Text = "Not marked (you said these are not characters)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 2) });
            var wrap = new WrapPanel();
            foreach (var n in _book.NotNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var name = n;
                var link = Palette.LinkText(name + " (mark again)", () =>
                {
                    _book.NotNames.RemoveAll(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
                    MarkDirty(); RefreshMarks(); ShowSidePanel(keepScroll: true);
                }, "Mark this name again as a possible character");
                link.Margin = new Thickness(0, 0, 12, 0);
                wrap.Children.Add(link);
            }
            p.Children.Add(wrap);
        }

        var all = PanelButton("All characters (Characters page)", "The cast of the whole book, with the chart of who is in which chapter", () => SelectNode(CharactersHeader));
        all.Margin = new Thickness(0, 16, 0, 0);
        all.HorizontalAlignment = HorizontalAlignment.Left;
        p.Children.Add(all);
        return Scroll(p);
    }

    private static Border Swatch(Brush fill) =>
        new() { Background = fill, Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Previous / Next buttons that step through where a name appears in the text.</summary>
    private UIElement MentionNav(Func<NameMark, bool> which, string what)
    {
        var marks = _nameMarks.Where(which).ToList();
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        var label = new TextBlock { Text = marks.Count == 0 ? $"{what} is not named in this chapter." : $"Named {marks.Count} time{(marks.Count == 1 ? "" : "s")} in this chapter.",
                                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 10, 0) };
        row.Children.Add(label);
        if (marks.Count == 0) return row;
        void Go(int step)
        {
            _mentionAt = ((_mentionAt < 0 ? (step > 0 ? -1 : 0) : _mentionAt) + step + marks.Count) % marks.Count;
            var m = marks[_mentionAt];
            _rtb?.Selection.Select(m.Start, m.End);
            _overlay?.SetCurrent(m);
            ScrollTo(m.Start);
            label.Text = $"Mention {_mentionAt + 1} of {marks.Count}.";
        }
        var prev = PanelButton("‹ Previous", "Select the mention before this one in the text", () => Go(-1));
        var next = PanelButton("Next ›", "Select the next mention in the text", () => Go(+1));
        row.Children.Add(prev); row.Children.Add(next);
        return row;
    }

    // ── Characters: one character's form ──────────────────────────────────────

    private UIElement CharacterPanel(Chapter ch, Character c)
    {
        var dock = new DockPanel();
        var top = PanelStack();
        top.Margin = new Thickness(14, 12, 14, 0);
        top.Children.Add(BackLink("Characters in this chapter", () => { _panelCharacterId = null; _mentionAt = -1; RefreshMarks(); ShowSidePanel(); }));
        var nameRow = new DockPanel();
        nameRow.Children.Add(Swatch(Solid(c)));
        var name = new TextBox { Text = c.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, ToolTip = "Their name — change it here or on their page" };
        System.Windows.Automation.AutomationProperties.SetName(name, "Character name");
        name.TextChanged += (_, _) => { if (name.Text.Trim().Length == 0) return; c.Name = name.Text.Trim(); MarkDirty(); };
        name.LostFocus += (_, _) => { RefreshTreeTexts(); RefreshMarks(); };
        nameRow.Children.Add(name);
        top.Children.Add(nameRow);
        top.Children.Add(MentionNav(m => ReferenceEquals(m.Target, c), c.Name));
        var row = new WrapPanel();
        row.Children.Add(PanelButton("Open their full page", "Their page in the tree, with the chapters they are in", () => SelectNode(c)));
        if (!ch.CharacterIds.Contains(c.Id))
            row.Children.Add(PanelButton("Add to this chapter's list", "The chapter's list is what the Characters chart and the AI use for who is in it", () =>
            {
                ch.CharacterIds.Add(c.Id);
                SyncCharactersItem(ch);
                SeedCharacterRoles();
                MarkDirty();
                UpdateChapterLinks(ch);
                ShowSidePanel();
            }));
        top.Children.Add(row);
        top.Children.Add(CharacterViewSwitch(c));
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(_characterView switch
        {
            CharacterView.Book => Scroll(BookCharacterView(c, inPanel: true)),
            CharacterView.Form => BuildCharacterPage(c, inPanel: true),
            _                  => ChapterCharacterView(ch, c)
        });
        return dock;
    }

    // ── Characters: a name that is not a character yet ───────────────────────

    private UIElement CandidatePanel(Chapter ch, string name)
    {
        var p = PanelStack();
        p.Children.Add(BackLink("Characters in this chapter", () => { _panelCandidate = null; _mentionAt = -1; RefreshMarks(); ShowSidePanel(); }));
        p.Children.Add(Heading($"\"{name}\""));
        p.Children.Add(Hint("Not a character yet. If it is someone in the story, add them; if it is a place or a thing, leave it — nothing changes unless you add it."));
        p.Children.Add(MentionNav(m => m.Target is NameCandidate n && n.Name == name, $"\"{name}\""));

        var cand = _candidates.FirstOrDefault(x => x.Name == name);
        if (cand?.LikelyCharacterId is { } lid && _book!.Characters.FirstOrDefault(x => x.Id == lid) is { } likely)
        {
            p.Children.Add(Card($"Perhaps this is {likely.Name}",
                Body($"\"{name}\" is part of {likely.Name}'s name. Add it to what they are also called, and every \"{name}\" in the book is coloured as them."),
                PanelButton($"Add \"{name}\" as another name for {likely.Name}", "Adds it to their Also called", () =>
                {
                    likely.Aliases = Bullets.Add(likely.Aliases, name, fromAi: false);
                    MarkDirty();
                    _panelCandidate = null; _panelCharacterId = likely.Id; _mentionAt = -1;
                    RefreshMarks();
                    ShowSidePanel();
                    UpdateStatus($"\"{name}\" added to {likely.Name}'s other names.");
                }, primary: true)));
        }

        p.Children.Add(Card("Not a character",
            Body($"A place, a boat, a thing? Stop marking \"{name}\" in this book. The Characters panel lists it under \"Not marked\", where you can mark it again."),
            PanelButton("Not a character — stop marking it", "Removes the dotted underline from this name everywhere in the book", () =>
            {
                if (!_book!.NotNames.Contains(name, StringComparer.OrdinalIgnoreCase)) _book.NotNames.Add(name);
                MarkDirty();
                _panelCandidate = null; _mentionAt = -1;
                RefreshMarks();
                ShowSidePanel();
                UpdateStatus($"\"{name}\" is no longer marked as a possible character.");
            })));

        p.Children.Add(Card("A new character",
            Body($"Adds {name} to the book's characters and to this chapter's list, then opens their form here to fill in."),
            PanelButton($"Add {name} as a character", "A new character with this name", () => AddCandidateAsCharacter(ch, name), primary: cand?.LikelyCharacterId == null)));

        if (_book!.Characters.Count > 0)
        {
            var pick = new ComboBox { ItemsSource = _book.Characters, DisplayMemberPath = nameof(Character.Name), MinWidth = 200, Margin = new Thickness(0, 6, 6, 0),
                                      ToolTip = "The character the text means by this name",
                                      SelectedItem = _book.Characters.FirstOrDefault(c => c.Id == cand?.LikelyCharacterId) };
            System.Windows.Automation.AutomationProperties.SetName(pick, "Character this name belongs to");
            var add = PanelButton("Add as their other name", "Adds the name to this character's Also called", () =>
            {
                if (pick.SelectedItem is not Character who) { UpdateStatus("Pick the character first."); return; }
                who.Aliases = Bullets.Add(who.Aliases, name, fromAi: false);
                MarkDirty();
                _panelCandidate = null; _panelCharacterId = who.Id; _mentionAt = -1;
                RefreshMarks();
                ShowSidePanel();
                UpdateStatus($"\"{name}\" added to {who.Name}'s other names.");
            });
            ToolTipService.SetShowOnDisabled(add, true);
            void Ready() { add.IsEnabled = pick.SelectedItem != null; add.ToolTip = add.IsEnabled ? "Adds the name to this character's Also called" : "Pick the character first"; }
            pick.SelectionChanged += (_, _) => Ready();
            Ready();
            var body = new WrapPanel();
            body.Children.Add(pick); body.Children.Add(add);
            var stack = new StackPanel();
            stack.Children.Add(Body($"If the text means a character you already have by \"{name}\" — a nickname, a title — pick them:"));
            stack.Children.Add(body);
            p.Children.Add(Card("Another name for someone", stack));
        }
        return Scroll(p);
    }

    private void AddCandidateAsCharacter(Chapter ch, string name)
    {
        if (_book == null) return;
        var c = new Character { Name = name };
        _book.Characters.Add(c);
        if (!ch.CharacterIds.Contains(c.Id)) ch.CharacterIds.Add(c.Id);
        SyncCharactersItem(ch);
        SeedCharacterRoles();
        MarkDirty();
        _panelCandidate = null; _panelCharacterId = c.Id; _mentionAt = -1;
        CommitCurrent();
        BuildTree(select: ch);                                     // the new character appears in the tree; the chapter stays open
        if (ReferenceEquals(_current, ch)) { RefreshMarks(); ShowSidePanel(); }
        UpdateStatus($"{name} added as a character and to this chapter's list — fill in their form on the right.");
    }

    /// <summary>A panel button that asks the AI: greyed out, saying why, while another AI run is going.</summary>
    private Button AiButton(string text, string tip, Action click, bool primary = false)
    {
        var b = PanelButton(text, tip, click, primary);
        b.Tag = tip;
        ToolTipService.SetShowOnDisabled(b, true);
        SetAiButton(b);
        _aiButtons.RemoveAll(x => !x.TryGetTarget(out _));
        _aiButtons.Add(new WeakReference<Button>(b));
        return b;
    }

    private readonly List<WeakReference<Button>> _aiButtons = new();

    private void SetAiButton(Button b)
    {
        b.IsEnabled = !AiBusy;
        b.ToolTip = AiBusy ? "The AI is working — see the status bar. This comes back when it finishes." : b.Tag as string;
    }

    /// <summary>The panel shows the AI buttons as they should be: greyed while a run goes, back when it ends.</summary>
    private void RefreshPanelForAi()
    {
        if (_mode != EditorMode.Write && _side != null) ShowSidePanel(keepScroll: true);
        foreach (var w in _aiButtons) if (w.TryGetTarget(out var b)) SetAiButton(b);   // and those on a page, such as a character's
    }

    private static string Sig(Item read) =>
        read.Passage + "|" + read.Resolved + "|" + string.Join(",", read.Suggestions.Select(s => s.Status));

    /// <summary>
    /// A paragraph read changed (a rewrite applied or undone, marked, resolved). A rewrite the
    /// author put in through the read is part of the paragraph now: the read follows it, so it
    /// is not shown as out of date or lost. The panel is redrawn only when something it shows changed,
    /// so typing in "Your rewrite" keeps its place.
    /// </summary>
    private void OnParagraphReadChanged(Item item)
    {
        if (_rtb == null || _current is not Chapter ch || item.OwnerId != ch.Id) return;
        var before = _readable.Select(r => r.Text).ToList();
        var (i, _) = ParagraphReads.Match(before, item.Passage, item.ParagraphIndex);
        var now = ReadableParagraphs();
        if (i >= 0 && i < now.Count && now.Count == before.Count && now[i].Text != before[i])
        {
            item.Passage = now[i].Text;
            item.ParagraphIndex = i;
        }
        var sig = Sig(item);
        if (_readSig.TryGetValue(item.Id, out var old) && old == sig) return;
        _readSig[item.Id] = sig;
        RefreshMarks();
        ShowSidePanel(keepScroll: true);
    }

    /// <summary>Reads whose paragraph is no longer in the chapter: what was read and what was said, to keep or delete.</summary>
    private UIElement OrphanPanel(Chapter ch)
    {
        var p = PanelStack();
        p.Children.Add(BackLink("The whole chapter", () => OpenParagraph(-1, scroll: false)));
        p.Children.Add(Heading("Reads of paragraphs no longer in the chapter"));
        p.Children.Add(Hint("These paragraphs were rewritten past recognition or deleted. Their rewrites cannot be applied any more; read what was said, then delete the ones you are done with."));
        var orphans = ReadsByParagraph(ch).TryGetValue(-1, out var list) ? list : new();
        if (orphans.Count == 0) p.Children.Add(Body("None — every read belongs to a paragraph in the chapter.", muted: true));
        foreach (var (read, _) in orphans)
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = $"{read.Provider ?? "AI"} · {read.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}", Foreground = Brushes.Gray, FontSize = 11 });
            box.Children.Add(new TextBlock { Text = "The paragraph as it was read", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 6, 0, 2) });
            box.Children.Add(new TextBox { Text = read.Passage, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Georgia"), FontSize = 13, Padding = new Thickness(6), MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var said = ParagraphReads.ReadPart(read.Body);
            if (said.Length > 0) box.Children.Add(new TextBlock { Text = said, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            if (read.Suggestions.Count > 0) box.Children.Add(Body($"{read.Suggestions.Count} rewrite(s), {read.Suggestions.Count(s => s.Status == SuggestionStatus.Applied)} applied.", muted: true));
            var target = read;
            box.Children.Add(PanelButton("Delete this read", "Removes this read and its rewrites (the chapter text is not changed)", () =>
            {
                if (MessageBox.Show(this, "Delete this read? The chapter text stays as it is.", "Delete read", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                _book!.Items.Remove(target);
                MarkDirty(); RefreshMarks(); ShowSidePanel();
            }));
            p.Children.Add(Card("Read", box));
        }
        return Scroll(p);
    }

    /// <summary>The character a character-page button acts on: the page open in the tree, or the form open beside a chapter.</summary>
    private Character? CharacterInView() =>
        _current as Character ??
        (_current is Chapter && _mode == EditorMode.Characters && _panelCharacterId is { } id ? _book?.Characters.FirstOrDefault(c => c.Id == id) : null);
}
