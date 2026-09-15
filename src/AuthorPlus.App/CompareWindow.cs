using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Versioning;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace AuthorPlus.App;

/// <summary>
/// Two versions of a chapter, read as one text: paragraphs that did not change stand as they are,
/// a paragraph that went is struck through on red, the one that replaced it is on green — and
/// where one paragraph clearly became another, only the words that moved are marked, the same way
/// a rewrite suggestion shows a change.
///
/// Deliberately not two columns: a novelist reads a chapter, and two columns of prose side by
/// side are read by nobody.
/// </summary>
public sealed class CompareWindow : Window
{
    public CompareWindow(string title, string leftLabel, string rightLabel, string? beforeXaml, string? afterXaml)
    {
        Title = "Compare — " + title;
        Width = 860; Height = 700; MinWidth = 520; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var paragraphs = TextDiff.Chapters(beforeXaml, afterXaml);
        var (added, removed) = TextDiff.Counts(paragraphs);

        var root = new DockPanel { Margin = new Thickness(16) };

        var heading = new TextBlock { Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap };
        heading.Inlines.Add(new Run(title) { FontWeight = FontWeights.SemiBold, FontSize = 15 });
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var note = new TextBlock
        {
            Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            Text = added == 0 && removed == 0
                ? $"Nothing changed between {leftLabel} and {rightLabel}."
                : $"On red, {leftLabel}; on green, {rightLabel}. " +
                  $"{Count(removed, "paragraph")} out, {Count(added, "paragraph")} in."
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);

        var flow = new FlowDocument
        {
            FontFamily = new FontFamily("Georgia"), FontSize = 15, LineHeight = 22,
            PagePadding = new Thickness(12), Background = Brushes.White, ColumnWidth = double.MaxValue
        };

        for (int i = 0; i < paragraphs.Count; i++)
        {
            // A paragraph taken out and one put in, one after the other, is a rewrite: show both,
            // with only the words that moved marked.
            if (paragraphs[i].Kind == DiffKind.Removed && i + 1 < paragraphs.Count && paragraphs[i + 1].Kind == DiffKind.Added)
            {
                var words = WordDiff.Compute(paragraphs[i].Text, paragraphs[i + 1].Text);
                flow.Blocks.Add(Line(WordDiff.Left(words), "was"));
                flow.Blocks.Add(Line(WordDiff.Right(words), "now"));
                i++;
                continue;
            }

            var piece = paragraphs[i];
            flow.Blocks.Add(Line(new[] { piece }, piece.Kind switch
            {
                DiffKind.Removed => "taken out",
                DiffKind.Added   => "put in",
                _                => null
            }));
        }

        root.Children.Add(new FlowDocumentScrollViewer
        {
            Document = flow, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(1),
            BorderBrush = Palette.Line, Background = Brushes.White
        });

        Content = root;
    }

    /// <summary>
    /// One paragraph of the comparison. The marking is on the words, never on the whole line, so a
    /// paragraph where one clause changed reads as a paragraph and not as a block of colour; the
    /// grey word in the margin says which side of the comparison the line came from.
    /// </summary>
    private static Paragraph Line(IEnumerable<DiffPiece> pieces, string? label)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
        if (label is not null)
            paragraph.Inlines.Add(new Run(label + "   ")
            {
                Foreground = Brushes.Gray, FontSize = 11, FontFamily = new FontFamily("Segoe UI")
            });

        foreach (var piece in pieces)
        {
            var run = new Run(piece.Text);
            if (piece.Kind == DiffKind.Removed)
            {
                run.Background = Palette.RemovedBg;
                run.TextDecorations = TextDecorations.Strikethrough;
                run.Foreground = Brushes.DimGray;
            }
            else if (piece.Kind == DiffKind.Added)
            {
                run.Background = Palette.AddedBg;
            }
            paragraph.Inlines.Add(run);
        }
        return paragraph;
    }

    private static string Count(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
}
