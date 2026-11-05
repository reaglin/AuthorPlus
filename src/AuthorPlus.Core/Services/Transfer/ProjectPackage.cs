using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthorPlus.Core.Services.Transfer;

/// <summary>What a project file says about the book inside it (<c>authorplus-project.json</c>).</summary>
public sealed class ProjectManifest
{
    public int      FormatVersion   { get; set; } = ProjectPackage.FormatVersion;
    public Guid     BookId          { get; set; }
    public string   Title           { get; set; } = string.Empty;
    public string   Author          { get; set; } = string.Empty;
    /// <summary>When the book itself was last changed (book.json's ModifiedUtc).</summary>
    public DateTime BookModifiedUtc { get; set; }
    public DateTime ExportedUtc     { get; set; }
    public string   ExportedFrom    { get; set; } = string.Empty;   // computer name, so the author knows which copy this is
    public string   AppVersion      { get; set; } = string.Empty;
    public bool     IncludesHistory { get; set; }
    public int      FileCount       { get; set; }
    /// <summary>The book's content when it was exported (<see cref="ProjectPackage.Fingerprint"/>): tells real changes from a re-save.</summary>
    public string   ContentHash     { get; set; } = string.Empty;
}

/// <summary>A book in this computer's library that has the same id as the one being imported.</summary>
public sealed record ExistingBook(string Folder, string Title, DateTime ModifiedUtc, string ContentHash);

/// <summary>How the file and the copy on this computer relate — what the import window says and recommends.</summary>
public enum ImportSituation
{
    /// <summary>The book is not on this computer.</summary>
    NewHere,
    /// <summary>The file has new writing and the copy here has none since it last went out or came in: replace.</summary>
    FileIsNewer,
    /// <summary>The file holds exactly what is here.</summary>
    Same,
    /// <summary>The copy here has new writing and the file has none: the file is an old one.</summary>
    FileIsOlder,
    /// <summary>Both have new writing since they parted: keep both.</summary>
    BothChanged,
    /// <summary>No record of an export or import here; judged by the dates alone, the file is newer.</summary>
    ProbablyNewer,
    /// <summary>No record of an export or import here; judged by the dates alone, the copy here is newer.</summary>
    ProbablyOlder
}

public sealed record ProjectExportResult(string File, int FileCount, long Bytes);

public sealed record ProjectImportResult(string Folder, string? PreviousCopyFolder);

public enum ProjectImportMode
{
    /// <summary>A new book folder beside whatever is there (a number is added to the name if needed).</summary>
    KeepBoth,
    /// <summary>The book with the same id is moved aside to "Replaced books" and this one takes its place.</summary>
    Replace
}

/// <summary>
/// Export Project / Import Project: the whole book folder — chapters, items, characters, timeline,
/// plotlines and, when the book keeps versions, its whole history (<c>.git</c>) — as one file, so the
/// author can carry a book to another computer and back. It is a zip with a manifest at the top and the
/// book folder under <c>book/</c>; the extension is <see cref="Extension"/>.
///
/// What is left out is what <see cref="Versioning.BookRepository"/> also leaves out: half-written
/// <c>*.tmp</c> files, <c>progress.json</c> (today's word count on <i>this</i> computer) and exports
/// (<c>.docx</c>, <c>.pdf</c>, <c>.epub</c>), which are made from the book and are not the book.
///
/// Import never overwrites in place. It unpacks into a new folder, checks that the book loads, and only
/// then moves the old copy aside (to <c>Documents\AuthorPlus\Replaced books</c>) and the new one in.
/// A bad file therefore costs nothing.
/// </summary>
public static class ProjectPackage
{
    public const string Extension    = ".authorplus";
    public const string ManifestName = "authorplus-project.json";
    public const string BookPrefix   = "book/";
    public const int    FormatVersion = 1;

    /// <summary>The folder (beside the library) that keeps the copy an import replaced.</summary>
    public const string ReplacedFolderName = "Replaced books";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private static readonly string[] SkippedExtensions = { ".tmp", ".docx", ".pdf", ".epub" };
    private const string ProgressFile = "progress.json";

    // ── Export ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the book folder to <paramref name="destinationFile"/>. The file is written beside the
    /// destination first and moved into place, so a failed export never leaves half a file behind.
    /// </summary>
    public static ProjectExportResult Export(string bookFolder, string destinationFile, string appVersion, bool includeHistory = true)
    {
        var bookJson = Path.Combine(bookFolder, BookStore.BookFileName);
        if (!File.Exists(bookJson))
            throw new InvalidOperationException($"\"{bookFolder}\" is not a book folder — it has no {BookStore.BookFileName}.");

        var meta = ReadBookMeta(bookJson);
        var files = BookFiles(bookFolder, includeHistory).ToList();
        var manifest = new ProjectManifest
        {
            BookId = meta.Id, Title = meta.Title, Author = meta.Author, BookModifiedUtc = meta.ModifiedUtc,
            ExportedUtc = DateTime.UtcNow, ExportedFrom = Environment.MachineName, AppVersion = appVersion,
            IncludesHistory = includeHistory && Directory.Exists(Path.Combine(bookFolder, ".git")),
            FileCount = files.Count, ContentHash = Fingerprint(bookFolder)
        };

        var dir = Path.GetDirectoryName(Path.GetFullPath(destinationFile))!;
        Directory.CreateDirectory(dir);
        var tmp = destinationFile + ".writing";
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                var m = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
                using (var s = m.Open()) JsonSerializer.Serialize(s, manifest, Json);

                foreach (var rel in files)
                {
                    var entry = zip.CreateEntry(BookPrefix + rel.Replace('\\', '/'), CompressionLevel.Optimal);
                    entry.LastWriteTime = File.GetLastWriteTime(Path.Combine(bookFolder, rel));
                    // Share everything: the open book's repository may hold its pack files open.
                    using var src = new FileStream(Path.Combine(bookFolder, rel), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var dst = entry.Open();
                    src.CopyTo(dst);
                }
            }
            File.Move(tmp, destinationFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        return new ProjectExportResult(destinationFile, files.Count, new FileInfo(destinationFile).Length);
    }

    /// <summary>
    /// "The Book of One 2026-11-05 1432 (DESKTOP).authorplus" — the name offered in the save dialog. The time and the
    /// computer keep two computers' exports apart in a shared folder such as OneDrive.
    /// </summary>
    public static string SuggestedFileName(string title, DateTime when, string computer) =>
        $"{BookStore.SafeFolderName(title)} {when:yyyy-MM-dd HHmm} ({BookStore.SafeFolderName(computer)}){Extension}";

    /// <summary>
    /// A hash of the book's own files (not its history, not this computer's bookkeeping). Saving with no change
    /// leaves it as it was: the save time in <c>book.json</c> is left out of it.
    /// </summary>
    public static string Fingerprint(string bookFolder)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var rel in BookFiles(bookFolder, includeHistory: false)
                     .Select(r => r.Replace('\\', '/')).OrderBy(r => r, StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(rel + "\n"));
            var bytes = File.ReadAllBytes(Path.Combine(bookFolder, rel));
            if (rel.Equals(BookStore.BookFileName, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (JsonNode.Parse(bytes) is JsonObject o) { o.Remove("ModifiedUtc"); bytes = Encoding.UTF8.GetBytes(o.ToJsonString()); }
                }
                catch (JsonException) { /* hash it as it is */ }
            }
            sha.AppendData(bytes);
            sha.AppendData(new byte[] { 0 });
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    /// <summary>
    /// What the file means for the copy here. <paramref name="lastHashHere"/> is the fingerprint the copy here had
    /// when it was last exported or imported on this computer (<see cref="TransferLog"/>) — the point where the two
    /// copies parted. Without it, only the dates are left to go on.
    /// </summary>
    public static ImportSituation Compare(ProjectManifest file, ExistingBook? here, string? lastHashHere)
    {
        if (here is null) return ImportSituation.NewHere;
        if (file.ContentHash.Length > 0 && file.ContentHash == here.ContentHash) return ImportSituation.Same;
        if (string.IsNullOrEmpty(lastHashHere) || file.ContentHash.Length == 0)
            return here.ModifiedUtc > file.BookModifiedUtc ? ImportSituation.ProbablyOlder : ImportSituation.ProbablyNewer;

        bool changedHere = here.ContentHash != lastHashHere;
        bool fileHasNew  = file.ContentHash != lastHashHere;
        return (changedHere, fileHasNew) switch
        {
            (false, _)    => ImportSituation.FileIsNewer,
            (true, false) => ImportSituation.FileIsOlder,
            (true, true)  => ImportSituation.BothChanged
        };
    }

    /// <summary>Paths relative to the book folder of everything that goes into the project file.</summary>
    internal static IEnumerable<string> BookFiles(string bookFolder, bool includeHistory)
    {
        var root = Path.GetFullPath(bookFolder);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, path);
            var top = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            bool inGit = top.Equals(".git", StringComparison.OrdinalIgnoreCase);
            if (inGit && !includeHistory) continue;
            if (!inGit)
            {
                if (SkippedExtensions.Contains(Path.GetExtension(rel), StringComparer.OrdinalIgnoreCase)) continue;
                if (rel.Equals(ProgressFile, StringComparison.OrdinalIgnoreCase)) continue;
            }
            yield return rel;
        }
    }

    // ── Import ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a project file and reads what it holds, without unpacking anything. Throws
    /// <see cref="InvalidDataException"/> with a sentence an author can act on when it is not one.
    /// </summary>
    public static ProjectManifest ReadManifest(string file)
    {
        using var zip = OpenProject(file);
        return ReadManifest(zip);
    }

    /// <summary>Opens the zip, turning "not a zip / cut short / locked" into one sentence the author can act on.</summary>
    private static ZipArchive OpenProject(string file)
    {
        try { return ZipFile.OpenRead(file); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"\"{Path.GetFileName(file)}\" could not be read as an Author+ project file. " +
                "It may not have finished copying or downloading (from OneDrive, say), or another program has it open. " +
                $"Wait a moment, or copy it again from the other computer.\n\nDetails for support: {ex.Message}", ex);
        }
    }

    private static ProjectManifest ReadManifest(ZipArchive zip)
    {
        var m = zip.GetEntry(ManifestName);
        if (m is null || zip.GetEntry(BookPrefix + BookStore.BookFileName) is null)
            throw new InvalidDataException(
                "This is not an Author+ project file. A project file is made on the other computer with File › Export Project. " +
                "(To add one chapter from a Word or text file, use Book › Insert Chapter from File instead.)");
        ProjectManifest? manifest;
        try
        {
            using var s = m.Open();
            manifest = JsonSerializer.Deserialize<ProjectManifest>(s, Json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            throw new InvalidDataException("The project file is damaged: its list of contents cannot be read. Make the file again on the other computer with File › Export Project.", ex);
        }
        if (manifest is null) throw new InvalidDataException("The project file is damaged: its list of contents is empty. Make the file again on the other computer with File › Export Project.");
        if (manifest.FormatVersion > FormatVersion)
            throw new InvalidDataException(
                "This project file was made by a newer version of Author+. Update Author+ (Help › Check for a new version) and try again.");
        return manifest;
    }

    /// <summary>
    /// Every book in <paramref name="libraryRoot"/> with the same id as the project file's, most recently changed first.
    /// There is more than one after an earlier "keep both".
    /// </summary>
    public static IReadOnlyList<ExistingBook> FindAll(ProjectManifest manifest, string libraryRoot)
    {
        var found = new List<ExistingBook>();
        if (!Directory.Exists(libraryRoot)) return found;
        foreach (var dir in Directory.GetDirectories(libraryRoot))
        {
            var json = Path.Combine(dir, BookStore.BookFileName);
            if (!File.Exists(json)) continue;
            try
            {
                var meta = ReadBookMeta(json);
                if (meta.Id == manifest.BookId) found.Add(new ExistingBook(dir, meta.Title, meta.ModifiedUtc, Fingerprint(dir)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* an unreadable book is not this one */ }
        }
        return found.OrderByDescending(x => x.ModifiedUtc).ToList();
    }

    /// <summary>The most recently changed book with the project file's id, if this computer has one.</summary>
    public static ExistingBook? FindExisting(ProjectManifest manifest, string libraryRoot) =>
        FindAll(manifest, libraryRoot).FirstOrDefault();

    /// <summary>
    /// Unpacks the project into the library. With <see cref="ProjectImportMode.Replace"/>,
    /// <paramref name="existing"/> is moved to "Replaced books" (beside the library) once the new copy
    /// has been unpacked and checked, and the new copy takes its folder name.
    /// </summary>
    public static ProjectImportResult Import(string file, string libraryRoot, ProjectImportMode mode, ExistingBook? existing = null)
    {
        if (mode == ProjectImportMode.Replace && existing is null)
            throw new ArgumentException("Replace needs the book to replace.", nameof(existing));

        Directory.CreateDirectory(libraryRoot);
        using var zip = OpenProject(file);
        var manifest = ReadManifest(zip);

        // Unpack beside the library, not in it, so a half-finished import never shows up as a book.
        var staging = Path.Combine(LibraryParent(libraryRoot), ".importing", Guid.NewGuid().ToString("N"));
        try
        {
            Extract(zip, staging);
            new BookStore(libraryRoot).Load(staging);   // throws if the book will not open — nothing has been touched yet

            string target;
            string? previous = null;
            if (mode == ProjectImportMode.Replace)
            {
                target = existing!.Folder;
                previous = MoveAside(existing.Folder, libraryRoot);
            }
            else
            {
                target = UniqueFolder(libraryRoot, BookStore.SafeFolderName(manifest.Title));
            }
            try { Directory.Move(staging, target); }
            catch when (previous is not null)
            {
                Directory.Move(previous, target);        // put the old copy back where it was
                throw;
            }
            return new ProjectImportResult(target, previous);
        }
        finally
        {
            if (Directory.Exists(staging)) DeleteTree(staging);
        }
    }

    private static void Extract(ZipArchive zip, string staging)
    {
        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(BookPrefix, StringComparison.Ordinal)) continue;
            var rel = entry.FullName[BookPrefix.Length..];
            if (rel.Length == 0 || rel.EndsWith('/')) continue;           // a folder entry
            var dest = Path.GetFullPath(Path.Combine(root, rel));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The project file names a file outside the book folder; it was not imported.");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    /// <summary>Moves a book folder to "Replaced books\{name} (replaced 2026-11-05 1432)" and returns where it went.</summary>
    private static string MoveAside(string folder, string libraryRoot)
    {
        var aside = Path.Combine(LibraryParent(libraryRoot), ReplacedFolderName);
        Directory.CreateDirectory(aside);
        var dest = UniqueFolder(aside, $"{Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar))} (replaced {DateTime.Now:yyyy-MM-dd HHmm})");
        Directory.Move(folder, dest);
        return dest;
    }

    /// <summary>Documents\AuthorPlus for the default library (Documents\AuthorPlus\Books) — the same volume, so moves are renames.</summary>
    private static string LibraryParent(string libraryRoot) =>
        Path.GetDirectoryName(Path.GetFullPath(libraryRoot).TrimEnd(Path.DirectorySeparatorChar)) ?? libraryRoot;

    private static string UniqueFolder(string root, string name)
    {
        var candidate = Path.Combine(root, name);
        for (int n = 2; Directory.Exists(candidate); n++)
            candidate = Path.Combine(root, $"{name} ({n})");
        return candidate;
    }

    /// <summary>Deletes a folder that may hold git's read-only object files.</summary>
    private static void DeleteTree(string folder)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(folder, recursive: true);
        }
        catch { /* a leftover staging folder is harmless: it is outside the library */ }
    }

    private sealed class BookMetaLite
    {
        public Guid     Id          { get; set; }
        public string   Title       { get; set; } = string.Empty;
        public string   Author      { get; set; } = string.Empty;
        public DateTime ModifiedUtc { get; set; }
    }

    private static BookMetaLite ReadBookMeta(string bookJson) =>
        JsonSerializer.Deserialize<BookMetaLite>(File.ReadAllText(bookJson)) ?? new BookMetaLite();
}

/// <summary>
/// This computer's record of where each book stood when it last went out (export) or came in (import): book folder →
/// fingerprint. Kept beside the recent-books list, never in the book, so it is never carried to another computer.
/// </summary>
public sealed class TransferLog
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuthorPlus", "transfers.json");

    public TransferLog(string? path = null) => _path = path ?? DefaultPath;

    public string? LastHash(string bookFolder) =>
        Load().TryGetValue(Key(bookFolder), out var h) ? h : null;

    public void Record(string bookFolder, string hash)
    {
        try
        {
            var all = Load();
            all[Key(bookFolder)] = hash;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(all, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the next import falls back to the dates */ }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    private static string Key(string folder) => Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
}
