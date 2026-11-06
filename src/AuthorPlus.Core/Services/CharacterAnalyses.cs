using System.Text.RegularExpressions;
using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services;

/// <summary>
/// The AI's analyses of a character: in one chapter (<see cref="ItemKind.CharacterInChapter"/>,
/// owned by the chapter) and across the book (<see cref="ItemKind.CharacterInBook"/>, owned by the
/// character). Finds the latest of each and splits a reply into its headed parts.
/// </summary>
public static class CharacterAnalyses
{
    public const string Role = "ROLE", Description = "DESCRIPTION", MotivationsAndActions = "MOTIVATIONS AND ACTIONS";
    public const string Summary = "SUMMARY", StoryArc = "STORY ARC", PlotlineInteractions = "PLOTLINE INTERACTIONS";

    /// <summary>The headings of a chapter analysis, in order.</summary>
    public static readonly IReadOnlyList<string> ChapterHeadings = new[] { Role, Description, MotivationsAndActions };
    /// <summary>The headings of a book analysis, in order.</summary>
    public static readonly IReadOnlyList<string> BookHeadings = new[] { Summary, StoryArc, PlotlineInteractions };

    /// <summary>
    /// The reply split at the given headings ("ROLE:", "**Role**", "## Role:" all count; text after the
    /// colon on the heading line belongs to it). Headings come back in the order given, each once,
    /// only those found. A reply with none of them comes back whole under "".
    /// </summary>
    public static IReadOnlyList<(string Heading, string Text)> Sections(string? body, IEnumerable<string> headings)
    {
        var wanted = headings.ToList();
        var text = (body ?? "").Replace("\r\n", "\n");
        var pattern = @"^[ \t]*(?:#+[ \t]*)?(?:\*\*)?(" + string.Join("|", wanted.Select(Regex.Escape)) + @")(?:\*\*)?[ \t]*:?[ \t]*(?:\*\*)?[ \t]*(.*)$";
        var matches = Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (matches.Count == 0) return text.Trim().Length == 0 ? Array.Empty<(string, string)>() : new[] { ("", text.Trim()) };

        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var start = m.Index + m.Length;
            var part = (m.Groups[2].Value + text[start..end]).Trim();
            var key = wanted.First(h => h.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
            found[key] = found.TryGetValue(key, out var before) && before.Length > 0 ? before + "\n" + part : part;
        }
        return wanted.Where(found.ContainsKey).Select(h => (h, found[h])).ToList();
    }

    /// <summary>The text under one heading, or "" when the reply has none.</summary>
    public static string Part(string? body, IEnumerable<string> headings, string heading) =>
        Sections(body, headings).FirstOrDefault(s => s.Heading.Equals(heading, StringComparison.OrdinalIgnoreCase)).Text ?? "";

    /// <summary>The latest analysis of the character in the chapter, or null.</summary>
    public static Item? InChapter(Book book, Guid characterId, Guid chapterId) =>
        book.Items.Where(i => i.Kind == ItemKind.CharacterInChapter && i.OwnerId == chapterId && i.CharacterIds.Contains(characterId))
                  .OrderByDescending(i => i.CreatedUtc).FirstOrDefault();

    /// <summary>The latest analysis of the character across the book, or null.</summary>
    public static Item? InBook(Book book, Guid characterId) =>
        book.Items.Where(i => i.Kind == ItemKind.CharacterInBook && i.OwnerId == characterId)
                  .OrderByDescending(i => i.CreatedUtc).FirstOrDefault();

    /// <summary>The chapters, in reading order, that have an analysis of the character, each with its latest.</summary>
    public static IReadOnlyList<(Chapter Chapter, Item Analysis)> ByChapter(Book book, Guid characterId) =>
        book.Chapters.Select(ch => (ch, InChapter(book, characterId, ch.Id))).Where(x => x.Item2 != null).Select(x => (x.ch, x.Item2!)).ToList();

    /// <summary>
    /// The chapters a book analysis draws on, in reading order: those that list the character, and
    /// those with an analysis of them.
    /// </summary>
    public static IReadOnlyList<Chapter> ChaptersOf(Book book, Character c) =>
        book.Chapters.Where(ch => ch.CharacterIds.Contains(c.Id) || c.RoleIn(ch.Id) != null || InChapter(book, c.Id, ch.Id) != null).ToList();

    /// <summary>
    /// What the book says about where the character first appears, for the chapter analysis to judge
    /// whether they are introduced in <paramref name="chapter"/>.
    /// </summary>
    public static string Appearance(Book book, Character c, Chapter chapter)
    {
        var listed = book.Chapters.Where(ch => ch.CharacterIds.Contains(c.Id) || c.RoleIn(ch.Id) != null).ToList();
        if (c.RoleIn(chapter.Id) == CharacterRole.Introduced) return "The author has marked this chapter as the one where they are introduced.";
        if (listed.Count == 0) return "No chapter lists them yet, so it is not known where they first appear.";
        int here = book.Chapters.IndexOf(chapter);
        var earlier = listed.Where(ch => book.Chapters.IndexOf(ch) < here).ToList();
        if (earlier.Count == 0)
            return listed.Contains(chapter) ? "This is the first chapter that lists them." : "No earlier chapter lists them.";
        return "Earlier chapters that list them: " + string.Join("; ", earlier.Select(ch => $"{book.ChapterNumber(ch)}. {ch.Title}")) + " — so they are not new here.";
    }
}
