using System.Text.Json;
using System.Text.Json.Serialization;
using AuthorPlus.Core.Models;

namespace AuthorPlus.Core.Services;

/// <summary>
/// Folder-per-book persistence.
///
/// <code>
/// {Root}/{Book folder}/
///   book.json                 metadata (Book minus the collections) + the id order of each collection
///   sections/{id}.json        Section
///   chapters/{id}.json        Chapter metadata (SectionId says which part it is in)
///   chapters/{id}.xaml        the prose — a WPF FlowDocument, saved by the editor
///   items/{id}.json           Item (OwnerId = a chapter, a section or the book)
///   characters/{id}.json
///   timeline/{id}.json
///   plotlines/{id}.json
/// </code>
///
/// One file per item so that a crash can damage at most one item, so that the folder is
/// git- and backup-friendly, and so that the author can always read their work with a
/// text editor. Every write goes to a temp file and is then moved into place (atomic on
/// NTFS), and items removed from the book are deleted from disk on save.
///
/// Order is the order of the lists in <see cref="Book"/>; it is persisted as id lists in
/// <c>book.json</c> so file-system enumeration order never matters.
///
/// Format 1 (2026-09-04) had no sections or items and kept a chapter's summary on the chapter.
/// Loading a format-1 book moves each summary into a Summary item; the next save writes format 2.
/// </summary>
public sealed class BookStore
{
    public const string BookFileName = "book.json";
    public const string ChapterBodyExtension = ".xaml";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Default library location: Documents\AuthorPlus\Books.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AuthorPlus", "Books");

    public string Root { get; }

    public BookStore(string? root = null)
    {
        Root = root ?? DefaultRoot;
    }

    // ── Library ───────────────────────────────────────────────────────────────

    /// <summary>Folders under <see cref="Root"/> that contain a <c>book.json</c>, with their titles.</summary>
    public IReadOnlyList<(string Folder, string Title, DateTime ModifiedUtc)> ListBooks()
    {
        if (!Directory.Exists(Root)) return Array.Empty<(string, string, DateTime)>();

        var result = new List<(string, string, DateTime)>();
        foreach (var dir in Directory.GetDirectories(Root))
        {
            var meta = Path.Combine(dir, BookFileName);
            if (!File.Exists(meta)) continue;
            try
            {
                var m = JsonSerializer.Deserialize<BookMeta>(File.ReadAllText(meta), Json);
                result.Add((dir, m?.Title ?? Path.GetFileName(dir), m?.ModifiedUtc ?? File.GetLastWriteTimeUtc(meta)));
            }
            catch
            {
                result.Add((dir, Path.GetFileName(dir) + "  (unreadable book.json)", File.GetLastWriteTimeUtc(meta)));
            }
        }
        return result.OrderByDescending(r => r.Item3).ToList();
    }

    /// <summary>Creates a new book folder (unique, filesystem-safe name from the title) and saves it.</summary>
    public Book Create(string title, string author = "")
    {
        Directory.CreateDirectory(Root);
        var folder = UniqueFolder(Root, SafeFolderName(title));
        var book = new Book { Title = title, Author = author, FolderPath = folder };
        Save(book);
        return book;
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    public Book Load(string folder)
    {
        var metaPath = Path.Combine(folder, BookFileName);
        if (!File.Exists(metaPath))
            throw new FileNotFoundException("Not a book folder (no book.json).", metaPath);

        var meta = JsonSerializer.Deserialize<BookMeta>(File.ReadAllText(metaPath), Json)
                   ?? throw new InvalidDataException("book.json is empty.");

        var book = new Book
        {
            Id = meta.Id, Title = meta.Title, Author = meta.Author, Synopsis = meta.Synopsis,
            Genre = meta.Genre, TargetWords = meta.TargetWords, CreatedUtc = meta.CreatedUtc,
            ModifiedUtc = meta.ModifiedUtc, FormatVersion = meta.FormatVersion, FolderPath = folder
        };

        book.Sections   = LoadItems<Section>      (folder, "sections",   meta.SectionOrder,   s => s.Id);
        book.Chapters   = LoadItems<Chapter>      (folder, "chapters",   meta.ChapterOrder,   c => c.Id);
        book.Items      = LoadItems<Item>         (folder, "items",      meta.ItemOrder,      i => i.Id);
        book.Characters = LoadItems<Character>    (folder, "characters", meta.CharacterOrder, c => c.Id);
        book.Timeline   = LoadItems<TimelineEvent>(folder, "timeline",   meta.TimelineOrder,  t => t.Id);
        book.Plotlines  = LoadItems<Plotline>     (folder, "plotlines",  meta.PlotlineOrder,  p => p.Id);

        Migrate(book);
        return book;
    }

    /// <summary>Format 1 → 2: chapter summaries become Summary items. Idempotent; in memory only until the next save.</summary>
    private static void Migrate(Book book)
    {
        foreach (var ch in book.Chapters)
        {
            if (!string.IsNullOrWhiteSpace(ch.LegacySummary) && book.SummaryOf(ch) is null)
                book.SetSummary(ch, ch.LegacySummary.Trim());
            ch.LegacySummary = null;
        }
        if (book.FormatVersion < Book.CurrentFormatVersion) book.FormatVersion = Book.CurrentFormatVersion;
    }

    private static List<T> LoadItems<T>(string folder, string sub, List<Guid> order, Func<T, Guid> id)
    {
        var dir = Path.Combine(folder, sub);
        if (!Directory.Exists(dir)) return new List<T>();

        var byId = new Dictionary<Guid, T>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var item = JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json);
                if (item != null) byId[id(item)] = item;
            }
            catch
            {
                // A corrupt item file loses that item only, never the book.
            }
        }

        // Persisted order first, then anything on disk the order list does not know about
        // (e.g. a file dropped in by hand) appended by file name.
        var result = new List<T>();
        foreach (var g in order)
            if (byId.Remove(g, out var item)) result.Add(item);
        result.AddRange(byId.OrderBy(kv => kv.Key).Select(kv => kv.Value));
        return result;
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes book.json and every item file, and deletes item files for ids no longer in the
    /// book. Items whose owner no longer exists are dropped. Chapter bodies are untouched here —
    /// see <see cref="SaveChapterBody"/>.
    /// </summary>
    public void Save(Book book)
    {
        if (string.IsNullOrEmpty(book.FolderPath))
            throw new InvalidOperationException("Book has no FolderPath; use Create() first.");

        Directory.CreateDirectory(book.FolderPath);
        book.ModifiedUtc = DateTime.UtcNow;
        book.FormatVersion = Book.CurrentFormatVersion;

        var owners = new HashSet<Guid> { book.Id };
        owners.UnionWith(book.Sections.Select(s => s.Id));
        owners.UnionWith(book.Chapters.Select(c => c.Id));
        book.Items.RemoveAll(i => !owners.Contains(i.OwnerId));

        SaveItems(book.FolderPath, "sections",   book.Sections,   s => s.Id, keepBodies: false);
        SaveItems(book.FolderPath, "chapters",   book.Chapters,   c => c.Id, keepBodies: true);
        SaveItems(book.FolderPath, "items",      book.Items,      i => i.Id, keepBodies: false);
        SaveItems(book.FolderPath, "characters", book.Characters, c => c.Id, keepBodies: false);
        SaveItems(book.FolderPath, "timeline",   book.Timeline,   t => t.Id, keepBodies: false);
        SaveItems(book.FolderPath, "plotlines",  book.Plotlines,  p => p.Id, keepBodies: false);

        var meta = new BookMeta
        {
            Id = book.Id, Title = book.Title, Author = book.Author, Synopsis = book.Synopsis,
            Genre = book.Genre, TargetWords = book.TargetWords, CreatedUtc = book.CreatedUtc,
            ModifiedUtc = book.ModifiedUtc, FormatVersion = book.FormatVersion,
            SectionOrder   = book.Sections.Select(s => s.Id).ToList(),
            ChapterOrder   = book.Chapters.Select(c => c.Id).ToList(),
            ItemOrder      = book.Items.Select(i => i.Id).ToList(),
            CharacterOrder = book.Characters.Select(c => c.Id).ToList(),
            TimelineOrder  = book.Timeline.Select(t => t.Id).ToList(),
            PlotlineOrder  = book.Plotlines.Select(p => p.Id).ToList()
        };
        WriteAtomic(Path.Combine(book.FolderPath, BookFileName), JsonSerializer.Serialize(meta, Json));
    }

    private static void SaveItems<T>(string folder, string sub, List<T> items, Func<T, Guid> id, bool keepBodies)
    {
        var dir = Path.Combine(folder, sub);
        Directory.CreateDirectory(dir);

        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var name = id(item).ToString("N");
            live.Add(name);
            WriteAtomic(Path.Combine(dir, name + ".json"), JsonSerializer.Serialize(item, Json));
        }

        // Remove files for deleted items (json, and the chapter body when its chapter is gone).
        foreach (var file in Directory.GetFiles(dir))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var ext  = Path.GetExtension(file);
            bool isItemFile = ext.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                              (keepBodies && ext.Equals(ChapterBodyExtension, StringComparison.OrdinalIgnoreCase));
            if (isItemFile && !live.Contains(stem))
            {
                try { File.Delete(file); } catch { /* best effort */ }
            }
        }
    }

    // ── Chapter bodies ────────────────────────────────────────────────────────

    public string ChapterBodyPath(Book book, Chapter chapter) =>
        Path.Combine(book.FolderPath, "chapters", chapter.Id.ToString("N") + ChapterBodyExtension);

    /// <summary>The chapter's FlowDocument XAML, or null when it has never been written.</summary>
    public string? LoadChapterBody(Book book, Chapter chapter)
    {
        var path = ChapterBodyPath(book, chapter);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>Writes the chapter's FlowDocument XAML and updates its word count and timestamp.</summary>
    public void SaveChapterBody(Book book, Chapter chapter, string xaml, int wordCount)
    {
        Directory.CreateDirectory(Path.Combine(book.FolderPath, "chapters"));
        WriteAtomic(ChapterBodyPath(book, chapter), xaml);
        chapter.WordCount   = wordCount;
        chapter.ModifiedUtc = DateTime.UtcNow;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Temp-file-then-move so a crash never leaves a half-written file behind.</summary>
    internal static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>"The Long Road: Part 1" → "The Long Road - Part 1"; never empty.</summary>
    public static string SafeFolderName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim().TrimEnd('.');
        while (cleaned.Contains("  ")) cleaned = cleaned.Replace("  ", " ");
        if (cleaned.Length > 80) cleaned = cleaned[..80].TrimEnd();
        return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
    }

    private static string UniqueFolder(string root, string name)
    {
        var candidate = Path.Combine(root, name);
        for (int n = 2; Directory.Exists(candidate); n++)
            candidate = Path.Combine(root, $"{name} ({n})");
        return candidate;
    }

    /// <summary>The on-disk shape of book.json: metadata plus the id order of each collection.</summary>
    private sealed class BookMeta
    {
        public Guid       Id             { get; set; }
        public string     Title          { get; set; } = string.Empty;
        public string     Author         { get; set; } = string.Empty;
        public string     Synopsis       { get; set; } = string.Empty;
        public string     Genre          { get; set; } = string.Empty;
        public int        TargetWords    { get; set; }
        public DateTime   CreatedUtc     { get; set; }
        public DateTime   ModifiedUtc    { get; set; }
        public int        FormatVersion  { get; set; } = 1;
        public List<Guid> SectionOrder   { get; set; } = new();
        public List<Guid> ChapterOrder   { get; set; } = new();
        public List<Guid> ItemOrder      { get; set; } = new();
        public List<Guid> CharacterOrder { get; set; } = new();
        public List<Guid> TimelineOrder  { get; set; } = new();
        public List<Guid> PlotlineOrder  { get; set; } = new();
    }
}
