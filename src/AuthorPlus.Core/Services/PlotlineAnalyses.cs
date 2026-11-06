using System.Text.RegularExpressions;
using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services;

/// <summary>
/// The AI's analyses of a plotline: in one chapter (<see cref="ItemKind.PlotlineInChapter"/>, owned
/// by the chapter) and across the book (<see cref="ItemKind.PlotlineInBook"/>, owned by the plotline).
/// A chapter analysis quotes the thread's key moments, which Plotlines mode marks in the text.
/// </summary>
public static class PlotlineAnalyses
{
    public const string Role = "ROLE", WhatHappens = "WHAT HAPPENS", Characters = "CHARACTERS", KeyMoments = "KEY MOMENTS";
    public const string Summary = "SUMMARY", Progression = "PROGRESSION", Convergences = "CONVERGENCES";

    /// <summary>The headings of a chapter analysis, in order.</summary>
    public static readonly IReadOnlyList<string> ChapterHeadings = new[] { Role, WhatHappens, Characters, KeyMoments };
    /// <summary>The headings of a book analysis, in order.</summary>
    public static readonly IReadOnlyList<string> BookHeadings = new[] { Summary, Progression, Characters, Convergences };

    /// <summary>The latest analysis of the plotline in the chapter, or null.</summary>
    public static Item? InChapter(Book book, Guid plotlineId, Guid chapterId) =>
        book.Items.Where(i => i.Kind == ItemKind.PlotlineInChapter && i.OwnerId == chapterId && i.PlotlineIds.Contains(plotlineId))
                  .OrderByDescending(i => i.CreatedUtc).FirstOrDefault();

    /// <summary>The latest analysis of the plotline across the book, or null.</summary>
    public static Item? InBook(Book book, Guid plotlineId) =>
        book.Items.Where(i => i.Kind == ItemKind.PlotlineInBook && i.OwnerId == plotlineId)
                  .OrderByDescending(i => i.CreatedUtc).FirstOrDefault();

    /// <summary>The chapters, in reading order, that have an analysis of the plotline, each with its latest.</summary>
    public static IReadOnlyList<(Chapter Chapter, Item Analysis)> ByChapter(Book book, Guid plotlineId) =>
        book.Chapters.Select(ch => (ch, InChapter(book, plotlineId, ch.Id))).Where(x => x.Item2 != null).Select(x => (x.ch, x.Item2!)).ToList();

    /// <summary>The chapters the thread runs through, in reading order (on either side's list, or analysed there).</summary>
    public static IReadOnlyList<Chapter> ChaptersOf(Book book, Plotline p) =>
        book.Chapters.Where(ch => p.ChapterIds.Contains(ch.Id) || ch.PlotlineIds.Contains(p.Id) || InChapter(book, p.Id, ch.Id) != null).ToList();

    /// <summary>The plotlines that run through the chapter, in the book's order.</summary>
    public static IReadOnlyList<Plotline> In(Book book, Chapter chapter) =>
        book.Plotlines.Where(p => p.ChapterIds.Contains(chapter.Id) || chapter.PlotlineIds.Contains(p.Id)).ToList();

    private static readonly Regex Quoted = new("[\"“]([^\"”]{12,})[\"”]");
    private const string QuoteMarks = "\"“”";

    /// <summary>
    /// The key moments of a chapter analysis, each as the ways to find it in the chapter, best first:
    /// the whole quoted passage (dialogue inside it kept), then each quotation in the point on its own.
    /// A point that quotes nothing is its own text. The App marks the first way that is found in the text.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> MomentChoices(string? body)
    {
        var part = CharacterAnalyses.Part(body, ChapterHeadings, KeyMoments);
        var found = new List<IReadOnlyList<string>>();
        foreach (var raw in part.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Replace("\\\"", "\"").Trim().TrimStart('-', '*', '•').Trim();
            if (line.Length == 0) continue;
            var choices = new List<string>();
            // The quote, before the note on why it matters ("… — the clue").
            var quote = line;
            foreach (var sep in new[] { " — ", " – ", " -- ", " - " })
            {
                int cut = line.LastIndexOf(sep, StringComparison.Ordinal);
                if (cut > 0 && QuoteMarks.Contains(line[..cut].TrimEnd()[^1])) { quote = line[..cut].TrimEnd(); break; }
            }
            if (quote.Length > 2 && QuoteMarks.Contains(quote[0]) && QuoteMarks.Contains(quote[^1]))
                choices.Add(Clean(quote[1..^1]));
            choices.AddRange(Quoted.Matches(line).Select(m => Clean(m.Groups[1].Value)));
            if (choices.Count == 0 && line.Length >= 12) choices.Add(line);
            var list = choices.Where(c => c.Length >= 12).Distinct().ToList();
            if (list.Count > 0) found.Add(list);
        }
        return found;
    }

    private static string Clean(string q) => q.Trim().TrimEnd('…').Trim();

    /// <summary>Every way of finding every key moment, flattened (see <see cref="MomentChoices"/>).</summary>
    public static IReadOnlyList<string> Moments(string? body) => MomentChoices(body).SelectMany(c => c).Distinct().ToList();
}
