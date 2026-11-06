using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Export;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace AuthorPlus.App;

/// <summary>
/// Create mode (Ron, 2026-11-06; plan phase 8): the author clicks a + between paragraphs (or the one
/// at the end), says what the new text must do — the action — and how — the style — and the AI
/// writes it. The draft is shown beside the text to read and edit before it goes in, and can be
/// taken out again. With no + chosen, the panel writes a whole new chapter after this one. Styles
/// the author uses are kept (<see cref="WritingStyles"/>) and offered again under the style box.
/// </summary>
public partial class MainWindow
{
    private int _insertAt = -1;                         // Create: the + chosen (before paragraph i; = count: at the end)
    private List<Paragraph> _createParas = new();       // Create: the paragraphs the + marks sit between
    private string _createAction = "", _createStyle = "", _createLength = "One paragraph";
    private string? _draft;                             // Create: what the AI wrote, not yet put in
    private List<Paragraph>? _inserted;                 // Create: what was last put in, so it can be taken out again (until the chapter is left)
    private List<string>? _insertedTexts;               // … and its words, to find it again when the editor is rebuilt (a mode switch)
    private bool _showingPutIn;                         // Create: the panel shows "New text is in the chapter"
    private string? _draftAsWritten;                    // Create: the draft as the AI wrote it, to know whether the author changed it
    private string _chapterTitle = "", _chapterAction = "", _chapterStyle = "", _chapterLength = "About 2,000 words";
    private readonly WritingStyles _styles = new();

    private static readonly string[] PassageLengths = { "A sentence or two", "One paragraph", "Two or three paragraphs", "A scene (about 500 words)" };
    private static readonly string[] ChapterLengths = { "About 1,000 words", "About 2,000 words", "About 3,000 words" };

    /// <summary>The paragraphs the + marks go between: every paragraph with text in it.</summary>
    private List<Paragraph> CreateParagraphs() =>
        _rtb == null ? new() : AllParagraphs(_rtb.Document.Blocks).Where(p => TextOf(p).Length > 0).ToList();

    private void ShowCreateMarks()
    {
        _createParas = CreateParagraphs();
        if (_insertAt > _createParas.Count) _insertAt = -1;
        FindInserted();
        int start = _inserted != null ? _createParas.IndexOf(_inserted[0]) : -1;
        _overlay?.ShowInserts(_createParas, _showingPutIn ? -1 : _insertAt, start, _inserted?.Count ?? 0);
    }

    /// <summary>
    /// What was last put in, in the document as it is now: the same paragraphs, or — after the editor was
    /// rebuilt by a mode switch — the run of paragraphs with the same words. Forgotten when it is not there any more.
    /// </summary>
    private void FindInserted()
    {
        if (_insertedTexts == null) { _inserted = null; return; }
        if (_inserted != null && _inserted.All(_createParas.Contains)) return;
        var texts = _createParas.Select(TextOf).ToList();
        int n = _insertedTexts.Count;
        for (int i = 0; i + n <= texts.Count; i++)
            if (Enumerable.Range(0, n).All(k => texts[i + k] == _insertedTexts[k])) { _inserted = _createParas.GetRange(i, n); return; }
        _inserted = null; _insertedTexts = null; _showingPutIn = false;
    }

    private void OpenInsert(int index)
    {
        if (index != _insertAt && !LetDraftGo()) return;
        if (index != _insertAt) { _draft = null; _draftAsWritten = null; }
        _showingPutIn = false;
        _insertAt = index;
        RefreshMarks();
        ShowSidePanel();
    }

    private string WhereLabel(int index) =>
        index == 0 && _createParas.Count > 0 ? "before the first paragraph"
        : index >= _createParas.Count ? "at the end of the chapter"
        : "between these two paragraphs";

    /// <summary>True when the draft can be thrown away: there is none, it is as the AI wrote it, or the author says so.</summary>
    private bool LetDraftGo()
    {
        if (_draft == null || _draft == _draftAsWritten) return true;
        return MessageBox.Show(this, "You have changed the AI's text and not put it in. Throw your changes away?", "Create",
                               MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>"Take it out again" for the text last put in, while it is still in the chapter.</summary>
    private Border? TakeOutCard()
    {
        if (_inserted == null) return null;
        return Card("Last text put in", Body($"{_inserted.Count} paragraph{(_inserted.Count == 1 ? "" : "s")}, with a green band in the text. It can be taken out until you leave this chapter."),
            PanelButton("Take it out again", "Removes that text from the chapter, as if it had never been put in", TakeOut),
            PanelButton("Show it", "Scrolls the text to it", () => ScrollTo(_inserted[0].ContentStart)));
    }

    // ── The panel: new text at a + ───────────────────────────────────────────

    private UIElement InsertPanel(Chapter ch)
    {
        var p = PanelStack();
        p.Children.Add(BackLink("Create — the whole chapter", () =>
        {
            if (!LetDraftGo()) return;
            _insertAt = -1; _draft = null; _draftAsWritten = null; _showingPutIn = false; RefreshMarks(); ShowSidePanel();
        }));

        if (_showingPutIn && _inserted != null)
        {
            p.Children.Add(Heading("✚  New text is in the chapter"));
            p.Children.Add(Card("Put in", Body($"{_inserted.Count} paragraph{(_inserted.Count == 1 ? "" : "s")}, with a green band in the text. Read it there; to change a word, " +
                                                 "double-click it to go to Write mode. It can be taken out again until you leave this chapter."),
                PanelButton("Take it out again", "Removes the new text from the chapter, as if it had never been put in", TakeOut),
                PanelButton("Write more after it", "A new + just after the new text", () => OpenInsert(_createParas.IndexOf(_inserted[^1]) + 1), primary: true)));
            return Scroll(p);
        }

        p.Children.Add(Heading($"✚  New text {WhereLabel(_insertAt)}"));
        var nav = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        nav.Children.Add(PanelButton("‹ Earlier place", "Move this place up one paragraph (the + marks can also be clicked)", () => { OpenInsert(_insertAt - 1); ScrollToPlace(); }));
        ((Button)nav.Children[^1]).IsEnabled = _insertAt > 0;
        nav.Children.Add(PanelButton("Later place ›", "Move this place down one paragraph", () => { OpenInsert(_insertAt + 1); ScrollToPlace(); }));
        ((Button)nav.Children[^1]).IsEnabled = _insertAt < _createParas.Count;
        p.Children.Add(nav);
        var before = _insertAt > 0 && _insertAt - 1 < _createParas.Count ? TextOf(_createParas[_insertAt - 1]) : "";
        var after = _insertAt < _createParas.Count ? TextOf(_createParas[_insertAt]) : "";
        if (before.Length > 0) p.Children.Add(Context("Just before:", before));
        if (after.Length > 0) p.Children.Add(Context("Just after:", after));
        if (TakeOutCard() is { } takeOut) { takeOut.Margin = new Thickness(0, 6, 0, 4); p.Children.Add(takeOut); }

        p.Children.Add(Label("Action — what should happen, or what should the reader take from it?"));
        var action = Box(_createAction, v => _createAction = v, "For example: Greg woke up feeling very much his age of 75 years", "Action");
        p.Children.Add(action);
        p.Children.Add(Label("Style — how should it be written?"));
        p.Children.Add(StyleBox(() => _createStyle, v => _createStyle = v, "For example: full description of the challenges of old age, so the reader feels them"));
        p.Children.Add(Label("Length"));
        p.Children.Add(Choice(PassageLengths, _createLength, v => _createLength = v));
        p.Children.Add(AiButton(_draft == null ? "Write it (AI)…" : "Write it again (AI)…",
            "Sends the action, the style, the paragraphs around this place and the chapter's summary; the AI writes the new text in the voice of the text around it. You read it here before it goes in.",
            () => WriteHere(ch), primary: _draft == null));

        if (_draft != null)
        {
            var draft = new TextBox
            {
                Text = _draft, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, FontFamily = new FontFamily("Georgia"), FontSize = 14,
                Padding = new Thickness(8), MinHeight = 160, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                ToolTip = "What the AI wrote. Change anything you like before it goes in; a blank line starts a new paragraph."
            };
            System.Windows.Automation.AutomationProperties.SetName(draft, "What the AI wrote");
            draft.TextChanged += (_, _) => _draft = draft.Text;
            System.Windows.Automation.AutomationProperties.SetHelpText(draft, "Change anything before it goes in; a blank line starts a new paragraph.");
            p.Children.Add(new Border { Height = 10 });
            p.Children.Add(Card("What the AI wrote", draft,
                PanelButton("Put it in the chapter", $"Puts this text into the chapter {WhereLabel(_insertAt)}; Take it out again undoes it", () => PutIn(ch), primary: true),
                PanelButton("Discard", "Throws this text away; the chapter is not changed", () => { if (!LetDraftGo()) return; _draft = null; _draftAsWritten = null; ShowSidePanel(); })));
        }
        else p.Children.Add(Hint("\nThe AI writes in the voice of the paragraphs around this place. You read what it wrote here, change it if you like, and only then put it in."));
        return Scroll(p);
    }

    private static UIElement Context(string label, string text)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
        t.Inlines.Add(new Run(label + " ") { FontWeight = FontWeights.SemiBold });
        t.Inlines.Add(new Run(Shorten(text, 220)) { FontStyle = FontStyles.Italic });
        return t;
    }

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 2) };

    private static TextBox Box(string text, Action<string> set, string tip, string name, int lines = 2)
    {
        var box = new TextBox { Text = text, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinLines = lines, Padding = new Thickness(4), ToolTip = tip };
        System.Windows.Automation.AutomationProperties.SetName(box, name);
        box.TextChanged += (_, _) => set(box.Text);
        return box;
    }

    private static ComboBox Choice(string[] options, string current, Action<string> set)
    {
        var combo = new ComboBox { ItemsSource = options, SelectedItem = options.Contains(current) ? current : options[1 % options.Length], HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 200,
                                   ToolTip = "About how much the AI should write" };
        System.Windows.Automation.AutomationProperties.SetName(combo, "Length");
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s) set(s); };
        return combo;
    }

    /// <summary>The style box, with "Choose a saved style ▾" under it: the styles used before, most recent first.</summary>
    private UIElement StyleBox(Func<string> get, Action<string> set, string tip)
    {
        var stack = new StackPanel();
        var box = Box(get(), set, tip + ". A style you use is kept, to choose again.", "Style");
        stack.Children.Add(box);
        TextBlock? link = null;
        link = Palette.LinkText("Choose a saved style ▾", () =>
        {
            var menu = new ContextMenu { PlacementTarget = link, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            var saved = _styles.Load();
            if (saved.Count == 0) menu.Items.Add(new MenuItem { Header = "No saved styles yet — a style you write here is kept when you use it", IsEnabled = false });
            foreach (var s in saved)
            {
                var item = new MenuItem { Header = Shorten(s, 90), ToolTip = s };
                var chosen = s;
                item.Click += (_, _) => { box.Text = chosen; box.Focus(); box.CaretIndex = box.Text.Length; };
                menu.Items.Add(item);
            }
            if (saved.Count > 0)
            {
                menu.Items.Add(new Separator());
                var forget = new MenuItem { Header = "Forget a style" };
                foreach (var s in saved)
                {
                    var gone = s;
                    var item = new MenuItem { Header = Shorten(s, 90), ToolTip = "Take this style off the list" };
                    item.Click += (_, _) => { _styles.Forget(gone); UpdateStatus("That style is off the list."); };
                    forget.Items.Add(item);
                }
                menu.Items.Add(forget);
            }
            menu.IsOpen = true;
        }, "The styles you have used before — pick one to put it in the box");
        link.HorizontalAlignment = HorizontalAlignment.Right;
        link.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(link);
        return stack;
    }

    private string LastParagraphs(IEnumerable<Paragraph> paras, int take)
    {
        var s = string.Join("\n\n", paras.Select(TextOf).Where(t => t.Length > 0).TakeLast(take));
        return s.Length > 0 ? s : "(none)";
    }

    private void WriteHere(Chapter ch)
    {
        if (_book == null || _insertAt < 0) return;
        if (_createAction.Trim().Length == 0)
        {
            MessageBox.Show(this, "Say what the new text should do first — the Action box. For example: \"Greg woke up feeling very much his age of 75 years\".",
                            "Create", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!LetDraftGo()) return;
        if (!EnsureAiOrExplain()) return;
        var values = new
        {
            book = _book.Title, chapter = ch.Title,
            summary = _book.SummaryText(ch).Length > 0 ? _book.SummaryText(ch) : "(no summary yet)",
            before = LastParagraphs(_createParas.Take(_insertAt), 3),
            after = string.Join("\n\n", _createParas.Skip(_insertAt).Take(2).Select(TextOf)) is { Length: > 0 } a ? a : "(none — this is the end of the chapter)",
            action = _createAction.Trim(),
            style = _createStyle.Trim().Length > 0 ? _createStyle.Trim() : "(as the author writes — match the text around it)",
            length = _createLength
        };
        string? written = null;
        var dlg = new AiRunWindow(_ai, _book, ch.Id, ItemKind.Notes, $"Write {WhereLabel(_insertAt)} — \"{ch.Title}\"", "New text", AiPrompts.CreatePassage,
            "New text for the chapter, from your action and style, in the voice of the paragraphs around the place you chose. Nothing goes into the chapter until you " +
            "put it in. The prompt is \"create-passage\" in AI › Prompt Library.",
            provider => _ai.Prompts.Get(AiPrompts.CreatePassage).Bind(values, provider), ConfirmSend,
            nextLabel: "Next: read it beside the chapter →", handleResponse: r => written = r.Text) { Owner = this };
        dlg.ShowDialog();
        if (written == null) return;
        _styles.Remember(_createStyle);
        _draft = _draftAsWritten = string.Join("\n\n", CreatedText.Paragraphs(written));
        ShowSidePanel();
        UpdateStatus("The AI's text is beside the chapter — read it, change it if you like, then Put it in the chapter.");
    }

    /// <summary>Puts the draft into the chapter at the chosen +, each paragraph styled like its neighbour.</summary>
    private void PutIn(Chapter ch)
    {
        if (_rtb == null || _draft == null || _insertAt < 0) return;
        var texts = CreatedText.Paragraphs(_draft);
        if (texts.Count == 0) { _draft = null; ShowSidePanel(); return; }
        var paras = CreateParagraphs();
        var like = BodyParagraphNear(paras, _insertAt);
        var made = texts.Select(t => StyledLike(like, t)).ToList();

        if (_insertAt < paras.Count)
            foreach (var m in made) paras[_insertAt].SiblingBlocks.InsertBefore(paras[_insertAt], m);
        else if (paras.Count > 0)
        {
            Block last = paras[^1];
            foreach (var m in made) { last.SiblingBlocks.InsertAfter(last, m); last = m; }
        }
        else foreach (var m in made) _rtb.Document.Blocks.Add(m);

        _inserted = made;
        _insertedTexts = made.Select(TextOf).ToList();
        _showingPutIn = true;
        _draft = null; _draftAsWritten = null;
        MarkDirty();
        UpdateWordCount(ch);
        RefreshMarks();
        ShowSidePanel();
        ScrollTo(made[0].ContentStart);
        UpdateStatus($"{made.Count} paragraph{(made.Count == 1 ? "" : "s")} put into the chapter, with a green band. Take it out again in the panel if it is not right.");
    }

    /// <summary>
    /// The paragraph new text should look like: the nearest ordinary paragraph to the place — after it first,
    /// then before — not a scene break or a short centred line.
    /// </summary>
    private static Paragraph? BodyParagraphNear(List<Paragraph> paras, int at)
    {
        bool Body(Paragraph p) { var t = TextOf(p); return t != FlowDocumentXaml.SceneBreak && t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length >= 3; }
        for (int d = 0; d < paras.Count; d++)
        {
            if (at + d < paras.Count && Body(paras[at + d])) return paras[at + d];
            if (at - 1 - d >= 0 && Body(paras[at - 1 - d])) return paras[at - 1 - d];
        }
        return paras.FirstOrDefault();
    }

    private void ScrollToPlace()
    {
        if (_insertAt >= 0 && _insertAt < _createParas.Count) ScrollTo(_createParas[_insertAt].ContentStart);
        else if (_createParas.Count > 0) ScrollTo(_createParas[^1].ContentEnd);
    }

    /// <summary>A new paragraph that looks like <paramref name="like"/>: its spacing, indent and font.</summary>
    private static Paragraph StyledLike(Paragraph? like, string text)
    {
        var run = new Run(text);
        var p = new Paragraph(run);
        if (like == null) return p;
        foreach (var dp in new DependencyProperty[] { Block.MarginProperty, Paragraph.TextIndentProperty, Block.TextAlignmentProperty, Block.LineHeightProperty,
                                                      TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.ForegroundProperty })
        {
            var v = like.ReadLocalValue(dp);
            if (v != DependencyProperty.UnsetValue) p.SetValue(dp, v);
        }
        if (like.Inlines.FirstInline is Run first)
            foreach (var dp in new DependencyProperty[] { TextElement.FontFamilyProperty, TextElement.FontSizeProperty, TextElement.ForegroundProperty })
            {
                var v = first.ReadLocalValue(dp);
                if (v != DependencyProperty.UnsetValue) run.SetValue(dp, v);
            }
        return p;
    }

    private void TakeOut()
    {
        if (_inserted == null || _current is not Chapter ch) return;
        int at = _createParas.IndexOf(_inserted[0]);
        foreach (var p in _inserted) p.SiblingBlocks?.Remove(p);
        _inserted = null; _insertedTexts = null; _showingPutIn = false;
        _insertAt = Math.Max(0, at);
        MarkDirty();
        UpdateWordCount(ch);
        RefreshMarks();
        ShowSidePanel();
        UpdateStatus("The new text is out of the chapter again.");
    }

    // ── The panel: a new chapter ──────────────────────────────────────────────

    private UIElement CreateChapterPanel(Chapter ch)
    {
        var p = PanelStack();
        p.Children.Add(Heading("✚  Create"));
        p.Children.Add(Hint("Click a + in the text — between two paragraphs, or the one at the end — and say what to write there and how. " +
                            "Or write a whole new chapter after this one, below."));
        if (TakeOutCard() is { } takeOut) p.Children.Add(takeOut);
        var count = CreateParagraphs().Count;
        p.Children.Add(Card("New text in this chapter", Body("The + marks in the left margin are the places new text can go."),
            PanelButton("At the end of this chapter", "Chooses the + after the last paragraph", () => { OpenInsert(count); ScrollToPlace(); }, primary: true),
            PanelButton("At the start", "Chooses the + before the first paragraph; ‹ › in the panel then move the place", () => { OpenInsert(0); ScrollToPlace(); })));

        var form = new StackPanel();
        form.Children.Add(Body($"The AI writes it in the voice of the book; it goes after \"{ch.Title}\" in the tree and opens here to read. Delete it like any chapter if it is not right.", muted: true));
        form.Children.Add(Label("Title"));
        form.Children.Add(Box(_chapterTitle, v => _chapterTitle = v, "The new chapter's title", "Title", lines: 1));
        form.Children.Add(Label("Action — what should happen in it?"));
        form.Children.Add(Box(_chapterAction, v => _chapterAction = v, "For example: Nell goes back up the tower at dawn and finds the logbook gone", "Action", lines: 3));
        form.Children.Add(Label("Style — how should it be written?"));
        form.Children.Add(StyleBox(() => _chapterStyle, v => _chapterStyle = v, "For example: slow and close, mostly what Nell notices, little dialogue"));
        form.Children.Add(Label("Length"));
        form.Children.Add(Choice(ChapterLengths, _chapterLength, v => _chapterLength = v));
        p.Children.Add(Card("A new chapter after this one", form,
            AiButton("Write the chapter (AI)…", "Sends the action, the style, the book's synopsis, and this chapter's summary and ending; the AI writes the new chapter",
                     () => WriteChapter(ch), primary: false)));
        return Scroll(p);
    }

    private void WriteChapter(Chapter after)
    {
        if (_book == null) return;
        if (_chapterAction.Trim().Length == 0)
        {
            MessageBox.Show(this, "Say what should happen in the new chapter first — the Action box.", "Create", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var title = _chapterTitle.Trim().Length > 0 ? _chapterTitle.Trim() : "New Chapter";
        var endText = ChapterText(after).Replace("\r\n", "\n").Split("\n", StringSplitOptions.RemoveEmptyEntries).Where(l => l.Trim().Length > 0).TakeLast(3);
        var values = new
        {
            book = _book.Title, synopsis = _book.Synopsis.Length > 0 ? _book.Synopsis : "(none)",
            previous = $"{_book.ChapterNumber(after)}. {after.Title}",
            previous_summary = _book.SummaryText(after).Length > 0 ? _book.SummaryText(after) : "(no summary)",
            previous_end = string.Join("\n\n", endText) is { Length: > 0 } e ? e : "(empty)",
            title, action = _chapterAction.Trim(),
            style = _chapterStyle.Trim().Length > 0 ? _chapterStyle.Trim() : "(as the author writes — match the book)",
            length = _chapterLength
        };
        string? written = null;
        var dlg = new AiRunWindow(_ai, _book, after.Id, ItemKind.Notes, $"Write a new chapter after \"{after.Title}\"", "New chapter", AiPrompts.CreateChapter,
            "A new chapter, from your action and style, in the voice of the book. It is added after this chapter and opens for you to read. " +
            "The prompt is \"create-chapter\" in AI › Prompt Library.",
            provider => _ai.Prompts.Get(AiPrompts.CreateChapter).Bind(values, provider), ConfirmSend,
            nextLabel: "Next: open the new chapter →", handleResponse: r => written = r.Text) { Owner = this };
        dlg.ShowDialog();
        if (written == null) return;

        _styles.Remember(_chapterStyle);
        var text = string.Join("\n\n", CreatedText.Paragraphs(written));
        var ch = new Chapter { Title = title, SectionId = after.SectionId };
        _book.Chapters.Insert(_book.Chapters.IndexOf(after) + 1, ch);
        _store.SaveChapterBody(_book, ch, FlowDocumentXaml.FromPlainText(text), WordCounter.Count(text));
        _chapterTitle = ""; _chapterAction = "";
        MarkDirty();
        BuildTree(select: ch);
        UpdateStatus($"\"{title}\" written after \"{after.Title}\" — read it; double-click to edit in Write mode. Delete it like any chapter if it is not right.");
    }
}
