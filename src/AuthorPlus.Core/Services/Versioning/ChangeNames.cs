using System.Text.Json;
using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services.Versioning;

/// <summary>
/// Turns the file names in a book folder back into the things the author knows:
/// <c>chapters/6f3a….xaml</c> is "Chapter 12 — The Interrogation (the text)", and
/// <c>items/91c0….json</c> is "Summary of Chapter 12 — The Interrogation".
///
/// The book being open is the first source of names. A file a version removed is no longer in
/// the book, so its own record is read at the version before it went (<c>textAt</c>) and its
/// title taken from there; only when that fails too does a line fall back to "a chapter that is
/// no longer in the book".
/// </summary>
public static class ChangeNames
{
    /// <summary>Where a chapter's prose lives, relative to the book folder — the path git works in.</summary>
    public static string ChapterTextPath(Chapter chapter) => $"chapters/{chapter.Id:N}.xaml";

    /// <summary>Where a chapter's own record lives, relative to the book folder.</summary>
    public static string ChapterRecordPath(Chapter chapter) => $"chapters/{chapter.Id:N}.json";

    /// <summary>One changed file, in the book's words.</summary>
    public static string Describe(string path, Book? book, Func<string, string?>? textAt = null)
    {
        var relative = path.Replace('\\', '/').TrimStart('/');
        var file = relative[(relative.LastIndexOf('/') + 1)..];
        var folder = relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : string.Empty;
        var extension = Path.GetExtension(file);
        var stem = Path.GetFileNameWithoutExtension(file);

        switch (relative)
        {
            case "book.json":       return "The book's details and the order of everything in it";
            case "progress.json":   return "Writing progress on this computer";
            case ".gitignore":
            case ".gitattributes":  return "Version-tracking settings";
        }

        if (!Guid.TryParseExact(stem, "N", out var id))
            return relative;                                     // something a person put in the folder by hand

        return folder switch
        {
            "chapters"   => Chapter(id, extension, book, textAt),
            "sections"   => Named(id, book?.Sections.FirstOrDefault(s => s.Id == id)?.Title, "Part", "a part that is no longer in the book", relative, textAt),
            "characters" => Named(id, book?.Characters.FirstOrDefault(c => c.Id == id)?.Name, "Character", "a character who is no longer in the book", relative, textAt),
            "timeline"   => Named(id, book?.Timeline.FirstOrDefault(t => t.Id == id)?.Title, "Timeline event", "an event that is no longer in the book", relative, textAt),
            "plotlines"  => Named(id, book?.Plotlines.FirstOrDefault(p => p.Id == id)?.Name, "Plotline", "a plotline that is no longer in the book", relative, textAt),
            "items"      => ItemLine(id, book, relative, textAt),
            _            => relative
        };
    }

    /// <summary>"Chapter 12 — The Interrogation", the way the tree says it.</summary>
    public static string ChapterLabel(Book book, Chapter chapter)
    {
        var number = book.ChapterNumber(chapter);
        var part = book.SectionOf(chapter);
        var prefix = number > 0 ? $"Chapter {number}" : "Chapter";
        var title = string.IsNullOrWhiteSpace(chapter.Title) ? "(untitled)" : chapter.Title.Trim();
        return part is null ? $"{prefix} — {title}" : $"{part.Title}, {prefix} — {title}";
    }

    /// <summary>
    /// The version message offered when the author has not written one: what changed, counted.
    /// "Chapter 12 — The Interrogation changed", "4 chapters changed, 1 character added".
    /// </summary>
    public static string Suggest(IReadOnlyList<BookChange> changes)
    {
        // book.json changes with almost everything else (it holds the order), so it never names a
        // version on its own account; nor do the files git keeps for itself.
        var real = changes.Where(c => !c.Path.StartsWith('.') && c.Path is not ("progress.json" or "book.json")).ToList();
        if (real.Count == 0) return "Saved a version";

        var inChapters = real.Where(c => c.Path.StartsWith("chapters/", StringComparison.Ordinal)).ToList();
        var chapters = inChapters.Select(c => c.Description.Replace(" (the text)", "", StringComparison.Ordinal))
                                 .Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
        var elsewhere = real.Except(inChapters).ToList();

        // One chapter, or one other thing: say which, and what happened to it.
        if (chapters.Count == 1 && elsewhere.Count == 0) return $"{chapters[0]} {Verb(inChapters[0].Kind)}";
        if (chapters.Count == 0 && elsewhere.Count == 1) return $"{elsewhere[0].Description} {Verb(elsewhere[0].Kind)}";

        var parts = new List<string>();
        Count(parts, chapters.Count, "chapter", "chapters");
        Count(parts, real.Count(c => c.Path.StartsWith("characters/", StringComparison.Ordinal)), "character", "characters");
        Count(parts, real.Count(c => c.Path.StartsWith("plotlines/", StringComparison.Ordinal)), "plotline", "plotlines");
        Count(parts, real.Count(c => c.Path.StartsWith("timeline/", StringComparison.Ordinal)), "timeline event", "timeline events");
        Count(parts, real.Count(c => c.Path.StartsWith("sections/", StringComparison.Ordinal)), "part", "parts");
        Count(parts, real.Count(c => c.Path.StartsWith("items/", StringComparison.Ordinal)), "note or analysis", "notes and analyses");
        if (parts.Count == 0) return "Saved a version";
        return Join(parts) + " changed";
    }

    // ── One line per kind of file ─────────────────────────────────────────────

    private static string Chapter(Guid id, string extension, Book? book, Func<string, string?>? textAt)
    {
        var chapter = book?.Chapters.FirstOrDefault(c => c.Id == id);
        var label = chapter is not null && book is not null
            ? ChapterLabel(book, chapter)
            : TitleAt(textAt, $"chapters/{id:N}.json") is { } title
                ? $"Chapter “{title}”"
                : "A chapter that is no longer in the book";
        return extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase) ? label + " (the text)" : label;
    }

    private static string Named(Guid id, string? name, string kind, string gone, string relative, Func<string, string?>? textAt)
    {
        if (!string.IsNullOrWhiteSpace(name)) return $"{kind}: {name}";
        return TitleAt(textAt, relative) is { } title ? $"{kind}: {title}" : Capitalise(gone);
    }

    private static string ItemLine(Guid id, Book? book, string relative, Func<string, string?>? textAt)
    {
        var item = book?.Items.FirstOrDefault(i => i.Id == id);
        if (item is null)
            return TitleAt(textAt, relative) is { } title ? $"Note: {title}" : "A note or analysis that is no longer in the book";

        var owner = book is null ? null : OwnerLabel(book, item.OwnerId);
        var kind = KindWord(item.Kind);
        return owner is null ? kind : $"{kind} of {owner}";
    }

    private static string? OwnerLabel(Book book, Guid ownerId)
    {
        if (ownerId == book.Id) return "the book";
        if (book.Chapters.FirstOrDefault(c => c.Id == ownerId) is { } chapter) return ChapterLabel(book, chapter);
        if (book.Sections.FirstOrDefault(s => s.Id == ownerId) is { } section) return section.Title;
        if (book.Characters.FirstOrDefault(c => c.Id == ownerId) is { } character) return character.Name;
        if (book.Plotlines.FirstOrDefault(p => p.Id == ownerId) is { } plotline) return plotline.Name;
        if (book.Items.FirstOrDefault(i => i.Id == ownerId) is { } parent)
            return KindWord(parent.Kind) + (OwnerLabel(book, parent.OwnerId) is { } grand ? " of " + grand : "");
        return null;
    }

    private static string KindWord(ItemKind kind) => kind switch
    {
        ItemKind.Summary             => "Summary",
        ItemKind.Analysis            => "Analysis",
        ItemKind.CharactersInChapter => "Chapter characters",
        ItemKind.Outline             => "Outline",
        ItemKind.ChapterPlotlines    => "Chapter plotlines",
        ItemKind.Suggestions         => "Rewrite suggestions",
        _                            => "Notes"
    };

    /// <summary>The Title or Name in one of our own json files, as it was at some version.</summary>
    private static string? TitleAt(Func<string, string?>? textAt, string relative)
    {
        var json = textAt?.Invoke(relative);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var property in new[] { "Title", "Name" })
                if (document.RootElement.TryGetProperty(property, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    value.GetString() is { Length: > 0 } text)
                    return text;
        }
        catch (JsonException) { /* an unreadable file still gets a line, just a vaguer one */ }
        return null;
    }

    private static string Verb(BookChangeKind kind) => kind switch
    {
        BookChangeKind.Added   => "added",
        BookChangeKind.Removed => "removed",
        _                      => "revised"
    };

    private static void Count(List<string> parts, int n, string one, string many)
    {
        if (n > 0) parts.Add(n == 1 ? $"1 {one}" : $"{n} {many}");
    }

    private static string Join(List<string> parts) =>
        parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];

    private static string Capitalise(string text) => char.ToUpperInvariant(text[0]) + text[1..];
}
