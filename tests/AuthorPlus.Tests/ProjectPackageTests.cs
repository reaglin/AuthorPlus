using System.IO.Compression;
using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Transfer;
using AuthorPlus.Core.Services.Versioning;

namespace AuthorPlus.Tests;

public class ProjectPackageTests
{
    private static readonly VersionAuthor Ron = new("Ron Eaglin", "ron@example.com");

    /// <summary>Two computers: each has its own Documents\AuthorPlus with a Books library.</summary>
    private static (string ComputerA, string ComputerB, string Carry) Computers(TempDir t)
    {
        var a = Path.Combine(t.Path, "A", "AuthorPlus", "Books");
        var b = Path.Combine(t.Path, "B", "AuthorPlus", "Books");
        var carry = Path.Combine(t.Path, "usb");
        Directory.CreateDirectory(a); Directory.CreateDirectory(b); Directory.CreateDirectory(carry);
        return (a, b, carry);
    }

    private static Book SampleBook(BookStore store)
    {
        var book = store.Create("The Book of One", "Ron");
        var part = new Section { Title = "Part One" };
        book.Sections.Add(part);
        var ch = new Chapter { Title = "The Message", SectionId = part.Id };
        book.Chapters.Add(ch);
        book.Characters.Add(new Character { Name = "Roland Ellison" });
        store.Save(book);
        store.SaveChapterBody(book, ch, "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph>It began with a message.</Paragraph></FlowDocument>", 5);
        return store.Load(book.FolderPath);
    }

    [Fact]
    public void Export_then_import_on_another_computer_gives_the_same_book_and_its_history()
    {
        using var t = new TempDir();
        var (a, b, carry) = Computers(t);
        var storeA = new BookStore(a);
        var book = SampleBook(storeA);
        using (var repo = BookRepository.Initialize(book.FolderPath, Ron))
        {
            book.Chapters[0].Title = "The Message (revised)";
            storeA.Save(book);
            repo.SaveVersion("Chapter one retitled", Ron);
        }

        var file = Path.Combine(carry, ProjectPackage.SuggestedFileName(book.Title, new DateTime(2026, 11, 5, 14, 32, 0), "DESKTOP"));
        var exported = ProjectPackage.Export(book.FolderPath, file, "1.0.0");
        Assert.EndsWith("The Book of One 2026-11-05 1432 (DESKTOP).authorplus", file);
        Assert.True(exported.FileCount > 5);

        var manifest = ProjectPackage.ReadManifest(file);
        Assert.Equal(book.Id, manifest.BookId);
        Assert.Equal("The Book of One", manifest.Title);
        Assert.True(manifest.IncludesHistory);
        Assert.Null(ProjectPackage.FindExisting(manifest, b));

        var result = ProjectPackage.Import(file, b, ProjectImportMode.KeepBoth);
        Assert.Null(result.PreviousCopyFolder);
        Assert.Equal(Path.Combine(b, "The Book of One"), result.Folder);

        var storeB = new BookStore(b);
        var copy = storeB.Load(result.Folder);
        Assert.Equal(book.Id, copy.Id);
        Assert.Equal("The Message (revised)", copy.Chapters[0].Title);
        Assert.Equal("Part One", copy.Sections.Single().Title);
        Assert.Equal("Roland Ellison", copy.Characters.Single().Name);
        Assert.Contains("It began with a message.", storeB.LoadChapterBody(copy, copy.Chapters[0]));

        using var repoB = BookRepository.TryOpen(result.Folder);
        Assert.NotNull(repoB);
        Assert.Contains("Chapter one retitled", repoB!.History().Select(v => v.Message));
        Assert.Empty(Directory.GetDirectories(Path.Combine(t.Path, "B", "AuthorPlus", ".importing")));
    }

    [Fact]
    public void Export_leaves_out_temp_files_todays_word_count_and_exports()
    {
        using var t = new TempDir();
        var (a, _, carry) = Computers(t);
        var book = SampleBook(new BookStore(a));
        File.WriteAllText(Path.Combine(book.FolderPath, "progress.json"), "{}");
        File.WriteAllText(Path.Combine(book.FolderPath, "book.json.tmp"), "half");
        File.WriteAllText(Path.Combine(book.FolderPath, "The Book of One.docx"), "x");
        File.WriteAllText(Path.Combine(book.FolderPath, "The Book of One.pdf"), "x");

        var file = Path.Combine(carry, "b.authorplus");
        ProjectPackage.Export(book.FolderPath, file, "1.0.0");

        using var zip = ZipFile.OpenRead(file);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("authorplus-project.json", names);
        Assert.Contains("book/book.json", names);
        Assert.Contains(names, n => n.StartsWith("book/chapters/") && n.EndsWith(".xaml"));
        Assert.DoesNotContain(names, n => n.EndsWith("progress.json") || n.EndsWith(".tmp") || n.EndsWith(".docx") || n.EndsWith(".pdf"));
        Assert.False(ProjectPackage.ReadManifest(file).IncludesHistory);   // this book keeps no versions
    }

    [Fact]
    public void Replace_moves_the_old_copy_aside_and_brings_the_newer_work_home()
    {
        using var t = new TempDir();
        var (a, b, carry) = Computers(t);
        var storeA = new BookStore(a);
        var book = SampleBook(storeA);

        // To computer B, written on there, and back.
        var there = Path.Combine(carry, "there.authorplus");
        ProjectPackage.Export(book.FolderPath, there, "1.0.0");
        var onB = ProjectPackage.Import(there, b, ProjectImportMode.KeepBoth).Folder;
        var storeB = new BookStore(b);
        var bookB = storeB.Load(onB);
        bookB.Chapters.Add(new Chapter { Title = "Written on the laptop" });
        storeB.Save(bookB);
        var back = Path.Combine(carry, "back.authorplus");
        ProjectPackage.Export(onB, back, "1.0.0");

        var manifest = ProjectPackage.ReadManifest(back);
        var existing = ProjectPackage.FindExisting(manifest, a);
        Assert.NotNull(existing);
        Assert.Equal(book.FolderPath, existing!.Folder);

        var result = ProjectPackage.Import(back, a, ProjectImportMode.Replace, existing);

        Assert.Equal(book.FolderPath, result.Folder);
        Assert.Equal(new[] { "The Message", "Written on the laptop" }, storeA.Load(result.Folder).Chapters.Select(c => c.Title));
        Assert.NotNull(result.PreviousCopyFolder);
        Assert.StartsWith(Path.Combine(t.Path, "A", "AuthorPlus", "Replaced books"), result.PreviousCopyFolder);
        Assert.Single(storeA.Load(result.PreviousCopyFolder!).Chapters);       // nothing lost: the old copy still opens
        Assert.Single(storeA.ListBooks());                                      // and it is not in the library twice
    }

    [Fact]
    public void Keep_both_adds_a_numbered_folder_beside_the_existing_book()
    {
        using var t = new TempDir();
        var (a, _, carry) = Computers(t);
        var book = SampleBook(new BookStore(a));
        var file = Path.Combine(carry, "p.authorplus");
        ProjectPackage.Export(book.FolderPath, file, "1.0.0");

        var result = ProjectPackage.Import(file, a, ProjectImportMode.KeepBoth);

        Assert.Equal(Path.Combine(a, "The Book of One (2)"), result.Folder);
        Assert.Equal(2, new BookStore(a).ListBooks().Count);
    }

    [Fact]
    public void A_file_that_is_not_a_project_is_refused_in_plain_words_and_nothing_changes()
    {
        using var t = new TempDir();
        var (_, b, carry) = Computers(t);

        var notZip = Path.Combine(carry, "notes.authorplus");
        File.WriteAllText(notZip, "just some text");
        var ex = Assert.Throws<InvalidDataException>(() => ProjectPackage.ReadManifest(notZip));
        Assert.Contains("could not be read as an Author+ project file", ex.Message);

        var otherZip = Path.Combine(carry, "photos.authorplus");
        using (var zip = ZipFile.Open(otherZip, ZipArchiveMode.Create)) zip.CreateEntry("photo.jpg");
        ex = Assert.Throws<InvalidDataException>(() => ProjectPackage.Import(otherZip, b, ProjectImportMode.KeepBoth));
        Assert.Contains("not an Author+ project file", ex.Message);
        Assert.Empty(Directory.GetDirectories(b));
    }

    [Fact]
    public void An_entry_that_climbs_out_of_the_book_folder_is_refused()
    {
        using var t = new TempDir();
        var (a, b, carry) = Computers(t);
        var book = SampleBook(new BookStore(a));
        var file = Path.Combine(carry, "evil.authorplus");
        ProjectPackage.Export(book.FolderPath, file, "1.0.0");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Update))
        using (var w = new StreamWriter(zip.CreateEntry("book/../../escaped.txt").Open())) w.Write("x");

        Assert.Throws<InvalidDataException>(() => ProjectPackage.Import(file, b, ProjectImportMode.KeepBoth));
        Assert.Empty(Directory.GetDirectories(b));
        Assert.False(File.Exists(Path.Combine(t.Path, "B", "escaped.txt")));
    }

    [Fact]
    public void A_project_from_a_newer_format_asks_for_an_update()
    {
        using var t = new TempDir();
        var (a, _, carry) = Computers(t);
        var book = SampleBook(new BookStore(a));
        var file = Path.Combine(carry, "future.authorplus");
        ProjectPackage.Export(book.FolderPath, file, "9.0.0");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Update))
        {
            zip.GetEntry(ProjectPackage.ManifestName)!.Delete();
            using var w = new StreamWriter(zip.CreateEntry(ProjectPackage.ManifestName).Open());
            w.Write($"{{\"FormatVersion\": 99, \"BookId\": \"{book.Id}\", \"Title\": \"x\"}}");
        }

        var ex = Assert.Throws<InvalidDataException>(() => ProjectPackage.ReadManifest(file));
        Assert.Contains("newer version of Author+", ex.Message);
    }

    [Fact]
    public void Saving_with_no_change_keeps_the_fingerprint_and_a_real_edit_changes_it()
    {
        using var t = new TempDir();
        var (a, _, _) = Computers(t);
        var store = new BookStore(a);
        var book = SampleBook(store);
        var before = ProjectPackage.Fingerprint(book.FolderPath);

        Thread.Sleep(20);
        store.Save(book);                                   // a new save time in book.json, nothing else
        File.WriteAllText(Path.Combine(book.FolderPath, "progress.json"), "{\"today\": 500}");
        Assert.Equal(before, ProjectPackage.Fingerprint(book.FolderPath));

        book.Characters[0].Name = "Roland";
        store.Save(book);
        Assert.NotEqual(before, ProjectPackage.Fingerprint(book.FolderPath));
    }

    [Fact]
    public void Compare_tells_the_round_trip_the_stale_file_and_the_clash_apart()
    {
        using var t = new TempDir();
        var (a, b, carry) = Computers(t);
        var storeA = new BookStore(a);
        var book = SampleBook(storeA);
        var logA = new TransferLog(Path.Combine(t.Path, "A", "transfers.json"));

        // Desktop exports; the log remembers where the copies parted.
        var outFile = Path.Combine(carry, "out.authorplus");
        ProjectPackage.Export(book.FolderPath, outFile, "1.0.0");
        var outManifest = ProjectPackage.ReadManifest(outFile);
        logA.Record(book.FolderPath, outManifest.ContentHash);
        ExistingBook Here() => ProjectPackage.FindExisting(outManifest, a)!;

        Assert.Equal(ImportSituation.NewHere, ProjectPackage.Compare(outManifest, null, null));
        Assert.Equal(ImportSituation.Same, ProjectPackage.Compare(outManifest, Here(), logA.LastHash(book.FolderPath)));

        // The laptop writes a chapter and sends it back.
        var storeB = new BookStore(b);
        var onB = storeB.Load(ProjectPackage.Import(outFile, b, ProjectImportMode.KeepBoth).Folder);
        onB.Chapters.Add(new Chapter { Title = "On the laptop" });
        storeB.Save(onB);
        var backFile = Path.Combine(carry, "back.authorplus");
        ProjectPackage.Export(onB.FolderPath, backFile, "1.0.0");
        var back = ProjectPackage.ReadManifest(backFile);

        storeA.Save(book);                                  // a re-save here is not writing
        Assert.Equal(ImportSituation.FileIsNewer, ProjectPackage.Compare(back, Here(), logA.LastHash(book.FolderPath)));

        // The desktop's own old export, after writing here: the file is the older one.
        book.Chapters.Add(new Chapter { Title = "On the desktop" });
        storeA.Save(book);
        Assert.Equal(ImportSituation.FileIsOlder, ProjectPackage.Compare(outManifest, Here(), logA.LastHash(book.FolderPath)));

        // Written on both: a clash.
        Assert.Equal(ImportSituation.BothChanged, ProjectPackage.Compare(back, Here(), logA.LastHash(book.FolderPath)));

        // No record here: only the dates to go on.
        Assert.Equal(ImportSituation.ProbablyOlder, ProjectPackage.Compare(outManifest, Here(), null));
    }

    [Fact]
    public void FindAll_lists_every_copy_after_keep_both_newest_first()
    {
        using var t = new TempDir();
        var (a, _, carry) = Computers(t);
        var store = new BookStore(a);
        var book = SampleBook(store);
        var file = Path.Combine(carry, "p.authorplus");
        ProjectPackage.Export(book.FolderPath, file, "1.0.0");
        var second = ProjectPackage.Import(file, a, ProjectImportMode.KeepBoth).Folder;
        Thread.Sleep(20);
        var copy = store.Load(second); store.Save(copy);

        var all = ProjectPackage.FindAll(ProjectPackage.ReadManifest(file), a);

        Assert.Equal(new[] { second, book.FolderPath }, all.Select(c => c.Folder));
    }

    [Fact]
    public void The_transfer_log_is_per_folder_and_survives_a_missing_or_broken_file()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "log", "transfers.json");
        var log = new TransferLog(path);
        Assert.Null(log.LastHash(@"C:\Books\One"));
        log.Record(@"C:\Books\One", "AAA");
        log.Record(@"C:\Books\Two", "BBB");
        Assert.Equal("AAA", new TransferLog(path).LastHash(@"c:\books\one\"));
        File.WriteAllText(path, "not json");
        Assert.Null(new TransferLog(path).LastHash(@"C:\Books\One"));
    }
}
