using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services.Import;

/// <summary>One chapter file the importer found, with what it learned before importing.</summary>
public sealed class ImportEntry
{
    public required int    Number      { get; init; }
    public required string Title       { get; init; }
    public required string Path        { get; init; }
    public int     WordCount   { get; set; }
    /// <summary>The document's own first heading, when it has one ("Chapter One - The Message").</summary>
    public string? HeadingInDocument { get; set; }
    public string? Error       { get; set; }
    /// <summary>The title as the heading inside the document states it ("Chapter Eight: The Interrogation" → "The Interrogation"), when there is one.</summary>
    public string? DocumentTitle =>
        HeadingInDocument is { } h ? (ChapterFileName.TryParse(h)?.Title ?? (h.Trim().Length > 0 ? h.Trim() : null)) : null;
    /// <summary>True when the heading inside the file names a different title than the file name does.</summary>
    public bool TitleMismatch =>
        DocumentTitle is { } d && !string.Equals(d, Title, StringComparison.OrdinalIgnoreCase);
    public bool Include { get; set; } = true;
    /// <summary>The title the import will use, honouring <see cref="ImportPlan.UseDocumentTitles"/>.</summary>
    public string EffectiveTitle(ImportPlan plan) => plan.UseDocumentTitles && DocumentTitle is { } d ? d : Title;
}

/// <summary>What <see cref="ManuscriptImporter.Scan"/> found in a folder: the plan the user reviews before importing.</summary>
public sealed class ImportPlan
{
    public required string Folder { get; init; }
    public List<ImportEntry> Chapters { get; } = new();
    /// <summary>Files in the folder that are not chapter files (compiled drafts, evaluations, images…).</summary>
    public List<string> Skipped { get; } = new();
    /// <summary>Chapter numbers absent between the first and last found (e.g. 23 when 22 and 24 exist).</summary>
    public List<int> MissingNumbers { get; } = new();
    /// <summary>Numbers that two or more files claim.</summary>
    public List<int> DuplicateNumbers { get; } = new();
    /// <summary>Take chapter titles from the heading inside each document instead of the file name.</summary>
    public bool UseDocumentTitles { get; set; }
    public int TotalWords => Chapters.Where(c => c.Include && c.Error is null).Sum(c => c.WordCount);
    public int MismatchCount => Chapters.Count(c => c.TitleMismatch);
}

/// <summary>
/// Turns a folder of per-chapter files into chapters of a book (task 1.13), and one file into
/// one chapter at a chosen position (task 1.19, "a chapter written on another device").
/// Files are read, never modified; the book folder is the new source of truth.
/// </summary>
public sealed class ManuscriptImporter
{
    private static readonly string[] ChapterExtensions = { ".docx", ".txt", ".md", ".markdown" };

    private readonly BookStore _store;

    public ManuscriptImporter(BookStore store) => _store = store;

    public static bool IsSupported(string path) =>
        ChapterExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads any supported manuscript file.</summary>
    public static ImportedDocument ReadFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".docx" ? DocxReader.Read(path) : TextReader_.ReadFile(path);
    }

    /// <summary>Looks at the files directly in <paramref name="folder"/> (subfolders ignored) and reads each chapter file.</summary>
    public ImportPlan Scan(string folder)
    {
        var plan = new ImportPlan { Folder = folder };
        foreach (var file in Directory.GetFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("~$")) continue;                       // Word lock files
            var parsed = IsSupported(file) ? ChapterFileName.TryParse(name) : null;
            if (parsed is null) { plan.Skipped.Add(name); continue; }

            var entry = new ImportEntry { Number = parsed.Number, Title = parsed.Title, Path = file };
            try
            {
                var doc = ReadFile(file);
                entry.HeadingInDocument = doc.FirstHeading;
                entry.WordCount = doc.WithoutLeadingHeading().WordCount;
            }
            catch (Exception ex)
            {
                entry.Error = ex.Message;
                entry.Include = false;
            }
            plan.Chapters.Add(entry);
        }

        plan.Chapters.Sort((a, b) => a.Number != b.Number ? a.Number.CompareTo(b.Number) : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        var numbers = plan.Chapters.Select(c => c.Number).ToList();
        plan.DuplicateNumbers.AddRange(numbers.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key));
        if (numbers.Count > 0)
            for (int n = numbers.Min(); n <= numbers.Max(); n++)
                if (!numbers.Contains(n)) plan.MissingNumbers.Add(n);
        return plan;
    }

    /// <summary>
    /// Imports the included entries as chapters appended to <paramref name="section"/> (or to the
    /// book level when null), in number order. Chapter titles come from the file names; the
    /// leading heading inside each file is dropped so the title is not repeated in the prose.
    /// Saves the book. Returns the chapters created.
    /// </summary>
    public IReadOnlyList<Chapter> Import(Book book, ImportPlan plan, Section? section)
    {
        var created = new List<Chapter>();
        foreach (var entry in plan.Chapters.Where(e => e.Include && e.Error is null))
        {
            var chapter = ImportOne(book, entry.Path, entry.EffectiveTitle(plan), section, insertAt: null);
            created.Add(chapter);
        }
        _store.Save(book);
        return created;
    }

    /// <summary>
    /// Imports one file as a chapter. <paramref name="insertAt"/> is the index in
    /// <see cref="Book.Chapters"/> to insert at (null = append after the last chapter of the
    /// section, or at the end of the book). Does not save.
    /// </summary>
    public Chapter ImportOne(Book book, string path, string? title, Section? section, int? insertAt)
    {
        var doc  = ReadFile(path);
        var body = doc.WithoutLeadingHeading();
        var finalTitle = title
                         ?? (ChapterFileName.TryParse(Path.GetFileName(path))?.Title)
                         ?? (doc.FirstHeading is { } h ? (ChapterFileName.TryParse(h)?.Title ?? h) : null)
                         ?? Path.GetFileNameWithoutExtension(path);

        var chapter = new Chapter
        {
            Title     = finalTitle,
            SectionId = section?.Id,
            Status    = ChapterStatus.Draft,
            WordCount = body.WordCount
        };

        int index = insertAt ?? DefaultInsertIndex(book, section);
        index = Math.Clamp(index, 0, book.Chapters.Count);
        book.Chapters.Insert(index, chapter);

        Directory.CreateDirectory(book.FolderPath);
        _store.SaveChapterBody(book, chapter, body.ToFlowDocumentXaml(), body.WordCount);
        return chapter;
    }

    /// <summary>After the last chapter of the section; for book-level chapters, the end.</summary>
    private static int DefaultInsertIndex(Book book, Section? section)
    {
        if (section is null) return book.Chapters.Count;
        int last = -1;
        for (int i = 0; i < book.Chapters.Count; i++)
            if (book.Chapters[i].SectionId == section.Id) last = i;
        return last >= 0 ? last + 1 : book.Chapters.Count;
    }
}
