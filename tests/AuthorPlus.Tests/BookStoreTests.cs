using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;

namespace AuthorPlus.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "authorplus-tests", Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
}

public class BookStoreTests
{
    [Fact]
    public void Create_makes_a_folder_with_book_json_and_a_safe_unique_name()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);

        var a = store.Create("The Long Road: Part 1?", "Ron");
        var b = store.Create("The Long Road: Part 1?", "Ron");

        Assert.EndsWith("The Long Road- Part 1-", a.FolderPath);
        Assert.EndsWith("The Long Road- Part 1- (2)", b.FolderPath);
        Assert.True(File.Exists(Path.Combine(a.FolderPath, BookStore.BookFileName)));
        Assert.Equal("Ron", store.Load(a.FolderPath).Author);
    }

    [Fact]
    public void Save_and_Load_round_trip_every_collection_in_order_with_one_file_per_item()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);
        var book = store.Create("Round Trip");

        var c1 = new Chapter { Title = "One", Status = ChapterStatus.Draft };
        var c2 = new Chapter { Title = "Two" };
        var hero = new Character { Name = "Mara", Motivations = "to get home" };
        book.Chapters.AddRange(new[] { c1, c2 });
        book.Characters.Add(hero);
        book.Timeline.Add(new TimelineEvent { Order = 1, When = "Day 1", Title = "Departure", CharacterIds = { hero.Id }, ChapterIds = { c1.Id } });
        var main = new Plotline { Name = "Main", Convergences = { new PlotlineConvergence { OtherPlotlineId = Guid.NewGuid(), Note = "meets" } } };
        main.ChapterIds.Add(c1.Id);                       // a thread's status follows its chapters: one chapter makes it active
        book.Plotlines.Add(main);
        book.SeedRoles(main);
        c1.PovCharacterId = hero.Id;
        store.SaveChapterBody(book, c1, "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph>Hello world</Paragraph></FlowDocument>", 2);
        store.Save(book);

        Assert.Equal(2, Directory.GetFiles(Path.Combine(book.FolderPath, "chapters"), "*.json").Length);
        Assert.Single(Directory.GetFiles(Path.Combine(book.FolderPath, "chapters"), "*.xaml"));

        var loaded = store.Load(book.FolderPath);
        Assert.Equal(new[] { "One", "Two" }, loaded.Chapters.Select(c => c.Title));
        Assert.Equal(ChapterStatus.Draft, loaded.Chapters[0].Status);
        Assert.Equal(2, loaded.Chapters[0].WordCount);
        Assert.Equal(hero.Id, loaded.Chapters[0].PovCharacterId);
        Assert.Equal("to get home", loaded.Characters.Single().Motivations);
        Assert.Equal("Day 1", loaded.Timeline.Single().When);
        Assert.Contains(hero.Id, loaded.Timeline.Single().CharacterIds);
        Assert.Equal(PlotlineStatus.Active, loaded.Plotlines.Single().Status);
        Assert.Equal(PlotlineRole.Introduced, loaded.Plotlines.Single().RoleIn(loaded.Chapters[0].Id));
        Assert.Equal("meets", loaded.Plotlines.Single().Convergences.Single().Note);
        Assert.Contains("Hello world", store.LoadChapterBody(loaded, loaded.Chapters[0]));
        Assert.Null(store.LoadChapterBody(loaded, loaded.Chapters[1]));
    }

    [Fact]
    public void Reordering_persists_through_book_json_not_file_order()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);
        var book = store.Create("Order");
        book.Chapters.AddRange(new[] { new Chapter { Title = "A" }, new Chapter { Title = "B" }, new Chapter { Title = "C" } });
        store.Save(book);

        (book.Chapters[0], book.Chapters[2]) = (book.Chapters[2], book.Chapters[0]);
        store.Save(book);

        Assert.Equal(new[] { "C", "B", "A" }, store.Load(book.FolderPath).Chapters.Select(c => c.Title));
    }

    [Fact]
    public void Removing_an_item_deletes_its_files_on_save_including_a_chapter_body()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);
        var book = store.Create("Delete");
        var gone = new Chapter { Title = "Gone" };
        var keep = new Chapter { Title = "Keep" };
        book.Chapters.AddRange(new[] { gone, keep });
        store.SaveChapterBody(book, gone, "<FlowDocument/>", 0);
        store.Save(book);
        var goneBody = store.ChapterBodyPath(book, gone);
        Assert.True(File.Exists(goneBody));

        book.Chapters.Remove(gone);
        store.Save(book);

        Assert.False(File.Exists(goneBody));
        Assert.Single(Directory.GetFiles(Path.Combine(book.FolderPath, "chapters"), "*.json"));
        Assert.Equal("Keep", store.Load(book.FolderPath).Chapters.Single().Title);
    }

    [Fact]
    public void A_corrupt_item_file_loses_only_that_item()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);
        var book = store.Create("Corrupt");
        book.Characters.AddRange(new[] { new Character { Name = "Good" }, new Character { Name = "Bad" } });
        store.Save(book);
        var badFile = Path.Combine(book.FolderPath, "characters", book.Characters[1].Id.ToString("N") + ".json");
        File.WriteAllText(badFile, "{ not json");

        var loaded = store.Load(book.FolderPath);

        Assert.Equal("Good", loaded.Characters.Single().Name);
    }

    [Fact]
    public void ListBooks_reports_titles_newest_first_and_ignores_stray_folders()
    {
        using var t = new TempDir();
        var store = new BookStore(t.Path);
        var old = store.Create("Older");
        File.SetLastWriteTimeUtc(Path.Combine(old.FolderPath, BookStore.BookFileName), DateTime.UtcNow.AddDays(-1));
        Directory.CreateDirectory(Path.Combine(t.Path, "not a book"));
        Thread.Sleep(20);
        store.Create("Newer");

        var list = store.ListBooks();

        Assert.Equal(new[] { "Newer", "Older" }, list.Select(b => b.Title));
    }

    [Theory]
    [InlineData("", "Untitled")]
    [InlineData("   ", "Untitled")]
    [InlineData("A/B\\C:D", "A-B-C-D")]
    [InlineData("Ends with dot.", "Ends with dot")]
    public void SafeFolderName_strips_invalid_characters(string title, string expected) =>
        Assert.Equal(expected, BookStore.SafeFolderName(title));
}
