using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace AuthorPlus.App;

/// <summary>
/// Plotlines mode (Ron, 2026-11-06; plan phase 8): the plotlines that run through the open chapter,
/// each analysed in this chapter (its part here, what happens, who drives it, its key moments quoted
/// and washed in its colour in the text) and across the book (summary, progression, characters,
/// convergences). A character's own story can be a plotline (<see cref="Plotline.CharacterId"/>).
/// </summary>
public partial class MainWindow
{
    private enum PlotlineView { Chapter, Book, Page }
    private PlotlineView _plotlineView = PlotlineView.Chapter;
    private Guid? _panelPlotlineId;                     // Plotlines: the thread open in the panel
    private int _momentsFound, _momentsAsked;           // Plotlines: key moments of the open thread found in the text
    private readonly List<string> _momentsMissing = new();   // Plotlines: the open thread's key moments not found in the text
    private TextBlock? _plotlineAnalysisHead;           // the "Plotline analysis" heading on the plotline's page

    private static readonly Dictionary<string, string> PlotHeadingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        [PlotlineAnalyses.Role] = "Its part in this chapter", [PlotlineAnalyses.WhatHappens] = "What happens",
        [PlotlineAnalyses.Characters] = "Characters", [PlotlineAnalyses.KeyMoments] = "Key moments",
        [PlotlineAnalyses.Summary] = "Summary", [PlotlineAnalyses.Progression] = "Progression", [PlotlineAnalyses.Convergences] = "Where it meets other plotlines"
    };

    /// <summary>A thread's colour in this chapter: by its place among the chapter's threads.</summary>
    private Color PlotColour(Plotline p)
    {
        int i = _current is Chapter ch ? PlotlineAnalyses.In(_book!, ch).ToList().IndexOf(p) : -1;
        if (i < 0) i = _book!.Plotlines.IndexOf(p);
        return CastColours[Math.Max(0, i) % CastColours.Length];
    }

    private Brush PlotWash(Plotline p) { var c = PlotColour(p); var b = new SolidColorBrush(Color.FromArgb(0x5C, c.R, c.G, c.B)); b.Freeze(); return b; }
    private Brush PlotSolid(Plotline p) { var b = new SolidColorBrush(PlotColour(p)); b.Freeze(); return b; }

    /// <summary>Plotlines mode's marks: each analysed thread's key moments, washed in its colour.</summary>
    private void ShowPlotlineMarks(Chapter ch)
    {
        _nameMarks = new List<NameMark>();
        _momentsFound = 0; _momentsAsked = 0; _momentsMissing.Clear();
        foreach (var p in PlotlineAnalyses.In(_book!, ch).Concat(_book!.Plotlines.Where(x => x.Id == _panelPlotlineId)).Distinct())
        {
            if (PlotlineAnalyses.InChapter(_book, p.Id, ch.Id) is not { } a) continue;
            foreach (var choices in PlotlineAnalyses.MomentChoices(a.Body))
            {
                // The whole quoted passage if it is in the text; otherwise each quotation in the point that is.
                var hits = new List<(TextPointer Start, TextPointer End)>();
                if (PassageInDocument.Locate(_rtb!.Document, choices[0]) is { } whole) hits.Add(whole);
                else foreach (var piece in choices.Skip(1)) if (PassageInDocument.Locate(_rtb.Document, piece) is { } h) hits.Add(h);
                if (p.Id == _panelPlotlineId) { _momentsAsked++; if (hits.Count > 0) _momentsFound++; else _momentsMissing.Add(choices[^1]); }
                foreach (var (start, end) in hits)
                    _nameMarks.Add(new NameMark(start, end, PlotWash(p), p, $"{p.Name}: a key moment — click for the plotline"));
            }
        }
        _nameMarks.Sort((x, y) => x.Start.CompareTo(y.Start));
        var selected = _panelPlotlineId is { } id ? _book.Plotlines.FirstOrDefault(x => x.Id == id) : null;
        _overlay?.ShowNames(_nameMarks, selected);
    }

    // ── The chapter's threads ─────────────────────────────────────────────────

    private UIElement PlotlinesPanel(Chapter ch)
    {
        var p = PanelStack();
        p.Children.Add(Heading("Plotlines in this chapter"));
        p.Children.Add(Hint("The threads of the story that run through this chapter. Click one to analyze it in this chapter — its key moments are then washed in its colour " +
                            "in the text — and to see it across the book."));
        var threads = PlotlineAnalyses.In(_book!, ch);
        if (threads.Count == 0)
            p.Children.Add(Body("No plotline is linked to this chapter yet. Find its plotlines with the AI, or add one the book already has.", muted: true));
        foreach (var t in threads)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(Swatch(PlotSolid(t)));
            var who = t.CharacterId is { } cid ? _book!.Characters.FirstOrDefault(c => c.Id == cid) : null;
            var a = PlotlineAnalyses.InChapter(_book!, t.Id, ch.Id);
            var info = new TextBlock
            {
                Text = $"  {Palette.RoleWord(t.RoleIn(ch.Id) ?? PlotlineRole.Continuing).ToLowerInvariant()} here · {(a != null ? "analyzed" : "not analyzed here yet")}" +
                       (who != null ? $" · {who.Name}'s story" : ""),
                Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
            };
            var thread = t;
            var link = Palette.LinkText(t.Name, () => OpenName(thread), "Open this plotline here: in this chapter, in the book, and its page");
            DockPanel.SetDock(link, Dock.Left);
            row.Children.Add(link);
            row.Children.Add(info);
            p.Children.Add(row);
        }

        var buttons = new List<Button>
        {
            AiButton("Find its plotlines (AI)", "Asks the AI which threads run through this chapter (and who is in it); known ones are linked, new ones are offered for adding",
                     FindChapterPlotlines, primary: threads.Count == 0)
        };
        var others = _book!.Plotlines.Where(x => !threads.Contains(x)).ToList();
        if (others.Count > 0)
        {
            Button? add = null;
            add = PanelButton("Add a plotline ▾", "Link one of the book's other plotlines to this chapter", () =>
            {
                var menu = new ContextMenu { PlacementTarget = add, Placement = PlacementMode.Bottom };
                foreach (var o in others)
                {
                    var thread = o;
                    var mi = new MenuItem { Header = o.Name };
                    mi.Click += (_, _) => { _book.AddToChapter(thread, ch); MarkDirty(); UpdateChapterLinks(ch); RefreshMarks(); ShowSidePanel(); };
                    menu.Items.Add(mi);
                }
                menu.IsOpen = true;
            });
            buttons.Add(add);
        }
        buttons.Add(PanelButton("Plotlines page", "The chart of every thread across the book", () => SelectNode(PlotlinesHeader)));
        var row2 = new WrapPanel();
        foreach (var b in buttons) row2.Children.Add(b);
        p.Children.Add(row2);
        return Scroll(p);
    }

    // ── One thread ────────────────────────────────────────────────────────────

    private UIElement PlotlinePanel(Chapter ch, Plotline t)
    {
        var dock = new DockPanel();
        var top = PanelStack();
        top.Margin = new Thickness(14, 12, 14, 0);
        top.Children.Add(BackLink("Plotlines in this chapter", () => { _panelPlotlineId = null; _mentionAt = -1; RefreshMarks(); ShowSidePanel(); }));
        var nameRow = new DockPanel();
        nameRow.Children.Add(Swatch(PlotSolid(t)));
        nameRow.Children.Add(Heading(t.Name));
        top.Children.Add(nameRow);
        top.Children.Add(MomentNav(t));

        var row = new WrapPanel();
        var inChapter = PlotlineAnalyses.In(_book!, ch).Contains(t);
        if (!inChapter)
            row.Children.Add(PanelButton("Add to this chapter", "Links the thread to this chapter, on the chapter's list and the Plotlines chart", () =>
            {
                _book!.AddToChapter(t, ch); MarkDirty(); UpdateChapterLinks(ch); ShowSidePanel();
            }, primary: true));
        if (t.CharacterId is { } cid && _book!.Characters.FirstOrDefault(c => c.Id == cid) is { } who)
            row.Children.Add(PanelButton($"See {who.Name} in this chapter", $"This thread is {who.Name}'s own story. Characters mode, with {who.Name} beside the text",
                                         () => OpenCharacterInChapter(ch, who)));
        top.Children.Add(row);
        top.Children.Add(PlotlineViewSwitch(t));
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(_plotlineView switch
        {
            PlotlineView.Book => Scroll(BookPlotlineView(t, inPanel: true)),
            PlotlineView.Page => BuildPlotlinePage(t, inPanel: true),
            _                 => ChapterPlotlineView(ch, t)
        });
        return dock;
    }

    private UIElement MomentNav(Plotline t)
    {
        var marks = _nameMarks.Where(m => ReferenceEquals(m.Target, t)).ToList();
        var outer = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        var row = new WrapPanel();
        outer.Children.Add(row);
        var label = new TextBlock
        {
            Text = _current is Chapter ch && PlotlineAnalyses.InChapter(_book!, t.Id, ch.Id) == null ? "Analyze it in this chapter to mark its key moments in the text."
                 : marks.Count == 0 ? "None of its key moments could be found in the text as it stands."
                 : $"{marks.Count} key moment{(marks.Count == 1 ? "" : "s")} marked in the text" + (_momentsAsked > _momentsFound ? $" ({_momentsAsked - _momentsFound} not found — the text may have changed)." : "."),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 10, 0), TextWrapping = TextWrapping.Wrap
        };
        row.Children.Add(label);
        if (_momentsMissing.Count > 0)
        {
            outer.Children.Add(new TextBlock { Text = "Not found in the text: " + string.Join("  ·  ", _momentsMissing.Select(m => "“" + Shorten(m, 80) + "”")),
                                               Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        }
        if (marks.Count == 0) return outer;
        void Go(int step)
        {
            _mentionAt = ((_mentionAt < 0 ? (step > 0 ? -1 : 0) : _mentionAt) + step + marks.Count) % marks.Count;
            var m = marks[_mentionAt];
            _rtb?.Selection.Select(m.Start, m.End);
            _overlay?.SetCurrent(m);
            ScrollTo(m.Start);
            label.Text = $"Key moment {_mentionAt + 1} of {marks.Count}.";
        }
        row.Children.Add(PanelButton("‹ Previous", "Select the key moment before this one in the text", () => Go(-1)));
        row.Children.Add(PanelButton("Next ›", "Select the next key moment in the text", () => Go(+1)));
        return outer;
    }

    private UIElement PlotlineViewSwitch(Plotline t)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (view, label, tip) in new[]
        {
            (PlotlineView.Chapter, "In this chapter", $"What this chapter does with \"{t.Name}\": its part here, what happens, who drives it, its key moments"),
            (PlotlineView.Book,    "In the book",     $"\"{t.Name}\" across the whole book: summary, progression, characters, where it meets other threads"),
            (PlotlineView.Page,    "Its page",        $"\"{t.Name}\"'s page — its kind, status, summary and the chapters it runs through")
        })
        {
            var b = new RadioButton
            {
                Content = label, GroupName = "PlotlineView", IsChecked = _plotlineView == view, ToolTip = tip,
                Style = (Style)FindResource(ToolBar.RadioButtonStyleKey), Padding = new Thickness(10, 3, 10, 3),
                FontWeight = _plotlineView == view ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 2, 0),
                BorderBrush = Palette.Line, BorderThickness = new Thickness(1)
            };
            System.Windows.Automation.AutomationProperties.SetName(b, label);
            var v = view;
            b.Checked += (_, _) => { if (_plotlineView == v) return; _plotlineView = v; ShowSidePanel(); };
            row.Children.Add(b);
        }
        return row;
    }

    private static StackPanel PlotSections(Item item, IReadOnlyList<string> headings)
    {
        var stack = new StackPanel();
        stack.Children.Add(MadeBy(item));
        foreach (var (heading, text) in CharacterAnalyses.Sections(item.Body, headings))
        {
            if (heading.Length > 0)
                stack.Children.Add(new TextBlock { Text = PlotHeadingWords.GetValueOrDefault(heading, heading), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
            stack.Children.Add(Body(text));
        }
        return stack;
    }

    private UIElement ChapterPlotlineView(Chapter ch, Plotline t)
    {
        var p = new StackPanel { Margin = new Thickness(14, 10, 14, 16) };
        var mine = PlotlineAnalyses.InChapter(_book!, t.Id, ch.Id);
        UIElement here = mine != null
            ? PlotSections(mine, PlotlineAnalyses.ChapterHeadings)
            : Body("Not analyzed in this chapter yet. The AI reads the chapter and writes, for this thread here only:\n" +
                   "• Its part in this chapter — introduced, moved on, turned or resolved\n• What happens — the thread's events here\n" +
                   "• Characters — who drives it and who it affects\n• Key moments — quoted from the text, and washed in the thread's colour there", muted: true);
        p.Children.Add(Card("In this chapter", here,
            AiButton(mine == null ? "Analyze it in this chapter (AI)…" : "Analyze again (AI)…",
                     "Sends this chapter and what the book says about the thread; the result is kept with the chapter and its key moments are marked in the text",
                     () => AnalyzePlotlineInChapter(ch, t), primary: mine == null)));

        var whole = PlotlineAnalyses.InBook(_book!, t.Id);
        var body = new StackPanel();
        if (whole != null)
        {
            body.Children.Add(MadeBy(whole));
            var summary = CharacterAnalyses.Part(whole.Body, PlotlineAnalyses.BookHeadings, PlotlineAnalyses.Summary);
            body.Children.Add(Body(summary.Length > 0 ? summary : Shorten(whole.Body, 600)));
        }
        else
        {
            var about = string.Join("; ", Bullets.Parse(t.Summary).Select(b => b.Text));
            body.Children.Add(Body((about.Length > 0 ? $"From its page: {Shorten(about, 300)}\n\n" : "") +
                                   "No book analysis yet. It covers the thread's summary, how it moves through the chapters, who carries it, and where it meets other threads.", muted: true));
        }
        var buttons = new List<Button>();
        if (whole != null) buttons.Add(PanelButton("Open the book analysis", "Its summary, progression, characters and convergences, here in the panel", () => { _plotlineView = PlotlineView.Book; ShowSidePanel(); }, primary: true));
        if (PlotlineBookBlocked(t) != null)
            body.Children.Add(Body("\nTo make it: analyze it in this chapter first (above) — the book analysis is built from the chapters' analyses and summaries.", muted: true));
        else
            buttons.Add(AiButton(whole == null ? "Analyze across the book (AI)…" : "Analyze the book again (AI)…",
                                 "Reads every chapter the thread runs through — its summary and the thread's analysis there — with the characters and the other threads",
                                 () => AnalyzePlotlineInBook(t)));
        p.Children.Add(Card("In the book", body, buttons.ToArray()));
        return Scroll(p);
    }

    /// <summary>The book analysis of a thread with its chapters under it: in the panel and on the plotline's page.</summary>
    private StackPanel BookPlotlineView(Plotline t, bool inPanel)
    {
        var p = new StackPanel { Margin = inPanel ? new Thickness(14, 10, 14, 16) : new Thickness(0) };
        var whole = PlotlineAnalyses.InBook(_book!, t.Id);
        var chapters = PlotlineAnalyses.ChaptersOf(_book!, t);
        var analysed = PlotlineAnalyses.ByChapter(_book!, t.Id).ToDictionary(x => x.Chapter.Id, x => x.Analysis);

        var body = new StackPanel();
        if (whole != null)
        {
            body.Children.Add(PlotSections(whole, PlotlineAnalyses.BookHeadings));
            int newer = PlotlineAnalyses.ByChapter(_book!, t.Id).Count(x => x.Analysis.CreatedUtc > whole.CreatedUtc);
            if (newer > 0) body.Children.Insert(0, Body($"{newer} chapter analys{(newer == 1 ? "is is" : "es are")} newer than this — analyze the book again to include {(newer == 1 ? "it" : "them")}.", muted: true));
        }
        else body.Children.Add(Body("No book analysis yet. The AI reads every chapter the thread runs through — the chapter's summary, and the thread's analysis there where there is one — " +
                                    "with the characters and the other plotlines, and writes:\n• Summary\n• Progression — how it moves through the chapters\n" +
                                    "• Characters — who carries it\n• Where it meets other plotlines", muted: true));
        if (PlotlineBookBlocked(t) is { } why)
        {
            body.Children.Add(Body("\n" + why, muted: true));
            p.Children.Add(Card("In the book", body));
        }
        else
            p.Children.Add(Card("In the book", body, AiButton(whole == null ? "Analyze across the book (AI)…" : "Analyze the book again (AI)…",
                "Reads every chapter the thread runs through — its summary and the thread's analysis there — with the characters and the other threads; kept with the plotline",
                () => AnalyzePlotlineInBook(t), primary: whole == null)));

        var each = new StackPanel();
        int summarised = chapters.Count(ch => _book!.SummaryText(ch).Length > 0);
        each.Children.Add(Body(chapters.Count == 0
            ? "It runs through no chapter yet. Open a chapter in Plotlines mode and add it there, or mark it on the Plotlines chart."
            : $"It runs through {chapters.Count} chapter{(chapters.Count == 1 ? "" : "s")}: {analysed.Count} analyzed for it, {summarised} with a summary. " +
              "The book analysis is built from these, so the more chapters are analyzed, the fuller it is.", muted: true));
        foreach (var ch in chapters)
        {
            var target = ch;
            var link = Palette.LinkText($"{_book!.ChapterNumber(ch)}. {ch.Title}", () => OpenPlotlineInChapter(target, t),
                                        $"Open this chapter in Plotlines mode with \"{t.Name}\" beside it");
            link.Margin = new Thickness(0, 8, 0, 0);
            link.FontWeight = FontWeights.SemiBold;
            each.Children.Add(link);
            if (analysed.TryGetValue(ch.Id, out var a))
            {
                var role = CharacterAnalyses.Part(a.Body, PlotlineAnalyses.ChapterHeadings, PlotlineAnalyses.Role);
                each.Children.Add(Body(Shorten(role.Length > 0 ? role : a.Body, 400)));
            }
            else each.Children.Add(Body("Not analyzed in this chapter yet.", muted: true));
        }
        p.Children.Add(Card("In each chapter", each));
        return p;
    }

    private string? PlotlineBookBlocked(Plotline t)
    {
        var chapters = PlotlineAnalyses.ChaptersOf(_book!, t);
        if (chapters.Count == 0)
            return "It runs through no chapter yet, so there is nothing to build the book analysis from. Open a chapter in Plotlines mode (Ctrl+5) and add it there — or mark it on the Plotlines chart.";
        if (chapters.All(ch => _book!.SummaryText(ch).Length == 0 && PlotlineAnalyses.InChapter(_book, t.Id, ch.Id) == null))
            return $"None of the {chapters.Count} chapter(s) it runs through has a summary or an analysis of it yet. Analyze it in a chapter (Plotlines mode), or write the chapters' summaries (Analysis mode).";
        return null;
    }

    private void OpenPlotlineInChapter(Chapter ch, Plotline t)
    {
        if (_mode != EditorMode.Plotlines) { _mode = EditorMode.Plotlines; UpdateModeMenu(); }
        if (ReferenceEquals(_current, ch)) { CommitCurrent(); ShowCurrent(); }
        else SelectNode(ch);
        if (!ReferenceEquals(_current, ch)) return;
        _panelPlotlineId = t.Id; _mentionAt = -1;
        _plotlineView = PlotlineView.Chapter;
        RefreshMarks();
        ShowSidePanel();
    }

    // ── A character's story as a plotline ─────────────────────────────────────

    /// <summary>The plotline that is the character's own story, or null.</summary>
    private Plotline? StoryOf(Character c) => _book!.Plotlines.FirstOrDefault(p => p.CharacterId == c.Id);

    /// <summary>"Their story" on a character's book view: the plotline, or a button that makes it.</summary>
    private Border StoryCard(Character c)
    {
        if (StoryOf(c) is { } story)
            return Card("Their story as a plotline", Body($"\"{story.Name}\" is {c.Name}'s own story, running through {story.ChapterIds.Count} chapter(s)."),
                PanelButton("Open the plotline", "Its page, with its analysis across the book", () => SelectNode(story)));
        var chapters = CharacterAnalyses.ChaptersOf(_book!, c);
        return Card("Their story as a plotline",
            Body($"A character who changes through the book has a story of their own. Make it a plotline, and it is followed like any other thread: in Plotlines mode, on the chart, and with its own analysis." +
                 (chapters.Count == 0 ? "" : $" It will run through the {chapters.Count} chapter(s) {c.Name} is in."), muted: true),
            PanelButton($"Make {c.Name}'s story a plotline", "Adds a plotline for their story, linked to them and to the chapters they are in", () => MakeStory(c)));
    }

    private void MakeStory(Character c)
    {
        if (_book == null || StoryOf(c) != null) return;
        var story = new Plotline { Name = $"{c.Name}'s story", CharacterId = c.Id, Kind = PlotlineKind.Primary, CharacterIds = { c.Id } };
        if (CharacterAnalyses.InBook(_book, c.Id) is { } whole)
        {
            var arc = CharacterAnalyses.Part(whole.Body, CharacterAnalyses.BookHeadings, CharacterAnalyses.StoryArc);
            if (arc.Length == 0) arc = CharacterAnalyses.Part(whole.Body, CharacterAnalyses.BookHeadings, CharacterAnalyses.Summary);
            if (arc.Length > 0) story.Summary = Bullets.AddRange("", arc.Split('\n').Select(l => l.Trim().TrimStart('-', '•').Trim()).Where(l => l.Length > 0), fromAi: true);
        }
        _book.Plotlines.Add(story);
        foreach (var ch in CharacterAnalyses.ChaptersOf(_book, c)) _book.AddToChapter(story, ch);
        MarkDirty();
        var keep = _current;
        BuildTree(select: keep);
        if (_current is Chapter && _side != null) ShowSidePanel();
        UpdateStatus($"\"{story.Name}\" is a plotline now, in {story.ChapterIds.Count} chapter(s). It is on the Plotlines page and in Plotlines mode.");
    }

    // ── The AI ────────────────────────────────────────────────────────────────

    private string AboutPlotline(Plotline t)
    {
        var lines = Bullets.Parse(t.Summary).Select(b => b.Text).Concat(Bullets.Parse(t.StatusNote).Select(b => b.Text)).ToList();
        if (t.CharacterId is { } cid && _book!.Characters.FirstOrDefault(c => c.Id == cid) is { } who) lines.Insert(0, $"It is {who.Name}'s own story.");
        return lines.Count > 0 ? string.Join("\n", lines) : "(nothing written yet)";
    }

    private void AnalyzePlotlineInChapter(Chapter ch, Plotline t)
    {
        if (_book == null) return;
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var text = ChapterText(ch);
        if (text.Trim().Length == 0)
        {
            MessageBox.Show(this, "This chapter has no text yet, so there is nothing to analyze.", "Analyze plotline", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        int here = _book.Chapters.IndexOf(ch);
        var earlier = PlotlineAnalyses.ChaptersOf(_book, t).Where(c => _book.Chapters.IndexOf(c) < here).ToList();
        var values = new
        {
            book = _book.Title, chapter = $"{_book.ChapterNumber(ch)}. {ch.Title}", plotline = t.Name, about = AboutPlotline(t),
            before = earlier.Count == 0 ? "No earlier chapter has it — this may be where it starts."
                   : "It runs through earlier chapters: " + string.Join("; ", earlier.Select(c => $"{_book.ChapterNumber(c)}. {c.Title}")) +
                     (PlotlineAnalyses.InChapter(_book, t.Id, earlier[^1].Id) is { } last ? $"\nIn the last of them:\n{CharacterAnalyses.Part(last.Body, PlotlineAnalyses.ChapterHeadings, PlotlineAnalyses.WhatHappens)}" : ""),
            text
        };
        var dlg = new AiRunWindow(_ai, _book, ch.Id, ItemKind.PlotlineInChapter, $"\"{t.Name}\" in \"{ch.Title}\"", $"{t.Name} in this chapter", AiPrompts.PlotlineInChapter,
            "What this chapter does with the thread: its part here, what happens, who drives it, and its key moments quoted from the text (marked in the chapter in Plotlines mode). " +
            "The prompt is \"plotline-in-chapter\" in AI › Prompt Library.",
            provider => _ai.Prompts.Get(AiPrompts.PlotlineInChapter).Bind(values, provider), ConfirmSend,
            nextLabel: "Next: see it beside the chapter →") { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        foreach (var item in dlg.Created) item.PlotlineIds = new List<Guid> { t.Id };
        MarkDirty();
        AfterPlotlineAnalysis(t, PlotlineView.Chapter);
        UpdateStatus($"\"{t.Name}\" analyzed in \"{ch.Title}\"; its key moments are marked in the text.");
    }

    private void AnalyzePlotlineInBook(Plotline t)
    {
        if (_book == null) return;
        if (PlotlineBookBlocked(t) is { } why) { MessageBox.Show(this, why, "Analyze across the book", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var chapters = PlotlineAnalyses.ChaptersOf(_book, t);
        var parts = chapters.Select(ch =>
        {
            var role = t.RoleIn(ch.Id);
            var head = $"Chapter {_book.ChapterNumber(ch)}. {ch.Title}" + (role != null ? $" ({Palette.RoleWord(role.Value).ToLowerInvariant()})" : "");
            var summary = _book.SummaryText(ch);
            var mine = PlotlineAnalyses.InChapter(_book, t.Id, ch.Id);
            return head + "\nSummary: " + (summary.Length > 0 ? summary : "(none)") + (mine != null ? $"\nThe thread in this chapter:\n{mine.Body.Trim()}" : "");
        });
        var characters = _book.Characters.Count == 0 ? "(none)" : string.Join("\n", _book.Characters.Select(c =>
        {
            var role = string.Join("; ", Bullets.Parse(c.Role).Select(b => b.Text));
            return $"- {c.Name}{(role.Length > 0 ? " — " + role : "")}{(t.CharacterIds.Contains(c.Id) ? " (linked to this thread)" : "")}{(t.CharacterId == c.Id ? " (this is their story)" : "")}";
        }));
        var others = string.Join("\n", _book.Plotlines.Where(x => x != t).Select(x =>
        {
            var runs = PlotlineAnalyses.ChaptersOf(_book, x).Select(ch => $"{_book.ChapterNumber(ch)}. {ch.Title}").ToList();
            return $"- {x.Name} — {Shorten(string.Join(" ", Bullets.Parse(x.Summary).Select(b => b.Text)), 300)} — {(runs.Count > 0 ? string.Join("; ", runs) : "in no chapter yet")}";
        }));
        var values = new
        {
            book = _book.Title, synopsis = _book.Synopsis.Length > 0 ? _book.Synopsis : "(none)", plotline = t.Name, about = AboutPlotline(t),
            chapters = string.Join("\n\n", parts), characters, others = others.Length > 0 ? others : "(none)"
        };
        var dlg = new AiRunWindow(_ai, _book, t.Id, ItemKind.PlotlineInBook, $"\"{t.Name}\" across the book", $"{t.Name} in the book", AiPrompts.PlotlineInBook,
            $"The thread across the whole book: summary, progression over the chapters, the characters who carry it, and where it meets other plotlines. It reads the {chapters.Count} chapter(s) " +
            "it runs through — each summary, and the thread's analysis there where there is one — not the chapters' full text. The prompt is \"plotline-in-book\" in AI › Prompt Library.",
            provider => _ai.Prompts.Get(AiPrompts.PlotlineInBook).Bind(values, provider), ConfirmSend,
            nextLabel: "Next: see the analysis →") { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        MarkDirty();
        AfterPlotlineAnalysis(t, PlotlineView.Book);
        UpdateStatus($"\"{t.Name}\" analyzed across the book.");
    }

    private void AfterPlotlineAnalysis(Plotline t, PlotlineView view)
    {
        if (_current is Chapter && _side != null && _mode == EditorMode.Plotlines)
        {
            _panelPlotlineId = t.Id;
            _plotlineView = view;
            RefreshMarks();
            ShowSidePanel();
        }
        else if (ReferenceEquals(_current, t))
        {
            ShowCurrent();
            var head = _plotlineAnalysisHead;
            head?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => head.BringIntoView());
        }
    }

    /// <summary>The block at the end of a plotline's page: whose story it is, and its analysis.</summary>
    private void AddPlotlineAnalysisBlock(StackPanel panel, Plotline t)
    {
        panel.Children.Add(WhoseStoryRow(t));
        _plotlineAnalysisHead = new TextBlock { Text = "Plotline analysis", FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 20, 0, 2) };
        panel.Children.Add(_plotlineAnalysisHead);
        panel.Children.Add(new TextBlock
        {
            Text = "The AI's reading of the thread across the book — summary, progression, the characters who carry it, where it meets other threads — and, under it, " +
                   "what each chapter does with it. Click a chapter to open it with the thread beside the text, where its analysis in that chapter is made.",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        panel.Children.Add(BookPlotlineView(t, inPanel: false));
    }

    /// <summary>"Whose story is it?": a character's own story, or a thread of the plot.</summary>
    private UIElement WhoseStoryRow(Plotline t)
    {
        var whose = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        whose.Children.Add(new TextBlock { Text = "Whose story is it?", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var none = "(no one's — a thread of the plot)";
        var options = new List<object> { none };
        options.AddRange(_book!.Characters);
        var combo = new ComboBox
        {
            ItemsSource = options, DisplayMemberPath = null, MinWidth = 220,
            SelectedItem = t.CharacterId is { } cid ? (object?)_book.Characters.FirstOrDefault(c => c.Id == cid) ?? none : none,
            ToolTip = "When the thread is a character's own story — their arc through the book — say whose. It is then shown with them in Characters mode and their book analysis."
        };
        combo.ItemTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(TextBlock)) };
        combo.ItemTemplate.VisualTree.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new CharacterNameConverter() });
        System.Windows.Automation.AutomationProperties.SetName(combo, "Whose story is it?");
        bool settingBack = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (settingBack) return;
            if (combo.SelectedItem is not Character c)
            {
                t.CharacterId = null; MarkDirty();
                UpdateStatus($"\"{t.Name}\" is a thread of the plot, not anyone's own story.");
                return;
            }
            if (StoryOf(c) is { } other && other != t &&
                MessageBox.Show(this, $"\"{other.Name}\" is already {c.Name}'s story. Make \"{t.Name}\" their story instead?", "Whose story is it?",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                settingBack = true;
                combo.SelectedItem = t.CharacterId is { } prev ? (object?)_book!.Characters.FirstOrDefault(x => x.Id == prev) ?? none : none;
                settingBack = false;
                return;
            }
            if (StoryOf(c) is { } old && old != t) old.CharacterId = null;
            t.CharacterId = c.Id;
            if (!t.CharacterIds.Contains(c.Id)) t.CharacterIds.Add(c.Id);
            var missing = CharacterAnalyses.ChaptersOf(_book!, c).Where(ch => t.RoleIn(ch.Id) == null).ToList();
            if (missing.Count > 0 &&
                MessageBox.Show(this, $"{c.Name} is in {missing.Count} chapter(s) this thread is not linked to. Link the thread to them too, so it follows their story?",
                                "Whose story is it?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                foreach (var ch in missing) _book!.AddToChapter(t, ch);
            MarkDirty();
            UpdateStatus($"\"{t.Name}\" is {c.Name}'s story now; it runs through {t.ChapterIds.Count} chapter(s).");
        };
        whose.Children.Add(combo);
        return whose;
    }

    private sealed class CharacterNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) => value is Character ch ? ch.Name : value?.ToString() ?? "";
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
    }
}
