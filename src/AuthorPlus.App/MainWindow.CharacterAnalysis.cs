using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using Brushes = System.Windows.Media.Brushes;

namespace AuthorPlus.App;

/// <summary>
/// The AI's analysis of a character (plan phase 7): in one chapter — their role there (and whether
/// they are introduced), how the chapter describes them, their motivations and actions in it — and
/// across the book — a summary as the book presents them, their story arc and how they meet the
/// plotlines. The chapter one is made from Characters mode; the book one from there or their page.
/// </summary>
public partial class MainWindow
{
    /// <summary>What the Characters panel shows of the character open in it.</summary>
    private enum CharacterView { Chapter, Book, Form }
    private CharacterView _characterView = CharacterView.Chapter;
    private TextBlock? _characterAnalysisHead;          // the "Character analysis" heading on the character's page, to bring into view

    private static readonly Dictionary<string, string> HeadingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        [CharacterAnalyses.Role] = "Role", [CharacterAnalyses.Description] = "Description",
        [CharacterAnalyses.MotivationsAndActions] = "Motivations and actions",
        [CharacterAnalyses.Summary] = "Summary", [CharacterAnalyses.StoryArc] = "Story arc",
        [CharacterAnalyses.PlotlineInteractions] = "Plotline interactions"
    };

    /// <summary>The row of views at the top of the character in the panel.</summary>
    private UIElement CharacterViewSwitch(Character c)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (view, label, tip) in new[]
        {
            (CharacterView.Chapter, "In this chapter", $"What this chapter shows of {c.Name}: their role here, how they are described, what they want and do"),
            (CharacterView.Book,    "In the book",     $"{c.Name} across the whole book: a summary, their story arc over the chapters, and the plotlines they touch"),
            (CharacterView.Form,    "Their form",      $"{c.Name}'s character form — the fields you fill in about them")
        })
        {
            var b = new RadioButton
            {
                Content = label, GroupName = "CharacterView", IsChecked = _characterView == view, ToolTip = tip,
                Style = (Style)FindResource(ToolBar.RadioButtonStyleKey), Padding = new Thickness(10, 3, 10, 3),
                FontWeight = _characterView == view ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 2, 0),
                BorderBrush = Palette.Line, BorderThickness = new Thickness(1)
            };
            System.Windows.Automation.AutomationProperties.SetName(b, label);
            var v = view;
            b.Checked += (_, _) => { if (_characterView == v) return; _characterView = v; ShowSidePanel(); };
            row.Children.Add(b);
        }
        return row;
    }

    /// <summary>"Claude · 2026-11-06 09:12", over an analysis.</summary>
    private static TextBlock MadeBy(Item item) => new()
    {
        Text = $"{item.Provider ?? "AI"} · {item.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
        Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 0, 4)
    };

    /// <summary>An analysis, heading by heading.</summary>
    private static StackPanel SectionsOf(Item item, IReadOnlyList<string> headings)
    {
        var stack = new StackPanel();
        stack.Children.Add(MadeBy(item));
        foreach (var (heading, text) in CharacterAnalyses.Sections(item.Body, headings))
        {
            if (heading.Length > 0)
                stack.Children.Add(new TextBlock { Text = HeadingWords.GetValueOrDefault(heading, heading), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
            stack.Children.Add(Body(text));
        }
        return stack;
    }

    // ── In this chapter ───────────────────────────────────────────────────────

    private UIElement ChapterCharacterView(Chapter ch, Character c)
    {
        var p = new StackPanel { Margin = new Thickness(14, 10, 14, 16) };
        var mine = CharacterAnalyses.InChapter(_book!, c.Id, ch.Id);

        UIElement here = mine != null
            ? SectionsOf(mine, CharacterAnalyses.ChapterHeadings)
            : Body($"Not analyzed in this chapter yet. The AI reads the chapter and writes, for {c.Name} here only:\n" +
                   "• Role — their part in this chapter, which can differ from their part in the book, and whether this is where they are introduced\n" +
                   "• Description — how this chapter describes them, and the personality it shows\n" +
                   "• Motivations and actions — what they want and do here, and what happens to them", muted: true);
        p.Children.Add(Card("In this chapter", here,
            AiButton(mine == null ? $"Analyze {c.Name} in this chapter (AI)…" : "Analyze again (AI)…",
                     "Sends this chapter and what is known about the character; the result is kept with the chapter and shown here",
                     () => AnalyzeCharacterInChapter(ch, c), primary: mine == null)));

        var whole = CharacterAnalyses.InBook(_book!, c.Id);
        var bookBody = new StackPanel();
        if (whole != null)
        {
            bookBody.Children.Add(MadeBy(whole));
            var summary = CharacterAnalyses.Part(whole.Body, CharacterAnalyses.BookHeadings, CharacterAnalyses.Summary);
            bookBody.Children.Add(Body(summary.Length > 0 ? summary : Shorten(whole.Body, 600)));
        }
        else
        {
            var role = Bullets.Parse(c.Role).Select(b => b.Text).ToList();
            bookBody.Children.Add(Body((role.Count > 0 ? $"Their role, from their form: {string.Join("; ", role)}.\n\n" : "") +
                                       "No book analysis yet. It covers who they are across the book, their story arc over the chapters and the plotlines they touch.", muted: true));
        }
        var buttons = new List<Button>();
        if (whole != null) buttons.Add(PanelButton("Open the book analysis", "Their summary, story arc and plotline interactions, here in the panel", () => { _characterView = CharacterView.Book; ShowSidePanel(); }, primary: true));
        if (BookAnalysisBlocked(c) != null)
            bookBody.Children.Add(Body("\nTo make it: analyze them in this chapter first (above) — the book analysis is built from the chapters' analyses and summaries.", muted: true));
        else
            buttons.Add(AiButton(whole == null ? "Analyze across the book (AI)…" : "Analyze the book again (AI)…",
                                 "Reads every chapter they are in — its summary and their analysis there — and the plotlines", () => AnalyzeCharacterInBook(c)));
        p.Children.Add(Card("In the book", bookBody, buttons.ToArray()));
        return Scroll(p);
    }

    // ── In the book ───────────────────────────────────────────────────────────

    /// <summary>The book analysis and the chapter analyses under it: in the panel, and on the character's page.</summary>
    private StackPanel BookCharacterView(Character c, bool inPanel)
    {
        var p = new StackPanel { Margin = inPanel ? new Thickness(14, 10, 14, 16) : new Thickness(0) };
        var whole = CharacterAnalyses.InBook(_book!, c.Id);
        var chapters = CharacterAnalyses.ChaptersOf(_book!, c);
        var analysed = CharacterAnalyses.ByChapter(_book!, c.Id).ToDictionary(x => x.Chapter.Id, x => x.Analysis);

        UIElement body = whole != null
            ? SectionsOf(whole, CharacterAnalyses.BookHeadings)
            : Body($"No book analysis yet. The AI reads every chapter {c.Name} is in — the chapter's summary, and their analysis in that chapter where there is one — " +
                   "and the book's plotlines, and writes:\n• Summary — who they are as the book presents them\n• Story arc — how they progress over the chapters\n" +
                   "• Plotline interactions — what they do to each thread, and what it does to them", muted: true);
        if (whole != null && Newer(c, whole) is var newer && newer > 0 && body is StackPanel sp)
            sp.Children.Insert(1, Body($"{newer} chapter analys{(newer == 1 ? "is is" : "es are")} newer than this — analyze the book again to include {(newer == 1 ? "it" : "them")}.", muted: true));
        if (BookAnalysisBlocked(c) is { } why)
        {
            var withWhy = new StackPanel();
            withWhy.Children.Add(body);
            withWhy.Children.Add(Body("\n" + why, muted: true));
            p.Children.Add(Card("In the book", withWhy));
        }
        else
            p.Children.Add(Card("In the book", body, AiButton(whole == null ? "Analyze across the book (AI)…" : "Analyze the book again (AI)…",
                "Reads every chapter they are in — its summary and their analysis there — and the plotlines; kept with the character",
                () => AnalyzeCharacterInBook(c), primary: whole == null)));

        var each = new StackPanel();
        int summarised = chapters.Count(ch => _book!.SummaryText(ch).Length > 0);
        each.Children.Add(Body(chapters.Count == 0
            ? $"No chapter lists {c.Name} yet. Open a chapter in Characters mode, click their name and analyze them there, or list them on the Characters chart."
            : $"{c.Name} is in {chapters.Count} chapter{(chapters.Count == 1 ? "" : "s")}: {analysed.Count} analyzed for them, {summarised} with a summary. " +
              "The book analysis is built from these, so the more chapters are analyzed, the fuller the arc.", muted: true));
        foreach (var ch in chapters)
        {
            var target = ch;
            var link = Palette.LinkText($"{_book!.ChapterNumber(ch)}. {ch.Title}", () => OpenCharacterInChapter(target, c),
                                        $"Open this chapter in Characters mode with {c.Name} beside it");
            link.Margin = new Thickness(0, 8, 0, 0);
            link.FontWeight = FontWeights.SemiBold;
            each.Children.Add(link);
            if (analysed.TryGetValue(ch.Id, out var a))
            {
                var role = CharacterAnalyses.Part(a.Body, CharacterAnalyses.ChapterHeadings, CharacterAnalyses.Role);
                each.Children.Add(Body(Shorten(role.Length > 0 ? role : a.Body, 400)));
            }
            else each.Children.Add(Body("Not analyzed in this chapter yet.", muted: true));
        }
        p.Children.Add(Card("In each chapter", each));
        p.Children.Add(StoryCard(c));
        return p;
    }

    /// <summary>Why the book analysis cannot be made yet (nothing to build it from), or null when it can.</summary>
    private string? BookAnalysisBlocked(Character c)
    {
        var chapters = CharacterAnalyses.ChaptersOf(_book!, c);
        if (chapters.Count == 0)
            return $"No chapter lists {c.Name} yet, so there is nothing to build the book analysis from. Open a chapter they are in, switch to Characters mode (Ctrl+4) " +
                   "and click their name to analyze them there — or list them on the Characters chart (the Characters page).";
        if (chapters.All(ch => _book!.SummaryText(ch).Length == 0 && CharacterAnalyses.InChapter(_book, c.Id, ch.Id) == null))
            return $"None of the {chapters.Count} chapter(s) {c.Name} is in has a summary or an analysis of them yet. Analyze them in a chapter (Characters mode, click their name), " +
                   "or write the chapters' summaries (Analysis mode).";
        return null;
    }

    /// <summary>How many of the character's chapter analyses were made after this book analysis.</summary>
    private int Newer(Character c, Item bookAnalysis) =>
        CharacterAnalyses.ByChapter(_book!, c.Id).Count(x => x.Analysis.CreatedUtc > bookAnalysis.CreatedUtc);

    /// <summary>Opens a chapter in Characters mode with the character in the panel, on what the chapter shows of them.</summary>
    private void OpenCharacterInChapter(Chapter ch, Character c)
    {
        if (_mode != EditorMode.Characters) { _mode = EditorMode.Characters; UpdateModeMenu(); }
        if (ReferenceEquals(_current, ch)) { CommitCurrent(); ShowCurrent(); }
        else SelectNode(ch);
        if (!ReferenceEquals(_current, ch)) return;
        _panelCharacterId = c.Id; _panelCandidate = null; _mentionAt = -1;
        _characterView = CharacterView.Chapter;
        RefreshMarks();
        ShowSidePanel();
    }

    // ── The AI ────────────────────────────────────────────────────────────────

    /// <summary>What the author and the book analysis say about the character, for the prompts.</summary>
    private string KnownAbout(Character c, bool withBookAnalysis)
    {
        string F(string label, string field) { var b = Bullets.Parse(field); return b.Count == 0 ? "" : $"{label}: {string.Join("; ", b.Select(x => x.Text))}\n"; }
        var known = F("Role", c.Role) + F("Origin", c.Origin) + F("Physical description", c.PhysicalDescription) + F("Personality", c.Personality) +
                    F("Motivations", c.Motivations) + F("Actions", c.Actions) + F("Arc", c.Arc);
        if (withBookAnalysis && CharacterAnalyses.InBook(_book!, c.Id) is { } whole)
        {
            var summary = CharacterAnalyses.Part(whole.Body, CharacterAnalyses.BookHeadings, CharacterAnalyses.Summary);
            if (summary.Length > 0) known += "From the book analysis:\n" + summary + "\n";
        }
        return known.Trim().Length > 0 ? known.Trim() : "(nothing written yet)";
    }

    private static string AliasesOf(Character c)
    {
        var a = Bullets.Parse(c.Aliases).Select(b => b.Text).ToList();
        return a.Count > 0 ? string.Join("; ", a) : "(none)";
    }

    private void AnalyzeCharacterInChapter(Chapter ch, Character c)
    {
        if (_book == null) return;
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();
        var text = ChapterText(ch);
        if (text.Trim().Length == 0)
        {
            MessageBox.Show(this, "This chapter has no text yet, so there is nothing to analyze.", "Analyze character", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var values = new
        {
            book = _book.Title, chapter = $"{_book.ChapterNumber(ch)}. {ch.Title}", character = c.Name, aliases = AliasesOf(c),
            known = KnownAbout(c, withBookAnalysis: true), appearance = CharacterAnalyses.Appearance(_book, c, ch), text
        };
        var dlg = new AiRunWindow(_ai, _book, ch.Id, ItemKind.CharacterInChapter, $"{c.Name} in \"{ch.Title}\"", $"{c.Name} in this chapter", AiPrompts.CharacterInChapter,
            $"{c.Name} as this chapter presents them: their role here (and whether they are introduced), how they are described, their motivations and actions. " +
            "It is shown beside the chapter in Characters mode and on their page. The prompt is \"character-in-chapter\" in AI › Prompt Library.",
            provider => _ai.Prompts.Get(AiPrompts.CharacterInChapter).Bind(values, provider), ConfirmSend,
            nextLabel: "Next: see it beside the chapter →") { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        foreach (var item in dlg.Created) item.CharacterIds = new List<Guid> { c.Id };
        MarkDirty();
        AfterCharacterAnalysis(c, CharacterView.Chapter);
        UpdateStatus($"{c.Name} analyzed in \"{ch.Title}\".");
    }

    private void AnalyzeCharacterInBook(Character c)
    {
        if (_book == null) return;
        var chapters = CharacterAnalyses.ChaptersOf(_book, c);
        if (BookAnalysisBlocked(c) is { } why)
        {
            MessageBox.Show(this, why, "Analyze across the book", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!EnsureAiOrExplain()) return;
        CommitCurrent();

        var parts = chapters.Select(ch =>
        {
            var role = c.RoleIn(ch.Id);
            var head = $"Chapter {_book.ChapterNumber(ch)}. {ch.Title}" + (role != null ? $" ({Palette.RoleWord(role.Value).ToLowerInvariant()})" : "") + (ch.PovCharacterId == c.Id ? " — their point of view" : "");
            var summary = _book.SummaryText(ch);
            var mine = CharacterAnalyses.InChapter(_book, c.Id, ch.Id);
            return head + "\nSummary: " + (summary.Length > 0 ? summary : "(none)") + (mine != null ? $"\n{c.Name} in this chapter:\n{mine.Body.Trim()}" : "");
        });
        var plotlines = _book.Plotlines.Count == 0 ? "(none)" : string.Join("\n", _book.Plotlines.Select(pl =>
        {
            var runs = _book.Chapters.Where(ch => pl.ChapterIds.Contains(ch.Id)).Select(ch => $"{_book.ChapterNumber(ch)}. {ch.Title}").ToList();
            var summary = string.Join(" ", Bullets.Parse(pl.Summary).Select(b => b.Text));
            return $"- {pl.Name} — {(summary.Length > 0 ? Shorten(summary, 400) : "(no summary)")} — {(runs.Count > 0 ? string.Join("; ", runs) : "not in any chapter yet")}";
        }));
        var values = new
        {
            book = _book.Title, synopsis = _book.Synopsis.Length > 0 ? _book.Synopsis : "(none)", character = c.Name, aliases = AliasesOf(c),
            known = KnownAbout(c, withBookAnalysis: false), chapters = string.Join("\n\n", parts), plotlines
        };
        var dlg = new AiRunWindow(_ai, _book, c.Id, ItemKind.CharacterInBook, $"{c.Name} across the book", $"{c.Name} in the book", AiPrompts.CharacterInBook,
            $"{c.Name} across the whole book: a summary as the book presents them, their story arc over the chapters, and the plotlines they touch. " +
            $"It reads the {chapters.Count} chapter(s) they are in — each summary, and their analysis in that chapter where there is one — not the chapters' full text. " +
            "The prompt is \"character-in-book\" in AI › Prompt Library.",
            provider => _ai.Prompts.Get(AiPrompts.CharacterInBook).Bind(values, provider), ConfirmSend,
            nextLabel: "Next: see the analysis →") { Owner = this };
        dlg.ShowDialog();
        if (dlg.Created.Count == 0) return;
        MarkDirty();
        AfterCharacterAnalysis(c, CharacterView.Book);
        UpdateStatus($"{c.Name} analyzed across the book.");
    }

    /// <summary>Shows the new analysis where the author asked for it: beside the chapter, or on the character's page.</summary>
    private void AfterCharacterAnalysis(Character c, CharacterView view)
    {
        if (_current is Chapter && _side != null && _mode == EditorMode.Characters)
        {
            _panelCharacterId = c.Id; _panelCandidate = null;
            _characterView = view;
            ShowSidePanel();
        }
        else if (ReferenceEquals(_current, c))
        {
            ShowCurrent();
            var head = _characterAnalysisHead;
            head?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => head.BringIntoView());
        }
    }
}
