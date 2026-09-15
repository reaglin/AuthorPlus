using AuthorPlus.Core.Models;
using AuthorPlus.Core.Services;
using AuthorPlus.Core.Services.Versioning;

namespace AuthorPlus.Tests;

/// <summary>
/// The book folder as a git repository: saving a version, the history, restoring, and the plain
/// English the author reads instead of file names. Everything here runs against a real
/// repository in a temp folder — no GitHub, no network.
/// </summary>
public class VersioningTests
{
    private static readonly VersionAuthor Ron = new("Ron Eaglin", "ron@example.com");

    private static (BookStore Store, Book Book) NewBook(TempDir t, string title = "Versioned")
    {
        var store = new BookStore(t.Path);
        var book = store.Create(title, "Ron Eaglin");
        return (store, book);
    }

    private static void Write(BookStore store, Book book, Chapter chapter, string text)
    {
        var xaml = "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
                   string.Concat(text.Split('\n').Select(p => $"<Paragraph>{p}</Paragraph>")) +
                   "</FlowDocument>";
        store.SaveChapterBody(book, chapter, xaml, WordCounter.Count(text));
    }

    [Fact]
    public void Starting_versioning_makes_a_main_branch_with_the_whole_book_in_the_first_version()
    {
        using var t = new TempDir();
        var (store, book) = NewBook(t);
        var chapter = new Chapter { Title = "The Gate" };
        book.Chapters.Add(chapter);
        Write(store, book, chapter, "It began at the gate.");
        store.Save(book);

        Assert.False(BookRepository.IsVersioned(book.FolderPath));
        using var repo = BookRepository.Initialize(book.FolderPath, Ron);

        Assert.True(BookRepository.IsVersioned(book.FolderPath));
        var history = repo.History();
        var first = Assert.Single(history);
        Assert.Equal("The book as it is today", first.Message);
        Assert.Equal("Ron Eaglin", first.AuthorName);
        Assert.False(repo.HasPendingChanges());

        // The prose is in that version, and so are the two settings files.
        Assert.Contains("It began at the gate.", repo.ReadTextAt(first.Sha, ChangeNames.ChapterTextPath(chapter)));
        Assert.Contains("progress.json", repo.ReadTextAt(first.Sha, ".gitignore"));
        Assert.Contains("* -text", repo.ReadTextAt(first.Sha, ".gitattributes"));
    }

    [Fact]
    public void A_version_is_only_saved_when_something_changed()
    {
        using var t = new TempDir();
        var (store, book) = NewBook(t);
        book.Chapters.Add(new Chapter { Title = "One" });
        store.Save(book);
        using var repo = BookRepository.Initialize(book.FolderPath, Ron);

        Assert.Null(repo.SaveVersion("nothing has happened", Ron));

        book.Chapters.Add(new Chapter { Title = "Two" });
        store.Save(book);
        Assert.True(repo.HasPendingChanges());
        var saved = repo.SaveVersion("Chapter two started", Ron);

        Assert.NotNull(saved);
        Assert.Equal(new[] { "Chapter two started", "The book as it is today" }, repo.History().Select(v => v.Message));
    }

    [Fact]
    public void Pending_changes_and_the_changes_in_a_version_are_named_as_the_author_knows_them()
    {
        using var t = new TempDir();
        var (store, book) = NewBook(t);
        var part = new Section { Title = "Part One" };
        book.Sections.Add(part);
        var chapter = new Chapter { Title = "The Interrogation", SectionId = part.Id };
        book.Chapters.Add(chapter);
        store.Save(book);
        using var repo = BookRepository.Initialize(book.FolderPath, Ron);

        Write(store, book, chapter, "They asked him twice.");
        book.Characters.Add(new Character { Name = "Edmund Blackwood" });
        book.SetSummary(chapter, "He is questioned.");
        store.Save(book);

        var pending = repo.PendingChanges(book);
        Assert.Contains(pending, c => c.Description == "Part One, Chapter 1 — The Interrogation (the text)" && c.Kind == BookChangeKind.Added);
        Assert.Contains(pending, c => c.Description == "Character: Edmund Blackwood");
        Assert.Contains(pending, c => c.Description == "Summary of Part One, Chapter 1 — The Interrogation");
        Assert.Contains(pending, c => c.Path == "book.json");
        Assert.DoesNotContain(pending, c => c.Path == "progress.json");   // ignored: this computer's bookkeeping

        var version = repo.SaveVersion(ChangeNames.Suggest(pending), Ron)!;
        var changed = repo.ChangesIn(version.Sha, book);
        Assert.Contains(changed, c => c.Description == "Character: Edmund Blackwood" && c.Kind == BookChangeKind.Added);
        Assert.Equal(pending.Count, changed.Count);
    }

    [Fact]
    public void A_removed_chapter_is_still_named_in_the_version_that_removed_it()
    {
        using var t = new TempDir();
        var (store, book) = NewBook(t);
        var chapter = new Chapter { Title = "The Deleted Scene" };
        book.Chapters.Add(chapter);
        Write(store, book, chapter, "Cut this.");
        store.Save(book);
        using var repo = BookRepository.Initialize(book.FolderPath, Ron);

        book.RemoveChapter(chapter);
        store.Save(book);
        File.Delete(Path.Combine(book.FolderPath, "chapters", chapter.Id.ToString("N") + ".xaml"));
        var version = repo.SaveVersion("Cut a chapter", Ron)!;

        var changes = repo.ChangesIn(version.Sha, book);
        // The chapter is gone from the book, so its name comes from its own record at the version before.
        Assert.Contains(changes, c => c.Kind == BookChangeKind.Removed && c.Description.Contains("The Deleted Scene", StringComparison.Ordinal));
    }

    [Fact]
    public void Restoring_puts_the_book_back_without_losing_what_came_after()
    {
        using var t = new TempDir();
        var (store, book) = NewBook(t);
        var chapter = new Chapter { Title = "One" };
        book.Chapters.Add(chapter);
        Write(store, book, chapter, "The first draft.");
        store.Save(book);
        using var repo = BookRepository.Initialize(book.FolderPath, Ron);
        var draft = repo.History().Single();

        Write(store, book, chapter, "A rewrite that went wrong.");
        store.Save(book);
        repo.SaveVersion("A rewrite", Ron);

        repo.Restore(draft.Sha, Ron);

        Assert.Contains("The first draft.", store.LoadChapterBody(book, chapter));
        var history = repo.History();
        Assert.Equal(3, history.Count);                                   // nothing was thrown away
        Assert.StartsWith("Restored the version from", history[0].Message);
        Assert.Equal("A rewrite", history[1].Message);
        Assert.False(repo.HasPendingChanges());                            // the folder matches the newest version
        Assert.Contains("A rewrite that went wrong.", repo.ReadTextAt(history[1].Sha, ChangeNames.ChapterTextPath(chapter)));
    }

    [Fact]
    public void Unsaved_work_is_kept_as_its_own_version_before_a_restore()
    {
        using var t = new TempDir();
        var (store, book) = NewBook(t);
        var chapter = new Chapter { Title = "One" };
        book.Chapters.Add(chapter);
        Write(store, book, chapter, "Draft.");
        store.Save(book);
        using var repo = BookRepository.Initialize(book.FolderPath, Ron);
        var draft = repo.History().Single();

        Write(store, book, chapter, "Words written this morning.");
        store.Save(book);                                                  // saved to disk, never versioned

        repo.Restore(draft.Sha, Ron);

        var history = repo.History();
        Assert.Contains(history, v => v.Message.StartsWith("Work in progress", StringComparison.Ordinal));
        Assert.Contains("Words written this morning.",
            repo.ReadTextAt(history.First(v => v.Message.StartsWith("Work in progress", StringComparison.Ordinal)).Sha,
                            ChangeNames.ChapterTextPath(chapter)));
    }

    [Fact]
    public void A_chapters_own_history_is_the_versions_that_touched_it()
    {
        using var t = new TempDir();
        var (store, book) = NewBook(t);
        var one = new Chapter { Title = "One" };
        var two = new Chapter { Title = "Two" };
        book.Chapters.AddRange(new[] { one, two });
        Write(store, book, one, "First.");
        Write(store, book, two, "Second.");
        store.Save(book);
        using var repo = BookRepository.Initialize(book.FolderPath, Ron);

        Write(store, book, two, "Second, revised.");
        store.Save(book);
        repo.SaveVersion("Chapter two revised", Ron);

        var chapterOne = repo.HistoryOf(ChangeNames.ChapterTextPath(one));
        var chapterTwo = repo.HistoryOf(ChangeNames.ChapterTextPath(two));

        Assert.Single(chapterOne);
        Assert.Equal(2, chapterTwo.Count);
        Assert.Equal("Chapter two revised", chapterTwo[0].Message);
    }

    [Fact]
    public void Two_versions_of_a_chapter_compare_paragraph_by_paragraph_in_the_prose()
    {
        using var t = new TempDir();
        var (store, book) = NewBook(t);
        var chapter = new Chapter { Title = "One" };
        book.Chapters.Add(chapter);
        Write(store, book, chapter, "He walked in.\nShe said nothing.\nThe door closed.");
        store.Save(book);
        using var repo = BookRepository.Initialize(book.FolderPath, Ron);
        var before = repo.History().Single();

        Write(store, book, chapter, "He walked in.\nShe said nothing at all.\nThe door closed.");
        store.Save(book);
        var after = repo.SaveVersion("A line reworked", Ron)!;

        var path = ChangeNames.ChapterTextPath(chapter);
        var lines = TextDiff.Chapters(repo.ReadTextAt(before.Sha, path), repo.ReadTextAt(after.Sha, path));

        Assert.Equal((1, 1), TextDiff.Counts(lines));
        Assert.Contains(lines, l => l.Kind == DiffKind.Removed && l.Text == "She said nothing.");
        Assert.Contains(lines, l => l.Kind == DiffKind.Added && l.Text == "She said nothing at all.");
        Assert.Equal(2, lines.Count(l => l.Kind == DiffKind.Equal));
    }

    [Fact]
    public void The_suggested_version_message_says_what_changed()
    {
        var one = new BookChange(BookChangeKind.Edited, "chapters/a.xaml", "Chapter 12 — The Interrogation (the text)");
        var meta = new BookChange(BookChangeKind.Edited, "book.json", "The book's details and the order of everything in it");
        Assert.Equal("Chapter 12 — The Interrogation revised", ChangeNames.Suggest(new[] { one, meta }));

        var character = new BookChange(BookChangeKind.Added, "characters/b.json", "Character: Edmund Blackwood");
        Assert.Equal("Character: Edmund Blackwood added", ChangeNames.Suggest(new[] { character, meta }));

        var another = new BookChange(BookChangeKind.Edited, "chapters/c.xaml", "Chapter 13 — The Road (the text)");
        Assert.Equal("2 chapters and 1 character changed", ChangeNames.Suggest(new[] { one, another, character, meta }));

        Assert.Equal("Saved a version", ChangeNames.Suggest(new[] { meta }));
    }

    [Fact]
    public void A_book_folder_that_is_not_versioned_opens_as_nothing()
    {
        using var t = new TempDir();
        var (_, book) = NewBook(t);
        Assert.Null(BookRepository.TryOpen(book.FolderPath));
        Assert.False(BookRepository.IsVersioned(book.FolderPath));
    }

    [Fact]
    public void The_github_sign_in_round_trips_and_can_be_forgotten()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "github.json");

        var account = GitHubAccount.Load(path);
        Assert.False(account.IsSignedIn);
        Assert.Null(account.Credentials);

        account.Save("reaglin", "ghp_example_token");
        var reloaded = GitHubAccount.Load(path);

        Assert.True(reloaded.IsSignedIn);
        Assert.Equal("reaglin", reloaded.Credentials!.Login);
        Assert.Equal("ghp_example_token", reloaded.Credentials.Token);
        Assert.DoesNotContain("ghp_example_token", File.ReadAllText(path), StringComparison.Ordinal);   // never in the clear

        reloaded.Forget();
        Assert.False(GitHubAccount.Load(path).IsSignedIn);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("The Book of One", "the-book-of-one")]
    [InlineData("Hydrology: Water Quantity & Quality", "hydrology-water-quantity-quality")]
    [InlineData("  ...  ", "my-book")]
    public void A_book_title_becomes_a_repository_name_github_will_take(string title, string expected) =>
        Assert.Equal(expected, GitHubApi.SuggestRepositoryName(title));
}
