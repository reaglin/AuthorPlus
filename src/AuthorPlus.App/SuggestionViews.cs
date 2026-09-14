using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace AuthorPlus.App;

/// <summary>What the suggestion screens need from the main window to act on a suggestion.</summary>
public interface ISuggestionActions
{
    /// <summary>Replaces the passage in the chapter with <paramref name="replacement"/>. False when the passage cannot be found.</summary>
    bool Apply(Chapter chapter, SuggestionEntry entry, string replacement);
    /// <summary>Opens the chapter and selects the passage. False when it cannot be found.</summary>
    bool GoTo(Chapter chapter, SuggestionEntry entry);
    /// <summary>Something on an item changed: save state, refresh labels.</summary>
    void Changed(Item item);
    /// <summary>Opens the "Ask again" form for this suggestion and, if the author asks, runs the guided rewrite.</summary>
    void AskAgain(Chapter chapter, Item item, SuggestionEntry entry);
    /// <summary>Throws this suggestion away (the caller confirms first).</summary>
    void Dismiss(Item item, SuggestionEntry entry);
    /// <summary>Rebuilds the screen showing <paramref name="item"/> (after new suggestions arrive, or one is dismissed).</summary>
    void Refresh(Item item);
}

/// <summary>
/// The Suggestions item, organised by passage. One block per passage: the passage itself once at
/// the top, then a collapsible frame per suggestion for it — including the ones "Ask again"
/// produced, which sit with their siblings rather than repeating the passage. A frame shows the
/// change as a single marked-up text (struck through where words go, highlighted where they
/// arrive), the reasoning, what was asked for if the author guided it, and the actions.
/// </summary>
public static class SuggestionsEditor
{
    private static readonly Brush RemovedBg = new SolidColorBrush(Color.FromRgb(0xFB, 0xD9, 0xD9));
    private static readonly Brush AddedBg   = new SolidColorBrush(Color.FromRgb(0xD3, 0xF3, 0xDB));
    private static readonly Brush BlockBg   = new SolidColorBrush(Color.FromRgb(0xF7, 0xF9, 0xFB));
    private static readonly Brush Line      = new SolidColorBrush(Color.FromRgb(0xD8, 0xDE, 0xE6));

    public static UIElement Build(Item item, Chapter? chapter, ISuggestionActions actions, bool aiAvailable)
    {
        if (item.Suggestions.Count == 0) item.Suggestions = SuggestionParser.Parse(item.Body);
        var stack = new StackPanel();

        if (item.Suggestions.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = "This reply did not come back in the suggestion format, so it is shown as text. Run Suggestions… again for the passage-by-passage view.", Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBox { Text = item.Body, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent });
            return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        stack.Children.Add(new TextBlock
        {
            Text = "One block per passage. Open a suggestion to see what it changes and what it is for. Apply puts it into the chapter; Mark keeps the passage for your own rewrite; " +
                   "Ask again… is the one to reach for when a suggestion misses what you were going for — you say what the passage is doing and it tries again, and the new ones join this block. " +
                   "Dismiss throws one away.",
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });

        int n = 0;
        foreach (var group in GroupByPassage(item.Suggestions))
        {
            n++;
            stack.Children.Add(PassageBlock(item, chapter, group, actions, $"Passage {n}", showChapterTitle: false));
        }
        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>Suggestions that address the same passage, in the order they were added.</summary>
    public static IEnumerable<List<SuggestionEntry>> GroupByPassage(IEnumerable<SuggestionEntry> entries)
    {
        var groups = new List<List<SuggestionEntry>>();
        foreach (var e in entries)
        {
            var key = Key(e.Original);
            var group = groups.FirstOrDefault(g => Key(g[0].Original) == key);
            if (group == null) groups.Add(new List<SuggestionEntry> { e });
            else group.Add(e);
        }
        return groups;

        static string Key(string s) => new string(s.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).ToArray()).ToLowerInvariant();
    }

    /// <summary>One passage with every suggestion made for it.</summary>
    public static Border PassageBlock(Item item, Chapter? chapter, List<SuggestionEntry> group, ISuggestionActions actions, string label, bool showChapterTitle)
    {
        var block = new Border { Background = BlockBg, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 14) };
        var body = new StackPanel();
        block.Child = body;

        var frames = new List<Expander>();

        // Header: which passage, how many suggestions, and the passage-level actions.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(buttons, Dock.Right);
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        title.Text = (showChapterTitle && chapter != null ? chapter.Title + " — " : "") + label +
                     $"   ({group.Count} suggestion{(group.Count == 1 ? "" : "s")}" +
                     $"{Counts(group)})";
        header.Children.Add(buttons);
        header.Children.Add(title);
        body.Children.Add(header);

        var goTo = new Button { Content = "Go to passage", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0), IsEnabled = chapter != null, ToolTip = "Open the chapter and select this passage" };
        goTo.Click += (_, _) => { if (chapter != null && !actions.GoTo(chapter, group[0])) MessageBox.Show("That passage is not in the chapter any more — it may already have been rewritten.", "Go to passage", MessageBoxButton.OK, MessageBoxImage.Information); };
        var toggle = new Button { Content = group.Count > 1 ? "Expand all" : "Collapse", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
        toggle.Click += (_, _) =>
        {
            bool expand = frames.Any(f => f.IsExpanded == false);
            foreach (var f in frames) f.IsExpanded = expand;
            toggle.Content = expand ? "Collapse all" : "Expand all";
        };
        buttons.Children.Add(goTo);
        buttons.Children.Add(toggle);

        // The passage itself, once.
        body.Children.Add(new TextBlock { Text = "The passage as it stands", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
        body.Children.Add(new TextBox
        {
            Text = group[0].Original, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.White,
            BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(8), FontFamily = new FontFamily("Georgia"), FontSize = 14,
            MaxHeight = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 8)
        });

        // One collapsible frame per suggestion.
        foreach (var e in group)
        {
            var frame = Frame(item, chapter, e, actions, expanded: group.Count == 1);
            frames.Add(frame);
            body.Children.Add(frame);
        }
        return block;
    }

    private static string Counts(List<SuggestionEntry> group)
    {
        var applied = group.Count(s => s.Status == SuggestionStatus.Applied);
        var marked = group.Count(s => s.Status == SuggestionStatus.Marked);
        var resolved = group.Count(s => s.Status == SuggestionStatus.Resolved);
        var parts = new List<string>();
        if (applied > 0) parts.Add($"{applied} applied");
        if (marked > 0) parts.Add($"{marked} marked");
        if (resolved > 0) parts.Add($"{resolved} resolved");
        return parts.Count == 0 ? "" : ", " + string.Join(", ", parts);
    }

    /// <summary>One suggestion: a collapsible frame that never repeats the passage.</summary>
    public static Expander Frame(Item item, Chapter? chapter, SuggestionEntry e, ISuggestionActions actions, bool expanded)
    {
        var headerText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = Brushes.Gray, FontSize = 11 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, MaxWidth = 780 };
        head.Children.Add(headerText);
        head.Children.Add(status);

        var frame = new Expander
        {
            Header = head, IsExpanded = expanded, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(0, 2, 0, 6),
            Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1)
        };

        var body = new StackPanel { Margin = new Thickness(10, 4, 8, 4) };
        frame.Content = body;

        // What the author asked for, when this one came from "Ask again".
        if (e.IsRefinement && (e.Intent.Length > 0 || e.AuthorRequest.Length > 0))
        {
            var asked = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), Background = new SolidColorBrush(Color.FromRgb(0xF2, 0xF6, 0xFF)), Padding = new Thickness(6) };
            asked.Inlines.Add(new Run("You asked for: ") { FontWeight = FontWeights.SemiBold });
            if (e.Intent.Length > 0) asked.Inlines.Add(new Run(e.Intent));
            if (e.Intent.Length > 0 && e.AuthorRequest.Length > 0) asked.Inlines.Add(new Run("  —  "));
            if (e.AuthorRequest.Length > 0) asked.Inlines.Add(new Run(e.AuthorRequest) { FontStyle = FontStyles.Italic });
            body.Children.Add(asked);
        }

        // The change itself, marked up in one text rather than two columns.
        body.Children.Add(new TextBlock { Text = "The change", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
        body.Children.Add(UnifiedDiff(WordDiff.Compute(e.Original, e.Rewrite)));

        if (e.Why.Length > 0)
        {
            var why = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            why.Inlines.Add(new Run("Why: ") { FontWeight = FontWeights.SemiBold });
            why.Inlines.Add(new Run(e.Why));
            body.Children.Add(why);
        }

        // The author's own rewrite, for a marked passage.
        var mine = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = e.Status == SuggestionStatus.Marked ? Visibility.Visible : Visibility.Collapsed };
        mine.Children.Add(new TextBlock { Text = "Your rewrite (in your own words) — Apply mine puts this into the chapter in place of the passage", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap });
        var mineBox = new TextBox { Text = e.AuthorRewrite, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinLines = 2, Padding = new Thickness(6), FontFamily = new FontFamily("Georgia"), FontSize = 14 };
        mineBox.TextChanged += (_, _) => { e.AuthorRewrite = mineBox.Text; actions.Changed(item); };
        mine.Children.Add(mineBox);
        var applyMine = new Button { Content = "Apply mine", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        mine.Children.Add(applyMine);
        body.Children.Add(mine);

        // Actions for this suggestion.
        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var askAgain = new Button { Content = "Ask again…", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0), FontWeight = FontWeights.SemiBold,
                                    ToolTip = "Tell the AI what this passage is meant to convey — the mood, the joke, the threat — and get fresh rewrites aimed at that" };
        var apply   = new Button { Content = "Apply", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Put this text into the chapter in place of the passage" };
        var mark    = new Button { Content = "Mark", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Keep this passage to rewrite in your own words (see Marked Passages)" };
        var resolve = new Button { Content = "Resolve", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Done with this one" };
        var reopen  = new Button { Content = "Reopen", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        var dismiss = new Button { Content = "Dismiss", Padding = new Thickness(10, 3, 10, 3), ToolTip = "Delete this suggestion" };
        actionRow.Children.Add(askAgain); actionRow.Children.Add(apply); actionRow.Children.Add(mark);
        actionRow.Children.Add(resolve); actionRow.Children.Add(reopen); actionRow.Children.Add(dismiss);
        body.Children.Add(actionRow);

        var note = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        body.Children.Add(note);

        void Paint()
        {
            var preview = e.Rewrite.Replace("\n", " ");
            if (preview.Length > 90) preview = preview[..90].TrimEnd() + "…";
            headerText.Text = $"Suggestion {e.Index}{(e.IsRefinement ? " (asked again)" : "")} — {preview}";
            status.Text = e.Status switch
            {
                SuggestionStatus.Applied  => $"✓ applied {e.ActedUtc?.ToLocalTime():MMM d}",
                SuggestionStatus.Marked   => "marked for your own rewrite",
                SuggestionStatus.Resolved => $"resolved {e.ActedUtc?.ToLocalTime():MMM d}",
                _ => ""
            };
            headerText.FontWeight = e.Status == SuggestionStatus.Open ? FontWeights.Normal : FontWeights.Normal;
            var open = e.Status == SuggestionStatus.Open;
            apply.IsEnabled = chapter != null && open;
            mark.IsEnabled = chapter != null && open;
            askAgain.IsEnabled = chapter != null && e.Status != SuggestionStatus.Applied;
            resolve.Visibility = e.Status == SuggestionStatus.Resolved ? Visibility.Collapsed : Visibility.Visible;
            reopen.Visibility = e.Status == SuggestionStatus.Resolved ? Visibility.Visible : Visibility.Collapsed;
            mine.Visibility = e.Status == SuggestionStatus.Marked ? Visibility.Visible : Visibility.Collapsed;
            frame.Opacity = e.Status is SuggestionStatus.Resolved or SuggestionStatus.Applied ? 0.65 : 1.0;
        }

        void Failed() { note.Text = "That passage is not in the chapter any more (it may already have been rewritten). Use Go to passage to check."; note.Visibility = Visibility.Visible; }

        apply.Click += (_, _) =>
        {
            if (chapter == null) return;
            if (!actions.Apply(chapter, e, e.Rewrite)) { Failed(); return; }
            e.Status = SuggestionStatus.Applied; e.ActedUtc = DateTime.UtcNow; actions.Changed(item); Paint();
        };
        applyMine.Click += (_, _) =>
        {
            if (chapter == null || mineBox.Text.Trim().Length == 0) return;
            if (!actions.Apply(chapter, e, mineBox.Text.Trim())) { Failed(); return; }
            e.Status = SuggestionStatus.Applied; e.ActedUtc = DateTime.UtcNow; actions.Changed(item); Paint();
        };
        mark.Click    += (_, _) => { e.Status = SuggestionStatus.Marked; e.ActedUtc = DateTime.UtcNow; actions.Changed(item); Paint(); frame.IsExpanded = true; mineBox.Focus(); };
        resolve.Click += (_, _) => { e.Status = SuggestionStatus.Resolved; e.ActedUtc = DateTime.UtcNow; actions.Changed(item); Paint(); };
        reopen.Click  += (_, _) => { e.Status = SuggestionStatus.Open; e.ActedUtc = null; actions.Changed(item); Paint(); };
        askAgain.Click += (_, _) => { if (chapter != null) actions.AskAgain(chapter, item, e); };
        dismiss.Click += (_, _) =>
        {
            if (MessageBox.Show($"Delete suggestion {e.Index}? This cannot be undone.", "Dismiss suggestion", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            actions.Dismiss(item, e);
        };
        Paint();
        return frame;
    }

    /// <summary>The change as one marked-up text: words that go struck through in red, words that arrive highlighted in green.</summary>
    private static TextBlock UnifiedDiff(IReadOnlyList<DiffPiece> pieces)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Georgia"), FontSize = 14,
            Background = Brushes.White, Padding = new Thickness(8)
        };
        foreach (var p in pieces)
        {
            var run = new Run(p.Text);
            switch (p.Kind)
            {
                case DiffKind.Removed:
                    run.Background = RemovedBg; run.TextDecorations = TextDecorations.Strikethrough; run.Foreground = Brushes.DimGray; break;
                case DiffKind.Added:
                    run.Background = AddedBg; break;
            }
            tb.Inlines.Add(run);
        }
        return tb;
    }
}

/// <summary>All marked passages of a chapter (or the book), each with its suggestion, reasoning, and the author's own rewrite box.</summary>
public sealed class MarkedPassagesWindow : Window
{
    public MarkedPassagesWindow(Book book, Chapter? chapter, ISuggestionActions actions)
    {
        Title = chapter == null ? "Marked passages — whole book" : $"Marked passages — {chapter.Title}";
        Width = 940; Height = 700; MinWidth = 640; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel { Margin = new Thickness(16) };
        var head = new TextBlock
        {
            Text = "Passages you marked to rewrite yourself. Each shows the passage, the suggestion that prompted you and its reasoning; write your version in the box and Apply mine, or Resolve when done. Go to passage opens the chapter there.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        var close = new Button { Content = "Close", Width = 90, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var stack = new StackPanel();
        int n = 0;
        foreach (var item in book.Items.Where(i => i.Kind == ItemKind.Suggestions))
        {
            var ch = ChapterOf(book, item);
            if (ch == null || (chapter != null && ch.Id != chapter.Id)) continue;
            foreach (var marked in item.Suggestions.Where(s => s.Status == SuggestionStatus.Marked))
            {
                n++;
                stack.Children.Add(SuggestionsEditor.PassageBlock(item, ch, new List<SuggestionEntry> { marked }, actions,
                    $"Marked passage {n}", showChapterTitle: chapter == null));
            }
        }
        if (n == 0) stack.Children.Add(new TextBlock { Text = "Nothing is marked. On a suggestion, click Mark to keep it here.", Foreground = Brushes.Gray });
        root.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    /// <summary>The chapter a Suggestions item belongs to: its owner is the Analysis item, whose owner is the chapter.</summary>
    public static Chapter? ChapterOf(Book book, Item suggestions)
    {
        var owner = suggestions.OwnerId;
        for (int hops = 0; hops < 4; hops++)
        {
            if (book.Chapters.FirstOrDefault(c => c.Id == owner) is { } ch) return ch;
            if (book.Items.FirstOrDefault(i => i.Id == owner) is { } parent) owner = parent.OwnerId; else return null;
        }
        return null;
    }
}

/// <summary>Finds a quoted passage inside a WPF FlowDocument (tolerant match via <see cref="PassageLocator"/>), for Apply and Go to passage.</summary>
public static class PassageInDocument
{
    /// <summary>
    /// The document's text runs are walked into one string with a map back to TextPointers, then
    /// <see cref="PassageLocator"/> does the tolerant match (whitespace, curly quotes, dashes),
    /// and the span maps back to pointers.
    /// </summary>
    public static (TextPointer Start, TextPointer End)? Locate(FlowDocument doc, string passage)
    {
        var sb = new System.Text.StringBuilder();
        var starts = new List<(int Index, TextPointer Pointer, int Length)>();
        for (var p = doc.ContentStart; p != null && p.CompareTo(doc.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            var ctx = p.GetPointerContext(LogicalDirection.Forward);
            if (ctx == TextPointerContext.ElementEnd && p.Parent is Paragraph) { sb.Append('\n'); continue; }
            if (ctx != TextPointerContext.Text) continue;
            var run = p.GetTextInRun(LogicalDirection.Forward);
            if (run.Length == 0) continue;
            starts.Add((sb.Length, p, run.Length));
            sb.Append(run);
        }
        var span = PassageLocator.Find(sb.ToString(), passage);
        if (span == null) return null;
        var start = ToPointer(starts, span.Start);
        var end = ToPointer(starts, span.Start + span.Length);
        return start != null && end != null ? (start, end) : null;
    }

    private static TextPointer? ToPointer(List<(int Index, TextPointer Pointer, int Length)> runs, int textIndex)
    {
        foreach (var (index, pointer, length) in runs)
            if (textIndex >= index && textIndex <= index + length) return pointer.GetPositionAtOffset(textIndex - index);
        // a position that falls on a paragraph break: snap to the next run's start
        foreach (var (index, pointer, _) in runs) if (index >= textIndex) return pointer;
        return runs.Count > 0 ? runs[^1].Pointer.GetPositionAtOffset(runs[^1].Length) : null;
    }
}
